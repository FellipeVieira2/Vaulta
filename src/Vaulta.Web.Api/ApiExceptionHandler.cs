using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vaulta.SharedKernel;

namespace Vaulta.Web.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ValidationException or DomainException or BadHttpRequestException => 400,
            UnauthorizedException => 401, ForbiddenException => 403,
            NotFoundException => 404, ConflictException => 409, _ => 500
        };
        if (status == 500) logger.LogError("Request {TraceId} failed with {ErrorType}", context.TraceIdentifier, exception.GetType().Name);
        context.Response.StatusCode = status;
        var detail = new ProblemDetails
        {
            Status = status, Title = status == 500 ? "Unexpected server error." : exception is ValidationException ? "Validation failed." : exception.Message,
            Instance = context.Request.Path
        };
        if (exception is ValidationException validation)
            detail.Extensions["errors"] = validation.Errors.GroupBy(x => x.PropertyName).ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray());
        detail.Extensions["correlationId"] = context.TraceIdentifier;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
        return true;
    }
}
