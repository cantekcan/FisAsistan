using FisAsistan.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using FisAsistan.Infrastructure.Persistence;
using FisAsistan.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FisAsistan.Tests.Api;

/// <summary>
/// Testler için gerçek PostgreSQL ve gerçek Tesseract yerine InMemory DB ve sahte
/// (fake) OCR/görüntü ön işleme servisleri kullanan test fabrikası. Böylece API
/// entegrasyon testleri harici bağımlılık gerektirmeden, hızlı ve deterministik çalışır.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = Guid.NewGuid().ToString();
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "fisasistan-tests", Guid.NewGuid().ToString());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<FisAsistanDbContext>(options => options.UseInMemoryDatabase(DbName));

            services.RemoveAll<IOcrService>();
            services.AddScoped<IOcrService, FakeOcrService>();

            services.RemoveAll<IImagePreprocessor>();
            services.AddScoped<IImagePreprocessor, FakeImagePreprocessor>();

            services.Configure<LocalFileStorageOptions>(o => o.RootPath = StorageRoot);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch
        {
            // Testlerde en iyi çaba temizliği — başarısız olursa görmezden gel.
        }
    }
}
