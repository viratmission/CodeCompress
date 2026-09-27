using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Services;

namespace CodeCompass.Api.Controllers;

[ApiController]
public class OnboardingController : ControllerBase
{
    private readonly IOnboardingService _onboarding;
    private readonly ILogger<OnboardingController> _logger;

    public OnboardingController(
        IOnboardingService onboarding,
        ILogger<OnboardingController> logger)
    {
        _onboarding = onboarding;
        _logger     = logger;
    }

    // ── GET /api/repositories/{repositoryId}/onboarding/{role} ─────────────
    [HttpGet("api/repositories/{repositoryId:int}/onboarding/{role}")]
    public async Task<ActionResult<OnboardingPathDto>> GetOnboardingPath(
        int repositoryId, string role, CancellationToken ct)
    {
        try
        {
            var path = await _onboarding.GetOrCreateOnboardingPathAsync(repositoryId, role, ct);
            return Ok(path);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get onboarding path for repo {RepoId}, role {Role}", repositoryId, role);
            return StatusCode(500, new { error = "Failed to load onboarding path." });
        }
    }

    // ── GET /api/onboarding/{onboardingId}/steps/{stepId} ──────────────────
    [HttpGet("api/onboarding/{onboardingId:int}/steps/{stepId:int}")]
    public async Task<ActionResult<OnboardingStepDetailDto>> GetStepDetail(
        int onboardingId, int stepId, CancellationToken ct)
    {
        try
        {
            var detail = await _onboarding.GetStepDetailAsync(onboardingId, stepId, ct);
            if (detail == null)
                return NotFound(new { error = $"Step {stepId} not found for onboarding {onboardingId}." });

            return Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get step detail for onboarding {OnboardingId}, step {StepId}", onboardingId, stepId);
            return StatusCode(500, new { error = "Failed to retrieve step detail." });
        }
    }

    // ── POST /api/onboarding/{onboardingId}/steps/{stepId}/complete ────────
    [HttpPost("api/onboarding/{onboardingId:int}/steps/{stepId:int}/complete")]
    public async Task<ActionResult<CompleteStepResponseDto>> CompleteStep(
        int onboardingId, int stepId, CancellationToken ct)
    {
        try
        {
            var result = await _onboarding.CompleteStepAsync(onboardingId, stepId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete step {StepId} for onboarding {OnboardingId}", stepId, onboardingId);
            return StatusCode(500, new { error = "Failed to mark step complete." });
        }
    }
}
