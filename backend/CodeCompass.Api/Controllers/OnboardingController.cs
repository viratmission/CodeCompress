using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Services;

namespace CodeCompass.Api.Controllers;

[Authorize]
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

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }

    // ── GET /api/repositories/{repositoryId}/onboarding/{role} ─────────────
    [HttpGet("api/repositories/{repositoryId:int}/onboarding/{role}")]
    public async Task<ActionResult<OnboardingPathDto>> GetOnboardingPath(
        int repositoryId, string role, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var path = await _onboarding.GetOrCreateOnboardingPathAsync(repositoryId, role, ct, userId);
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
            var userId = GetCurrentUserId();
            var detail = await _onboarding.GetStepDetailAsync(onboardingId, stepId, ct, userId);
            if (detail == null)
                return NotFound(new { error = $"Step {stepId} not found for onboarding {onboardingId}." });

            return Ok(detail);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
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
            var userId = GetCurrentUserId();
            var result = await _onboarding.CompleteStepAsync(onboardingId, stepId, ct, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
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
