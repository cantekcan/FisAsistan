using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FisAsistan.Application.Auth;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Api;

public class ReceiptsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReceiptsApiTests(CustomWebApplicationFactory factory)
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

    [Fact]
    public async Task List_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/receipts");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_ValidImage_ReturnsExtractedFields()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImage.OnePixelPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "receipt.png");

        var response = await client.PostAsync("/api/receipts/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ReceiptDetailDto>();

        dto.Should().NotBeNull();
        dto!.Status.Should().Be(ReceiptStatus.PendingReview);
        dto.Fields.Should().Contain(f => f.FieldName == ReceiptFieldName.TotalAmount && f.CurrentValue == "62.50");
        dto.VatLines.Should().ContainSingle(v => v.RatePercent == 10m);
        dto.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_UnsupportedExtension_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF" başlığı
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "receipt.pdf");

        var response = await client.PostAsync("/api/receipts/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Array.Empty<byte>());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "empty.png");

        var response = await client.PostAsync("/api/receipts/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FullFlow_UploadEditApprove_ReflectsUserCorrection()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImage.OnePixelPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "receipt.png");

        var uploadResponse = await client.PostAsync("/api/receipts/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();

        // Currency alanı bulunamadığında parser zaten varsayılan "TRY" atadığından, kullanıcı
        // düzeltmesini gerçek bir değişiklikle (kaynak Parser -> User) test etmek için
        // MerchantName alanı üzerinden doğrulanır.
        var updateRequest = new UpdateReceiptFieldsRequest
        {
            Fields = new List<UpdateReceiptFieldItem>
            {
                new() { FieldName = ReceiptFieldName.MerchantName, Value = "Düzeltilmiş Satıcı Adı", IsConfirmed = true }
            }
        };

        var updateResponse = await client.PutAsJsonAsync($"/api/receipts/{uploaded!.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();

        var merchantField = updated!.Fields.First(f => f.FieldName == ReceiptFieldName.MerchantName);
        merchantField.CurrentValue.Should().Be("Düzeltilmiş Satıcı Adı");
        merchantField.Source.Should().Be(ValueSource.User);

        var approveResponse = await client.PostAsync($"/api/receipts/{uploaded.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();
        approved!.Status.Should().Be(ReceiptStatus.Approved);
    }

    [Fact]
    public async Task Reject_SetsStatusAndReason()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImage.OnePixelPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "receipt.png");
        var uploadResponse = await client.PostAsync("/api/receipts/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();

        var rejectResponse = await client.PostAsJsonAsync($"/api/receipts/{uploaded!.Id}/reject",
            new RejectReceiptRequest { Reason = "Görsel okunaksız" });

        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();
        rejected!.Status.Should().Be(ReceiptStatus.Rejected);
        rejected.RejectionReason.Should().Be("Görsel okunaksız");
    }

    [Fact]
    public async Task ExportCsv_ReturnsCsvFile()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImage.OnePixelPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "receipt.png");
        await client.PostAsync("/api/receipts/upload", content);

        var response = await client.GetAsync("/api/receipts/export/csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("MIGROS");
    }

    [Fact]
    public async Task Delete_RemovesReceipt()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImage.OnePixelPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "receipt.png");
        var uploadResponse = await client.PostAsync("/api/receipts/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ReceiptDetailDto>();

        var deleteResponse = await client.DeleteAsync($"/api/receipts/{uploaded!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/receipts/{uploaded.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
