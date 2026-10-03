using Vaulta.Catalog.Application;
using Vaulta.Catalog.Contracts;
namespace Vaulta.Vision.Application;
public sealed record VisionCatalogVariant(Guid Id,string Code,string Name,string? Surface,string? Edition);
public sealed record VisionCatalogPrinting(CatalogPrintingDetails Printing,int? Hp,string? Stage,string? CardType,IReadOnlyList<VisionCatalogVariant> Variants,IReadOnlyList<string>? SetNameAliases=null);
public sealed record VisionResolution(string Status,Guid? PrintingId,double PrintingConfidence,Guid? VariantId,double VariantConfidence,string? ReviewReason);
