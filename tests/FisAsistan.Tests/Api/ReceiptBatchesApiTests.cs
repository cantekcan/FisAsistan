using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FisAsistan.Application.Auth;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FisAsistan.Tests.Segmentation.SyntheticImageFactory;

namespace FisAsistan.Tests.Api;

/// <summary>
/// Çoklu fiş (bir fotoğrafta birden fazla fiş) yükleme akışının uçtan uca API testleri.
/// Segmentasyon adımında GERÇEK OpenCvReceiptSegmentationService kullanılır (sahte değil) —
/// yalnızca OCR (FakeOcrService, bkz. CustomWebApplicationFactory) sahtedir, böylece testler
/// hem gerçek görüntü işleme mantığını hem de API/veritabanı entegrasyonunu doğrular.
/// </summary>
public class ReceiptBatchesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReceiptBatchesApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"user_{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "Test1234!",
            FullName = "Test User"
        });

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static byte[] BuildMultiReceiptPhoto(int count)
    {
        var rects = new List<RectSpec>();
        for (var i = 0; i < count; i++)
        {
            var col = i % 3;
            var row = i / 3;
            rects.Add(new RectSpec(180 + col * 300, 200 + row * 400, 220, 320, (i % 2 == 0 ? 3 : -3)));
        }

        using var stream = CreateImage(1000, 200 + ((count / 3) + 1) * 400, rects);
        return stream.ToArray();
    }

    [Fact]
    public async Task Upload_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/receipt-batches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_PhotoWithFourReceipts_ProposesFourRegions()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(4));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");

        var response = await client.PostAsync("/api/receipt-batches/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var batch = await response.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        batch.Should().NotBeNull();
        batch!.Status.Should().Be(ReceiptBatchStatus.RegionsProposed);
        batch.PendingRegions.Should().HaveCount(4);
        batch.Receipts.Should().BeEmpty("henüz işleme (process) çağrılmadı");
    }

    [Fact]
    public async Task UpdateRegions_RemoveOne_ReflectsInPendingRegions()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(2));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await client.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        batch!.PendingRegions.Should().HaveCount(2);

        var updateRequest = new UpdateBatchRegionsRequest
        {
            Regions = batch.PendingRegions.Take(1).ToList() // ikinciyi sil
        };

        var updateResponse = await client.PutAsJsonAsync($"/api/receipt-batches/{batch.Id}/regions", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();
        updated!.PendingRegions.Should().HaveCount(1);
    }

    [Fact]
    public async Task Process_ApprovedRegions_CreatesOneReceiptPerRegion()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(3));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await client.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();
        batch!.PendingRegions.Should().HaveCount(3);

        var processResponse = await client.PostAsync($"/api/receipt-batches/{batch.Id}/process", null);
        processResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var processed = await processResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();
        processed!.Status.Should().Be(ReceiptBatchStatus.Completed);
        processed.PendingRegions.Should().BeEmpty();
        processed.Receipts.Should().HaveCount(3);

        // Her Receipt bağımsız olarak mevcut OCR/parser pipeline'ından geçmiş olmalı
        // (FakeOcrService'in sabit örnek metninden bağımsız alan çıkarımı doğrulanır).
        foreach (var receipt in processed.Receipts)
        {
            var detailResponse = await client.GetAsync($"/api/receipts/{receipt.Id}");
            detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = await detailResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();
            detail!.RawOcrText.Should().NotBeNullOrWhiteSpace();
            detail.Status.Should().Be(ReceiptStatus.PendingReview);
        }
    }

    [Fact]
    public async Task Process_ResultingReceipts_AppearInNormalExport()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(2));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await client.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        await client.PostAsync($"/api/receipt-batches/{batch!.Id}/process", null);

        // Batch'ten gelen fişler, EKSTRA bir export formatı gerekmeden normal /api/receipts/export/csv'de görünmeli.
        var exportResponse = await client.GetAsync("/api/receipts/export/csv");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await exportResponse.Content.ReadAsStringAsync();

        var lineCount = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        lineCount.Should().BeGreaterThanOrEqualTo(3, "başlık satırı + en az 2 fiş satırı olmalı");
    }

    [Fact]
    public async Task GetById_OtherUsersBatch_ReturnsNotFound()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var stranger = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(1));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await owner.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        var strangerResponse = await stranger.GetAsync($"/api/receipt-batches/{batch!.Id}");

        strangerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Process_OtherUsersBatch_ReturnsNotFound()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var stranger = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(1));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await owner.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        var strangerResponse = await stranger.PostAsync($"/api/receipt-batches/{batch!.Id}/process", null);

        strangerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_NoReceiptsDetected_ReturnsSegmentationFailedStatus()
    {
        var client = await CreateAuthenticatedClientAsync();

        // Tek renkli, kontrastsız bir görsel — hiçbir fiş tespit edilememeli.
        using var uniformStream = CreateImage(400, 300, Array.Empty<RectSpec>());

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(uniformStream.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "blank.png");

        var response = await client.PostAsync("/api/receipt-batches/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var batch = await response.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();
        batch!.Status.Should().Be(ReceiptBatchStatus.SegmentationFailed);
        batch.PendingRegions.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_RemovesBatch()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildMultiReceiptPhoto(1));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "batch.png");
        var uploadResponse = await client.PostAsync("/api/receipt-batches/upload", content);
        var batch = await uploadResponse.Content.ReadFromJsonAsync<ReceiptBatchDetailDto>();

        var deleteResponse = await client.DeleteAsync($"/api/receipt-batches/{batch!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/receipt-batches/{batch.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
