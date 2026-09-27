using System;
using System.Collections.Generic;

namespace Wadd.Core.Models;

public class GoalGenerationResult
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "Personal";
    public DateTime? TargetDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> SuggestedMilestones { get; set; } = new();
    public bool IsLiveAi { get; set; }
    public string SourceLabel { get; set; } = "Smart Offline Engine";
}

public class JournalDraftResult
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsLiveAi { get; set; }
    public string SourceLabel { get; set; } = "Smart Offline Engine";
}

public class AiModelOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}
