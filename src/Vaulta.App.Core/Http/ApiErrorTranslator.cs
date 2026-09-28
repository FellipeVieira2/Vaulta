namespace Vaulta.App.Core.Http;

/// <summary>
/// Central translation from transport/HTTP failures to short, non-technical messages a
/// ViewModel can show a user. The backend's ProblemDetails "title" is preserved as internal
/// diagnostic detail only; the message shown to the user never includes server-provided text.
/// </summary>
public static class ApiErrorTranslator
{
    public static ApiException FromStatus(int statusCode, string? detail) => new(statusCode, MessageFor(statusCode), detail);

    public static ApiException FromException(Exception exception) => exception switch
    {
        ApiException api => api,
        OperationCanceledException => new ApiException(null, "A operação foi cancelada.", exception.Message),
        HttpRequestException http => new ApiException((int?)http.StatusCode, MessageForTransport(http), http.Message),
        _ => new ApiException(null, "Não foi possível concluir agora. Tente novamente em instantes.", exception.Message)
    };

    private static string MessageForTransport(HttpRequestException exception) =>
        exception.StatusCode is { } status ? MessageFor((int)status) : "Sem conexão com a internet. Verifique sua rede e tente novamente.";

    private static string MessageFor(int statusCode) => statusCode switch
    {
        400 => "Confira os dados informados e tente novamente.",
        401 => "Sua sessão expirou. Entre novamente para continuar.",
        403 => "Você não tem permissão para realizar esta ação.",
        404 => "Não encontramos o que você está procurando.",
        409 => "Este item foi alterado em outro lugar. Atualize e tente novamente.",
        429 => "Muitas tentativas. Aguarde um instante antes de tentar novamente.",
        >= 500 => "O servidor está indisponível agora. Tente novamente em instantes.",
        _ => "Não foi possível concluir agora. Tente novamente em instantes."
    };
}
