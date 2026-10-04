using Vaulta.App.Core.Catalog;
using Vaulta.App.Core.Vision;
using Vaulta.Vision.Contracts;
namespace Vaulta.App.Views;
public sealed partial class ScannerSessionPage
{
 private readonly IVisionClient _vision;
 private readonly VisionCaptureArchive _captureArchive;
 private Task<bool>? _archiveTask;
 private VisionPoliciesDto? _historyPolicies;
 private string HistoryKey=>"vision-consent-"+_account.User?.Id.ToString("N");
 private VisionParticipation Participation=>new(Preferences.Default.Get(HistoryKey+"-ops",null as string),Preferences.Default.Get(HistoryKey+"-improve",null as string));
 private async Task RecordHumanReview(VisionScanResultDto result,ScannerSessionCard card,ScannerOperation operation,Task<bool>? archive)
 {
  if(result.History is not {RunId:{} run,PersistenceStatus:"saved" or "replayed"} trace)return;
  try
  {
   if(archive is not null)await archive.WaitAsync(operation.Context.Token);
   RequireScannerOperation(operation);
   var source=result.PrintingId==card.PrintingId && result.VariantId==card.VariantId?"UserConfirmation":"UserCorrection";
   var feedback=await _vision.FeedbackAsync(trace.AttemptId,Guid.NewGuid().ToString("N"),new(run,source,card.PrintingId,card.VariantId,"front","card-present"),operation.Context.Token);
   RequireScannerOperation(operation); ShowImprovementNotice(feedback);
  }
  catch(OperationCanceledException) { }
  catch(Exception) { if(_visible && _viewGeneration==operation.Context.Generation && _session?.Id==operation.Session.Id && _account.User?.Id==operation.Session.OwnerId) _status.Text="Carta salva · o registro da confirmação ficou pendente."; }
 }
 private void ShowImprovementNotice(VisionFeedbackDto feedback)
 {
  _status.Text=feedback.Improvement?.Status switch
  {
   "promoted"=>"Exemplo confirmado · memória de desenvolvimento atualizando em instantes.",
   "awaiting_verification"=>"Confirmação registrada · a foto ou o rótulo aguardam verificação.",
   "not_eligible"=>"Confirmação registrada · esta captura ainda não pode entrar na memória.",
   _=>"Confirmação registrada · exemplos aguardam revisão antes de entrar na memória."
  };
 }
 private async Task ConfigureHistory()
 {
  RequireOwner();_historyPolicies=await _vision.PoliciesAsync(_lifetime.Token);RequireOwner();
  if(!_historyPolicies.Enabled){await DisplayAlertAsync("Histórico","A coleta de fotos ainda não está habilitada neste ambiente.","Entendi");return;}
  var choice=await DisplayActionSheetAsync("Histórico e melhoria","Voltar",null,"Ativar histórico com fotos","Contribuir com exemplos revisados","Desativar novos registros");RequireOwner();
  if(choice=="Desativar novos registros"){Preferences.Default.Remove(HistoryKey+"-ops");Preferences.Default.Remove(HistoryKey+"-improve");return;}
  if(choice is not ("Ativar histórico com fotos" or "Contribuir com exemplos revisados"))return;
  var improve=choice=="Contribuir com exemplos revisados";
  var accepted=await DisplayAlertAsync("Permitir fotos privadas?",$"Guardar as fotos identificadas e as correções por {_historyPolicies.RetentionDays} dias para revisar suas leituras."+(improve?" Exemplos que você confirmar e que forem revisados também poderão melhorar o reconhecimento.":""),"Permitir","Agora não");RequireOwner();
  if(!accepted)return;Preferences.Default.Set(HistoryKey+"-ops",_historyPolicies.OperationalPolicyVersion);
  if(improve)Preferences.Default.Set(HistoryKey+"-improve",_historyPolicies.ImprovementPolicyVersion);else Preferences.Default.Remove(HistoryKey+"-improve");
 }
 private async Task<CreateScanAttemptResponse?> ReserveHistory(CancellationToken ct)
 {
  if(Participation.OperationalPolicyVersion is null)return null;
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(2));
  try
  {
   _historyPolicies=await _vision.PoliciesAsync(timeout.Token);
   var participation=Participation;if(!participation.CanStore(_historyPolicies))return null;
   return await _vision.CreateAsync(new(participation.OperationalPolicyVersion!,participation.CanImprove(_historyPolicies)?participation.ImprovementPolicyVersion:null,_session?.Id),timeout.Token);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch(Exception){return null;}
 }
 private void AddHistoryActions(VerticalStackLayout row,ScannerSessionCard card)
 {
  if(card.History is not {RunId:{ } run,PersistenceStatus:"saved" or "replayed"} trace)return;
  row.Children.Add(Action("Confirmar reconhecimento",async()=>
  {var operation=BeginScannerOperation();var feedback=await _vision.FeedbackAsync(trace.AttemptId,Guid.NewGuid().ToString("N"),new(run,"UserConfirmation",card.PrintingId,card.VariantId,"front","card-present"),operation.Context.Token);RequireScannerOperation(operation);ShowImprovementNotice(feedback);}));
  row.Children.Add(Action("Corrigir reconhecimento",async()=>
  {
   var operation=BeginScannerOperation();var query=await DisplayPromptAsync("Corrigir carta","Digite o nome correto.","Buscar","Voltar");RequireScannerOperation(operation);if(string.IsNullOrWhiteSpace(query))return;
   var candidates=await _scanner.SearchCardsAsync(query,null,operation.Context.Token);RequireScannerOperation(operation);
   var labels=candidates.Candidates.Select(x=>$"{x.Name} · {x.SetName} · {x.CollectorNumber}").ToArray();if(labels.Length==0){_status.Text="Carta ainda fora do catálogo.";return;}
   var selected=await DisplayActionSheetAsync("Escolha a impressão correta","Voltar",null,labels);RequireScannerOperation(operation);var index=Array.IndexOf(labels,selected);if(index<0)return;
   var details=await _scanner.GetCardDetailsAsync(candidates.Candidates[index].PrintingId,operation.Context.Token);RequireScannerOperation(operation);
   var variantLabel=await DisplayActionSheetAsync("Acabamento e edição","Voltar",null,details.Printing.Variants.Select(x=>x.Name).Append("Não sei o acabamento").ToArray());RequireScannerOperation(operation);if(variantLabel=="Voltar" || variantLabel is null)return;
   var variant=details.Printing.Variants.FirstOrDefault(x=>x.Name==variantLabel);
   await _vision.FeedbackAsync(trace.AttemptId,Guid.NewGuid().ToString("N"),new(run,"UserCorrection",details.Printing.PrintingId,variant?.Id,"front","card-present"),operation.Context.Token);RequireScannerOperation(operation);
   var quote=card.VisualIdentification?.Certification is null ? details.MarketQuotes.FirstOrDefault(x=>x.VariantId==variant?.Id):null;
   var corrected=card with {PrintingId=details.Printing.PrintingId,VariantId=variant?.Id,Name=details.Printing.CardName,SetName=details.Printing.SetName,CollectorNumber=details.Printing.CollectorNumber,VariantName=variant?.Name??"Acabamento pendente",ArtworkUrl=details.Printing.ArtworkUrl,MarketValue=quote is null?null:new(decimal.Round(quote.MarketValueBrl,2),quote.Source,quote.UpdatedAt,quote.OriginalValue,quote.OriginalCurrency,quote.ExchangeRate,quote.ExchangeRateAt)};
   var updated=_session!.Correct(corrected);await _store.Save(updated,operation.Context.Token);RequireScannerOperation(operation);_session=updated;ShowReview();_status.Text="Correção registrada e total atualizado. A leitura original foi preservada no histórico.";
  }));
  row.Children.Add(Action("Excluir foto e histórico",async()=>
  {RequireOwner();await _vision.DeleteAsync(trace.AttemptId,_lifetime.Token);RequireOwner();_status.Text="Exclusão solicitada. A carta continua na sessão.";}));
 }
}
