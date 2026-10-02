using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vaulta.App.Core.Identity;
using Vaulta.App.Services.Authentication;
using Vaulta.Identity.Contracts;

namespace Vaulta.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly LoginFlow _flow;
    private bool _active;

    public LoginViewModel(IAuthenticationService authenticationService)
    {
        _flow = new LoginFlow((email, password, token) => authenticationService.LoginAsync(new LoginRequest(email, password), token));
        _flow.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyState);
    }

    [ObservableProperty] private string email = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract)), NotifyPropertyChangedFor(nameof(LoginButtonText))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPasswordHidden)), NotifyPropertyChangedFor(nameof(PasswordToggleText))]
    private bool isPasswordVisible;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEmailError))] private string? emailError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPasswordError))] private string? passwordError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string? statusMessage;

    public bool CanInteract => !IsBusy;
    public bool IsPasswordHidden => !IsPasswordVisible;
    public string PasswordToggleText => IsPasswordVisible ? "Ocultar" : "Mostrar";
    public string LoginButtonText => IsBusy ? "Entrando…" : "Entrar";
    public bool HasEmailError => !string.IsNullOrEmpty(EmailError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);
    public bool HasMessage => !string.IsNullOrEmpty(StatusMessage);

    public void Activate() { _active = true; ApplyState(); }
    public void Deactivate() { _active = false; _flow.CancelPending(); Password = ""; IsPasswordVisible = false; }
    partial void OnEmailChanged(string value) { EmailError = null; StatusMessage = null; }
    partial void OnPasswordChanged(string value) { PasswordError = null; StatusMessage = null; }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!CanInteract) return;
        var success = await _flow.SubmitAsync(Email, Password);
        ApplyState();
        if (!success || !_active) return;
        Password = "";
        IsPasswordVisible = false;
        try { await Shell.Current.GoToAsync("//main/home/home-page"); }
        catch (Exception) { StatusMessage = "Você entrou, mas não foi possível abrir o início. Tente novamente."; }
    }

    [RelayCommand] private void TogglePassword() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void RecoverPassword()
    {
        // The existing API has no recovery endpoint. Never claim an email was sent.
        StatusMessage = "A recuperação de senha ainda não está disponível no aplicativo.";
        Announce(StatusMessage);
    }

    [RelayCommand]
    private async Task SignUpAsync()
    {
        if (!CanInteract) return;
        try { await Shell.Current.GoToAsync("experience?screen=signup"); }
        catch (Exception) { StatusMessage = "Não foi possível abrir o cadastro. Tente novamente."; }
    }

    private void ApplyState()
    {
        IsBusy = _flow.State.IsBusy;
        EmailError = _flow.State.EmailError;
        PasswordError = _flow.State.PasswordError;
        StatusMessage = _flow.State.Message;
        if (_active && !IsBusy) Announce(StatusMessage ?? EmailError ?? PasswordError);
    }

    private static void Announce(string? message)
    {
        if (!string.IsNullOrEmpty(message)) SemanticScreenReader.Default.Announce(message);
    }
}
