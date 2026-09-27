using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;

namespace CodeCompass.Api.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(int userId, CancellationToken ct = default);
    string GenerateJwtToken(User user);
}
