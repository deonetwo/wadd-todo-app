using System.Collections.Generic;
using System.Threading.Tasks;
using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

public interface IAiGoalService
{
    string Provider { get; set; }
    string ApiKey { get; set; }
    string? CustomBaseUrl { get; set; }
    string? CustomModel { get; set; }
    string GeminiApiKey { get; set; }

    Task<GoalGenerationResult> GenerateGoalDetailsAsync(
        string? goalTitleOrPrompt = null,
        string? category = null,
        System.DateTime? targetDate = null,
        string? description = null,
        IReadOnlyList<string>? existingMilestones = null,
        string? newMilestoneDraft = null,
        IReadOnlyList<string>? availableTags = null);

    Task<IReadOnlyList<string>> GenerateMilestonesAsync(
        string goalTitle,
        string? category = null,
        string? description = null,
        IReadOnlyList<string>? existingMilestones = null,
        System.DateTime? targetDate = null,
        string? newMilestoneDraft = null,
        IReadOnlyList<string>? availableTags = null);

    Task<JournalDraftResult> GenerateJournalPromptAsync(
        string goalTitle,
        int completedSteps,
        int totalSteps,
        string? recentMilestone = null,
        string? category = null,
        string? description = null,
        IReadOnlyList<string>? allMilestones = null,
        string? currentJournalDraft = null,
        IReadOnlyList<string>? availableTags = null);

    Task<IReadOnlyList<AiModelOption>> GetAvailableModelsAsync(string? provider = null, string? apiKey = null, string? customBaseUrl = null);

    Task<(bool Success, string Message, string? ModelName)> TestConnectionAsync(string? provider = null, string? apiKey = null, string? model = null, string? customBaseUrl = null);
}

