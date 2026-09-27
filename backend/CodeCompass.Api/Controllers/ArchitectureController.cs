using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CodeCompass.Api.Services;
using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/repositories/{repositoryId:int}")]
public class ArchitectureController : ControllerBase
{
    private readonly IArchitectureService _arch;
    private readonly ILogger<ArchitectureController> _logger;

    public ArchitectureController(IArchitectureService arch, ILogger<ArchitectureController> logger)
    {
        _arch   = arch;
        _logger = logger;
    }

    // ── GET /api/repositories/{id}/architecture ───────────────────────────
    [HttpGet("architecture")]
    public async Task<ActionResult<ArchitectureGraphDto>> GetArchitecture(
        int repositoryId, CancellationToken ct)
    {
        try
        {
            var graph = await _arch.GetArchitectureGraphAsync(repositoryId, ct);
            if (graph == null) return NotFound(new { error = $"Repository {repositoryId} not found." });
            return Ok(graph);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build architecture graph for repository {Id}", repositoryId);
            return StatusCode(500, new { error = "Failed to build architecture graph." });
        }
    }

    // ── GET /api/repositories/{id}/modules/{moduleId} ─────────────────────
    [HttpGet("modules/{moduleId}")]
    public async Task<ActionResult<ModuleDetailDto>> GetModuleDetail(
        int repositoryId, string moduleId, CancellationToken ct)
    {
        var cleanId = moduleId.StartsWith("module-", StringComparison.OrdinalIgnoreCase)
            ? moduleId.Substring(7)
            : moduleId;

        if (!int.TryParse(cleanId, out var id))
            return BadRequest(new { error = $"Invalid module identifier '{moduleId}'." });

        try
        {
            var detail = await _arch.GetModuleDetailAsync(repositoryId, id, ct);
            if (detail == null)
                return NotFound(new { error = $"Module {id} not found in repository {repositoryId}." });
            return Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get module detail {ModuleId}", moduleId);
            return StatusCode(500, new { error = "Failed to retrieve module detail." });
        }
    }

    // ── GET /api/repositories/{id}/flow?from=X&to=Y ───────────────────────
    [HttpGet("flow")]
    public async Task<ActionResult<FlowResultDto>> GetFlow(
        int repositoryId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        try
        {
            var result = await _arch.GetFlowAsync(repositoryId, from, to, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute flow for repository {Id}", repositoryId);
            return StatusCode(500, new { error = "Failed to compute dependency flow." });
        }
    }
}
