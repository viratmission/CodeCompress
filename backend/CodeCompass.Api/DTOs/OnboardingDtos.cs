namespace CodeCompass.Api.DTOs;

public class OnboardingPathDto
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int UserOnboardingId { get; set; }
    public double ProgressPercentage { get; set; }
    public List<OnboardingStepSummaryDto> Steps { get; set; } = new();
    public List<RepositoryModuleDto> RelatedModules { get; set; } = new();
    public List<RepositoryFileDto> RelevantFiles { get; set; } = new();
}

public class OnboardingStepSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StepType { get; set; } = "module";
    public int? ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string ModulePath { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime? CompletedAt { get; set; }
}

public class OnboardingStepDetailDto
{
    public int StepId { get; set; }
    public int UserOnboardingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WhatYouWillLearn { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string StepType { get; set; } = "module";
    public RepositoryModuleDto? RelatedModule { get; set; }
    public List<RepositoryFileDto> RelevantFiles { get; set; } = new();
    public List<RepositoryDependencyDto> Dependencies { get; set; } = new();
    public string AiExplanation { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTime? CompletedAt { get; set; }
}

public class CompleteStepResponseDto
{
    public int UserOnboardingId { get; set; }
    public int StepId { get; set; }
    public string Status { get; set; } = "completed";
    public DateTime CompletedAt { get; set; }
    public double ProgressPercentage { get; set; }
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; }
    public bool IsFinished { get; set; }
}

public class StarterTaskSummaryDto
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Easy";
    public string Reason { get; set; } = string.Empty;
    public int? RelatedModuleId { get; set; }
    public string RelatedModuleName { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

public class StarterTaskDetailDto
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Easy";
    public string ReasonForRecommendation { get; set; } = string.Empty;
    public RepositoryModuleDto? RelatedModule { get; set; }
    public List<RepositoryFileDto> RelevantFiles { get; set; } = new();
    public List<RepositoryDependencyDto> KnownDependencies { get; set; } = new();
    public string SuggestedFirstStep { get; set; } = string.Empty;
    public string ExistingPattern { get; set; } = string.Empty;
    public List<ChangeImpactItemDto> ChangeImpact { get; set; } = new();
}
