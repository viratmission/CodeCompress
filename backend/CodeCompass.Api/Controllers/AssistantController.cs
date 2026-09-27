using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CodeCompass.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/repositories/{repositoryId:int}/assistant")]
public class AssistantController : ControllerBase
{
    private readonly IAssistantService _assistant;
    private readonly CodeCompassDbContext _db;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(
        IAssistantService assistant,
        CodeCompassDbContext db,
        ILogger<AssistantController> logger)
    {
        _assistant = assistant;
        _db        = db;
        _logger    = logger;
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }

    // ── POST /api/repositories/{id}/assistant/ask ─────────────────────────
    [HttpPost("ask")]
    public async Task<ActionResult<AssistantAnswerDto>> Ask(
        int repositoryId,
        [FromBody] AskRequestDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "question is required." });

        try
        {
            var userId = GetCurrentUserId();
            var answer = await _assistant.AskAsync(repositoryId, request, ct, userId);
            return Ok(answer);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("analyzed"))
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request was cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assistant ask failed for repo {Id}", repositoryId);
            return StatusCode(500, new { error = "An unexpected error occurred." });
        }
    }

    // ── GET /api/repositories/{id}/assistant/suggestions ─────────────────
    [HttpGet("suggestions")]
    public async Task<ActionResult<SuggestedQuestionsDto>> GetSuggestions(
        int repositoryId, CancellationToken ct)
    {
        try
        {
            var suggestions = await _assistant.GetSuggestedQuestionsAsync(repositoryId, ct);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get suggestions for repo {Id}", repositoryId);
            return StatusCode(500, new { error = "Failed to retrieve suggestions." });
        }
    }

    // ── GET /api/repositories/{id}/assistant/history ──────────────────────
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<ConversationDto>>> GetHistory(
        int repositoryId,
        [FromQuery] string? sessionId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var query = _db.Conversations
            .Where(c => c.RepositoryId == repositoryId)
            .AsQueryable();

        if (userId.HasValue)
        {
            // Scope by authenticated user; also allow unassigned legacy conversations matching sessionId
            query = query.Where(c => c.UserId == userId.Value || (c.UserId == null && c.SessionId == sessionId));
        }
        else if (!string.IsNullOrWhiteSpace(sessionId))
        {
            query = query.Where(c => c.SessionId == sessionId);
        }

        var history = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new ConversationDto
            {
                Id           = c.Id,
                RepositoryId = c.RepositoryId,
                SessionId    = c.SessionId,
                Question     = c.Question,
                Answer       = c.Answer,
                QuestionType = c.QuestionType,
                CreatedAt    = c.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(history);
    }
}
