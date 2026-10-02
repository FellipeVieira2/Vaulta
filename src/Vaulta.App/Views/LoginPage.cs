using Vaulta.App.ViewModels;
using Vaulta.App.Views.Components;

namespace Vaulta.App.Views;

/// <summary>Native email/password login, based on Figma 29:153.</summary>
public sealed class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Entrar";
        BackgroundColor = MarketplaceTheme.Background;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        var content = new VerticalStackLayout { Padding = new Thickness(24, 36, 24, 24), Spacing = 0 };
        var brand = MarketplaceTheme.Text("VAULTA", 22, bold: true);
        brand.TextColor = MarketplaceTheme.Brand;
        brand.Margin = new Thickness(0, 0, 0, 64);
        content.Children.Add(brand);
        var title = MarketplaceTheme.Text("Bem-vindo de volta", 28, bold: true);
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        content.Children.Add(title);
        var description = MarketplaceTheme.Text("Acesse sua coleção e continue de onde parou.", 14, secondary: true);
        description.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(description);

        var email = new AuthenticationField("E-mail", "voce@exemplo.com", nameof(LoginViewModel.Email),
            nameof(LoginViewModel.EmailError), nameof(LoginViewModel.HasEmailError)) { Margin = new Thickness(0, 56, 0, 0) };
        email.Input.Keyboard = Keyboard.Email;
        email.Input.ReturnType = ReturnType.Next;
        email.Input.MaxLength = 320;
        var password = new AuthenticationField("Senha", "Sua senha", nameof(LoginViewModel.Password),
            nameof(LoginViewModel.PasswordError), nameof(LoginViewModel.HasPasswordError)) { Margin = new Thickness(0, 24, 0, 0) };
        password.Input.ReturnType = ReturnType.Go;
        password.Input.MaxLength = 1024;
        password.Input.SetBinding(Entry.IsPasswordProperty, nameof(LoginViewModel.IsPasswordHidden));
        password.Input.SetBinding(Entry.ReturnCommandProperty, nameof(LoginViewModel.LoginCommand));
        email.Input.Completed += (_, _) => password.Input.Focus();
        var toggle = MarketplaceTheme.Action("Mostrar");
        toggle.FontSize = 12;
        toggle.MinimumWidthRequest = 72;
        toggle.SetBinding(Button.TextProperty, nameof(LoginViewModel.PasswordToggleText));
        toggle.SetBinding(Button.CommandProperty, nameof(LoginViewModel.TogglePasswordCommand));
        SemanticProperties.SetHint(toggle, "Alterna a visibilidade da senha digitada");
        password.InputLayout.Add(toggle, 1);
        email.SetBinding(IsEnabledProperty, nameof(LoginViewModel.CanInteract));
        password.SetBinding(IsEnabledProperty, nameof(LoginViewModel.CanInteract));
        content.Children.Add(email);
        content.Children.Add(password);

        var recover = MarketplaceTheme.Action("Esqueci minha senha");
        recover.HorizontalOptions = LayoutOptions.End;
        recover.Margin = new Thickness(0, 8, 0, 8);
        recover.SetBinding(Button.CommandProperty, nameof(LoginViewModel.RecoverPasswordCommand));
        recover.SetBinding(IsEnabledProperty, nameof(LoginViewModel.CanInteract));
        content.Children.Add(recover);

        var message = MarketplaceTheme.Text(null, 13);
        message.Margin = new Thickness(0, 0, 0, 16);
        message.SetBinding(Label.TextProperty, nameof(LoginViewModel.StatusMessage));
        message.SetBinding(IsVisibleProperty, nameof(LoginViewModel.HasMessage));
        content.Children.Add(message);

        var submit = MarketplaceTheme.Action("Entrar");
        submit.BackgroundColor = MarketplaceTheme.Brand;
        submit.TextColor = MarketplaceTheme.Background;
        submit.SetBinding(Button.TextProperty, nameof(LoginViewModel.LoginButtonText));
        submit.SetBinding(Button.CommandProperty, nameof(LoginViewModel.LoginCommand));
        submit.SetBinding(IsEnabledProperty, nameof(LoginViewModel.CanInteract));
        var spinner = new ActivityIndicator { Color = MarketplaceTheme.Background, WidthRequest = 18, HeightRequest = 18, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(24, 0) };
        spinner.SetBinding(ActivityIndicator.IsRunningProperty, nameof(LoginViewModel.IsBusy));
        spinner.SetBinding(IsVisibleProperty, nameof(LoginViewModel.IsBusy));
        var submitLayout = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        submitLayout.Add(submit);
        submitLayout.Add(spinner);
        content.Children.Add(submitLayout);

        var signupNote = MarketplaceTheme.Text("Ainda não tem conta?", 13, secondary: true);
        signupNote.HorizontalTextAlignment = TextAlignment.Center;
        signupNote.Margin = new Thickness(0, 32, 0, 16);
        content.Children.Add(signupNote);
        var signup = MarketplaceTheme.Action("Criar conta");
        signup.BackgroundColor = MarketplaceTheme.Surface;
        signup.TextColor = MarketplaceTheme.Primary;
        signup.BorderColor = MarketplaceTheme.Border;
        signup.BorderWidth = 1;
        signup.SetBinding(Button.CommandProperty, nameof(LoginViewModel.SignUpCommand));
        signup.SetBinding(IsEnabledProperty, nameof(LoginViewModel.CanInteract));
        content.Children.Add(signup);

        var footer = MarketplaceTheme.Text("Sua coleção. Seu próximo capítulo.", 11, secondary: true);
        footer.HorizontalTextAlignment = TextAlignment.Center;
        footer.Margin = new Thickness(0, 80, 0, 24);
        content.Children.Add(footer);
        Content = new ScrollView { Content = content };
    }

    protected override void OnAppearing() { base.OnAppearing(); _viewModel.Activate(); }
    protected override void OnDisappearing() { _viewModel.Deactivate(); base.OnDisappearing(); }
}
