using FluentValidation;
using Vaulta.Identity.Application.Commands;

namespace Vaulta.Identity.Application;

public sealed class RegisterValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Request.Username).NotEmpty().Matches("^[a-zA-Z0-9_]{3,30}$");
        RuleFor(x => x.Request.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Password).NotEmpty().SetValidator(new PasswordValidator());
    }
}
public sealed class PasswordValidator : AbstractValidator<string>
{
    public PasswordValidator()
    {
        RuleFor(x => x).NotEmpty().MinimumLength(12).MaximumLength(128)
            .Must(x => x is not null && x.Any(char.IsUpper) && x.Any(char.IsLower) && x.Any(char.IsDigit) && x.Any(c => !char.IsLetterOrDigit(c)))
            .WithMessage("Password needs 12–128 characters, upper/lower case, digit and symbol.");
    }
}
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Request.Password).NotEmpty().MaximumLength(128);
    }
}
public sealed class TokenValidator : AbstractValidator<string>
{
    public TokenValidator() => RuleFor(x => x).NotEmpty().Length(64).Matches("^[A-Za-z0-9+/]{64}$");
}
