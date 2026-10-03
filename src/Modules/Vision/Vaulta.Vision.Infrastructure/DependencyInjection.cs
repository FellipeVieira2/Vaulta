using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Vision.Application;
using Vaulta.Vision.Encoding;
namespace Vaulta.Vision.Infrastructure;
public sealed class VisionOptions
{
    public string? ModelManifestPath { get; set; }
    public int EncoderConcurrency { get; set; }=2;
    public int MaxIndexReferences { get; set; }=100000;
}
public static class DependencyInjection
{
    public static IServiceCollection AddVisionModule(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddDbContext<VisionDbContext>(options=>options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        var options=configuration.GetSection("Vision").Get<VisionOptions>() ?? new();
        if(options.EncoderConcurrency is <1 or >4 || options.MaxIndexReferences is <1 or >100000) throw new InvalidOperationException("Invalid Vision resource limits.");
        services.AddSingleton(options);
        var history=configuration.GetSection("Vision:History").Get<VisionHistoryOptions>() ?? new();history.Validate();services.AddSingleton(history);services.AddScoped<VisionHistoryService>();
        services.AddSingleton<IImageEncoder>(_=>new OnnxImageEncoder(options.ModelManifestPath ?? throw new InvalidOperationException("Vision encoder is not configured."),options.EncoderConcurrency));
        services.AddSingleton<CosineReferenceIndex>(p=>new(p.GetRequiredService<IImageEncoder>().Identity,options.MaxIndexReferences));
        services.AddSingleton<IVisualReferenceIndex>(p=>p.GetRequiredService<CosineReferenceIndex>());
        services.AddScoped<IVisualReferenceBuilder,VisualReferenceBuilder>();
        services.AddScoped<CatalogVisionPreparation>();
        services.AddScoped<IVisionCatalog,VisionCatalog>();
        services.AddScoped<IVisionEvidenceReader,VisionEvidenceReader>();
        services.AddSingleton<VisionPrintingResolver>();
        services.AddSingleton<VisionScanCapacity>();
        services.AddScoped<VisionScannerService>();
        services.AddHostedService<VisualIndexRefreshWorker>(); services.AddHostedService<VisionRetentionWorker>(); return services;
    }
}
