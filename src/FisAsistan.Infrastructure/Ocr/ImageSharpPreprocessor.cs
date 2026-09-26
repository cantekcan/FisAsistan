using FisAsistan.Application.Common.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FisAsistan.Infrastructure.Ocr;

/// <summary>
/// OCR doğruluğunu artırmak için EXIF'e göre otomatik döndürme, boyutlandırma
/// (Tesseract için ~300dpi'ye denk gelecek genişlik), gri tonlama, kontrast ve
/// gürültü azaltma (hafif bulanıklaştırma + keskinleştirme) uygular. Tamamen yerel,
/// ücretsiz ve açık kaynak (Apache-2.0) SixLabors.ImageSharp kütüphanesiyle çalışır.
/// </summary>
public class ImageSharpPreprocessor : IImagePreprocessor
{
    private const int TargetMaxDimension = 2200;
    private const int TargetMinDimension = 1000;

    public async Task<MemoryStream> PreprocessAsync(Stream imageStream, CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(imageStream, ct);

        image.Mutate(ctx =>
        {
            ctx.AutoOrient();

            var longestSide = Math.Max(image.Width, image.Height);
            if (longestSide > TargetMaxDimension)
            {
                var scale = (double)TargetMaxDimension / longestSide;
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size((int)(image.Width * scale), (int)(image.Height * scale)),
                    Mode = ResizeMode.Max
                });
            }
            else if (longestSide < TargetMinDimension)
            {
                var scale = (double)TargetMinDimension / longestSide;
                ctx.Resize(
                    (int)(image.Width * scale),
                    (int)(image.Height * scale),
                    KnownResamplers.Lanczos3);
            }

            ctx.Grayscale();
            ctx.GaussianBlur(0.5f);   // hafif gürültü azaltma
            ctx.Contrast(1.25f);
            ctx.GaussianSharpen(0.6f);
        });

        var output = new MemoryStream();
        await image.SaveAsync(output, new PngEncoder(), ct);
        output.Position = 0;
        return output;
    }
}
