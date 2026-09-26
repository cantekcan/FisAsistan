namespace FisAsistan.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "FisAsistan";
    public string Audience { get; set; } = "FisAsistanClient";
    public int ExpiryMinutes { get; set; } = 480;
}
