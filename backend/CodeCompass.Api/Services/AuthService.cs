using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;

namespace CodeCompass.Api.Services;

public class AuthService : IAuthService
{
    private readonly CodeCompassDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;
    private readonly PasswordHasher<User> _hasher;

    public AuthService(
        CodeCompassDbContext db,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
        _hasher = new PasswordHasher<User>();
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        // Validate email format
        if (!IsValidEmail(normalizedEmail))
            throw new ArgumentException("Invalid email format.");

        // Validate password strength
        ValidatePasswordStrength(request.Password);

        // Validate display name
        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            throw new ArgumentException("Display name must be at least 2 characters.");

        // Check unique email
        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (exists)
            throw new InvalidOperationException("An account with this email address already exists.");

        var user = new User
        {
            Email       = normalizedEmail,
            DisplayName = displayName,
            Role        = "Developer",
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        // Hash password securely using ASP.NET Core PasswordHasher
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("New user registered successfully: UserId={UserId}", user.Id);

        var token = GenerateJwtToken(user);
        return new AuthResponseDto
        {
            Token = token,
            User  = MapToDto(user)
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user == null)
        {
            _logger.LogWarning("Failed login attempt for non-existent email.");
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed login attempt: Invalid password for UserId={UserId}", user.Id);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, request.Password);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("User logged in successfully: UserId={UserId}", user.Id);

        var token = GenerateJwtToken(user);
        return new AuthResponseDto
        {
            Token = token,
            User  = MapToDto(user)
        };
    }

    public async Task<UserDto?> GetCurrentUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        return user == null ? null : MapToDto(user);
    }

    public string GenerateJwtToken(User user)
    {
        var key = JwtKeyHelper.GetSecurityKey(_config);
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var issuer = JwtKeyHelper.GetIssuer(_config);
        var audience = JwtKeyHelper.GetAudience(_config);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(user.Role) ? "Developer" : user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeEmail(string email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    }

    private static void ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters long.");

        bool hasDigit = password.Any(char.IsDigit);
        bool hasLetter = password.Any(char.IsLetter);

        if (!hasDigit || !hasLetter)
            throw new ArgumentException("Password must contain at least one letter and one number.");
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id          = user.Id,
        Email       = user.Email,
        DisplayName = user.DisplayName,
        Role        = user.Role,
        CreatedAt   = user.CreatedAt
    };
}
