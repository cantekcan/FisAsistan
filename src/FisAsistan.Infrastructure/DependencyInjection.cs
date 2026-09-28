using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Infrastructure.Auth;
using FisAsistan.Infrastructure.Export;
using FisAsistan.Infrastructure.Ocr;
using FisAsistan.Infrastructure.Persistence;
using FisAsistan.Infrastructure.Receipts;
using FisAsistan.Infrastructure.ReceiptBatches;
using FisAsistan.Infrastructure.Segmentation;
using FisAsistan.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FisAsistan.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Veritabanı sağlayıcısı hariç tüm altyapı servislerini kaydeder. Testlerde gerçek
    /// PostgreSQL yerine InMemory sağlayıcı kullanılabilmesi için DbContext kaydı ayrı
    /// tutulur (bkz. <see cref="AddPostgresDatabase"/>) — aksi halde aynı IServiceCollection
    /// içinde iki farklı EF Core sağlayıcısı (Npgsql + InMemory) çakışır.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.Configure<LocalFileStorageOptions>(configuration.GetSection(LocalFileStorageOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddScoped<IOcrService, TesseractOcrService>();
        services.AddScoped<IImagePreprocessor, ImageSharpPreprocessor>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IReceiptExportService, ReceiptExportService>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        services.AddSingleton<ReceiptParserService>();
        services.AddScoped<IReceiptProcessingService, ReceiptProcessingService>();

        services.AddScoped<IReceiptSegmentationService, OpenCvReceiptSegmentationService>();
        services.AddScoped<IReceiptBatchProcessingService, ReceiptBatchProcessingService>();

        return services;
    }

    /// <summary>Üretim/geliştirme ortamı için gerçek PostgreSQL bağlantısını kaydeder.</summary>
    public static IServiceCollection AddPostgresDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FisAsistanDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        return services;
    }
}
