using FisAsistan.Application.Auth;
using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Domain.Entities;
using FisAsistan.Domain.Enums;
using FisAsistan.Infrastructure.Auth;
using FisAsistan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FisAsistan.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly FisAsistanDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        FisAsistanDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == emailNormalized, ct))
        {
            return Conflict(new { message = "Bu e-posta adresiyle zaten bir kullanıcı kayıtlı." });
        }

        var user = new User
        {
            Email = emailNormalized,
            FullName = request.FullName,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.Accountant
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(BuildAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized, ct);

        if (user is null || !user.IsActive || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "E-posta veya şifre hatalı." });
        }

        return Ok(BuildAuthResponse(user));
    }

    private AuthResponse BuildAuthResponse(User user) => new()
    {
        Token = _jwtTokenService.GenerateToken(user),
        Email = user.Email,
        FullName = user.FullName,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes)
    };
}
