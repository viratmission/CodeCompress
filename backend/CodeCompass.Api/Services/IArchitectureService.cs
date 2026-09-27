using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

public interface IArchitectureService
{
    Task<ArchitectureGraphDto?> GetArchitectureGraphAsync(int repositoryId, CancellationToken ct = default);
    Task<ModuleDetailDto?> GetModuleDetailAsync(int repositoryId, int moduleId, CancellationToken ct = default);
    Task<FlowResultDto> GetFlowAsync(int repositoryId, string? from, string? to, CancellationToken ct = default);
}
