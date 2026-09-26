using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Vaulta.Assets.Infrastructure;
using Vaulta.Identity.Infrastructure;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Collection.Infrastructure;
using Vaulta.Web.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddAssetsModule(builder.Configuration);
builder.Services.AddCollectionModule(builder.Configuration);
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["correlationId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Vaulta Identity API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Access token returned by /api/v1/auth/login."
    });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag", "X-Correlation-ID");
}));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (context, _) => await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
        .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = new() { Status = 429, Title = "Too many requests." } });
});
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("Vaulta.Web.Api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter("Microsoft.AspNetCore.Hosting"));

var app = builder.Build();
if (args.Contains("--migrate") || builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AssetsDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CollectionDbContext>().Database.MigrateAsync();
    if (args.Contains("--migrate")) return;
}
app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers["X-Correlation-ID"].ToString();
    context.TraceIdentifier = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? incoming : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = context.TraceIdentifier });
    await next(context);
});
app.UseExceptionHandler();
app.UseStatusCodePages(async context => await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
    .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = new() { Status = context.HttpContext.Response.StatusCode } }));
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) { app.UseHsts(); app.UseHttpsRedirection(); }
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityEndpoints();
app.MapCatalogEndpoints();
app.MapAssetEndpoints();
app.MapCollectionEndpoints();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.Run();

public partial class Program;

internal sealed class PostgresHealthCheck(IdentityDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("PostgreSQL unavailable.");
}
