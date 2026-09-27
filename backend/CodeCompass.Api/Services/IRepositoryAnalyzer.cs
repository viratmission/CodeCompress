using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

public interface IRepositoryAnalyzer
{
    Task<AnalysisSummaryDto> AnalyzeAsync(string gitUrl, CancellationToken ct = default);
}
