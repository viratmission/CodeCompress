using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Services;

namespace CodeCompass.Api.Controllers;

[Authorize]
[ApiController]
public class StarterTasksController : ControllerBase
{
    private readonly IStarterTaskService _starterTasks;
    private readonly ILogger<StarterTasksController> _logger;

    public StarterTasksController(
        IStarterTaskService starterTasks,
        ILogger<StarterTasksController> logger)
    {
        _starterTasks = starterTasks;
        _logger       = logger;
    }

    // ── GET /api/repositories/{repositoryId}/starter-tasks ────────────────
    [HttpGet("api/repositories/{repositoryId:int}/starter-tasks")]
    public async Task<ActionResult<List<StarterTaskSummaryDto>>> GetStarterTasks(
        int repositoryId, CancellationToken ct)
    {
        try
        {
            var tasks = await _starterTasks.GetStarterTasksAsync(repositoryId, ct);
            return Ok(tasks);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get starter tasks for repository {RepoId}", repositoryId);
            return StatusCode(500, new { error = "Failed to retrieve starter tasks." });
        }
    }

    // ── GET /api/starter-tasks/{id} ───────────────────────────────────────
    [HttpGet("api/starter-tasks/{id:int}")]
    public async Task<ActionResult<StarterTaskDetailDto>> GetStarterTaskDetail(
        int id, CancellationToken ct)
    {
        try
        {
            var task = await _starterTasks.GetStarterTaskDetailAsync(id, ct);
            if (task == null)
                return NotFound(new { error = $"Starter task {id} not found." });

            return Ok(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get starter task detail for id {Id}", id);
            return StatusCode(500, new { error = "Failed to retrieve starter task details." });
        }
    }
}
