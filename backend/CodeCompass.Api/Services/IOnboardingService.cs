using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

public interface IOnboardingService
{
    Task<OnboardingPathDto> GetOrCreateOnboardingPathAsync(
        int repositoryId, string role, CancellationToken ct = default, int? userId = null);

    Task<OnboardingStepDetailDto?> GetStepDetailAsync(
        int userOnboardingId, int stepId, CancellationToken ct = default, int? userId = null);

    Task<CompleteStepResponseDto> CompleteStepAsync(
        int userOnboardingId, int stepId, CancellationToken ct = default, int? userId = null);
}
