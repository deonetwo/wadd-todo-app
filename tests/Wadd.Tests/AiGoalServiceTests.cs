using System;
using System.Net.Http;
using System.Threading.Tasks;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

[Collection("AppSettingsTests")]
public class AiGoalServiceTests
{
    private readonly AiGoalService _aiGoalService;

    public AiGoalServiceTests()
    {
        _aiGoalService = new AiGoalService(new HttpClient()) { ApiKey = string.Empty };
    }

    [Fact]
    public async Task GenerateGoalDetailsAsync_EmptyPrompt_ReturnsSensibleDefaults()
    {
        var result = await _aiGoalService.GenerateGoalDetailsAsync("");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.Category));
        Assert.NotNull(result.TargetDate);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
        Assert.NotEmpty(result.SuggestedMilestones);
    }

    [Theory]
    [InlineData("Run a full marathon this year", "Health & Fitness")]
    [InlineData("Launch my SaaS product startup", "Career & Business")]
    [InlineData("Save $10,000 emergency fund", "Finance & Wealth")]
    [InlineData("Learn C# and master Avalonia UI", "Learning & Skills")]
    [InlineData("Travel to Japan for vacation", "Travel & Lifestyle")]
    [InlineData("Build positive daily mindfulness habits", "Personal Growth")]
    public async Task GenerateGoalDetailsAsync_CategorizesCorrectly(string prompt, string expectedCategory)
    {
        var result = await _aiGoalService.GenerateGoalDetailsAsync(prompt);

        Assert.NotNull(result);
        Assert.Equal(expectedCategory, result.Category);
        Assert.NotEmpty(result.Description);
        Assert.NotEmpty(result.SuggestedMilestones);
        Assert.True(result.TargetDate.HasValue);
    }

    [Fact]
    public async Task GenerateMilestonesAsync_ReturnsActionableSteps()
    {
        var milestones = await _aiGoalService.GenerateMilestonesAsync("Run a 10K", "Health & Fitness", "Training plan");

        Assert.NotNull(milestones);
        Assert.NotEmpty(milestones);
        Assert.InRange(milestones.Count, 3, 6);
        foreach (var m in milestones)
        {
            Assert.False(string.IsNullOrWhiteSpace(m));
        }
    }

    [Fact]
    public async Task GenerateJournalPromptAsync_ZeroProgress_ReturnsBeginningJournal()
    {
        var draft = await _aiGoalService.GenerateJournalPromptAsync("Master Guitar", 0, 4);

        Assert.NotNull(draft);
        Assert.False(string.IsNullOrWhiteSpace(draft.Title));
        Assert.Contains("Setting Intentions", draft.Title);
        Assert.Contains("Master Guitar", draft.Title);
        Assert.False(string.IsNullOrWhiteSpace(draft.Content));
    }

    [Fact]
    public async Task GenerateJournalPromptAsync_HalfwayProgress_ReturnsMomentumJournal()
    {
        var draft = await _aiGoalService.GenerateJournalPromptAsync("Master Guitar", 2, 4);

        Assert.NotNull(draft);
        Assert.False(string.IsNullOrWhiteSpace(draft.Title));
        Assert.Contains("Momentum", draft.Title);
        Assert.Contains("50%", draft.Content);
    }

    [Fact]
    public async Task GenerateJournalPromptAsync_CompletedProgress_ReturnsCelebrationJournal()
    {
        var draft = await _aiGoalService.GenerateJournalPromptAsync("Master Guitar", 4, 4);

        Assert.NotNull(draft);
        Assert.False(string.IsNullOrWhiteSpace(draft.Title));
        Assert.Contains("Achieved", draft.Title);
    }

    [Fact]
    public void GeminiApiKey_GetAndSet_UpdatesPropertyCorrectly()
    {
        _aiGoalService.GeminiApiKey = " AIzaSyTestKey123 ";

        Assert.Equal("AIzaSyTestKey123", _aiGoalService.GeminiApiKey);
        Assert.Equal("AIzaSyTestKey123", _aiGoalService.ApiKey);
    }

    [Theory]
    [InlineData("Gemini", "AIzaSy...")]
    [InlineData("OpenRouter", "sk-or-v1-...")]
    [InlineData("OpenAI", "sk-proj-...")]
    [InlineData("Groq", "gsk_...")]
    [InlineData("DeepSeek", "sk-...")]
    [InlineData("Custom", "custom-key")]
    public void MultiProviderSettings_SetsCorrectly(string provider, string apiKey)
    {
        _aiGoalService.Provider = provider;
        _aiGoalService.ApiKey = apiKey;
        _aiGoalService.CustomBaseUrl = "http://localhost:11434/v1";
        _aiGoalService.CustomModel = "llama3.2";

        Assert.Equal(provider, _aiGoalService.Provider);
        Assert.Equal(apiKey, _aiGoalService.ApiKey);
        Assert.Equal("http://localhost:11434/v1", _aiGoalService.CustomBaseUrl);
        Assert.Equal("llama3.2", _aiGoalService.CustomModel);
    }

    [Theory]
    [InlineData("Gemini", "gemini-2.0-flash")]
    [InlineData("OpenRouter", "openai/gpt-4o-mini")]
    [InlineData("OpenAI", "gpt-4o-mini")]
    [InlineData("Groq", "llama-3.3-70b-versatile")]
    [InlineData("DeepSeek", "deepseek-chat")]
    [InlineData("Custom", "llama3.2")]
    public void GetDefaultModelForProvider_ReturnsExpectedModel(string provider, string expectedModel)
    {
        var model = Wadd.UI.ViewModels.MainViewModel.GetDefaultModelForProvider(provider);
        Assert.Equal(expectedModel, model);
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenRouter")]
    [InlineData("OpenAI")]
    [InlineData("Groq")]
    [InlineData("DeepSeek")]
    [InlineData("Custom")]
    public async Task GetAvailableModelsAsync_ReturnsCuratedModelsWhenOffline(string provider)
    {
        var models = await _aiGoalService.GetAvailableModelsAsync(provider, apiKey: "");
        Assert.NotNull(models);
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.IsRecommended);
        foreach (var m in models)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
        }
    }

    [Fact]
    public void BuildGoalPromptContext_IncludesAllNonBlankFields()
    {
        var targetDate = new DateTime(2026, 12, 31);
        var existingMilestones = new List<string> { "Step A", "Step B" };

        var context = AiGoalService.BuildGoalPromptContext(
            goalTitleOrPrompt: "Run a Marathon",
            category: "Health & Fitness",
            targetDate: targetDate,
            description: "Finish sub-4 hours with disciplined training.",
            existingMilestones: existingMilestones,
            newMilestoneDraft: "Step C");

        Assert.Contains("Run a Marathon", context);
        Assert.Contains("Health & Fitness", context);
        Assert.Contains("2026-12-31", context);
        Assert.Contains("Finish sub-4 hours", context);
        Assert.Contains("1. Step A", context);
        Assert.Contains("2. Step B", context);
        Assert.Contains("3. Step C", context);
    }

    [Fact]
    public void BuildMilestonesPromptContext_IncludesAllNonBlankFields()
    {
        var targetDate = new DateTime(2026, 11, 15);
        var existing = new List<string> { "Initial Research" };

        var context = AiGoalService.BuildMilestonesPromptContext(
            goalTitle: "Launch SaaS",
            category: "Career & Business",
            description: "B2B productivity tool",
            existingMilestones: existing,
            targetDate: targetDate,
            newMilestoneDraft: "Create landing page");

        Assert.Contains("Launch SaaS", context);
        Assert.Contains("Career & Business", context);
        Assert.Contains("B2B productivity tool", context);
        Assert.Contains("2026-11-15", context);
        Assert.Contains("1. Initial Research", context);
        Assert.Contains("Create landing page", context);
    }

    [Fact]
    public void BuildJournalPromptContext_IncludesAllNonBlankFields()
    {
        var allMilestones = new List<string> { "[Completed] Step 1", "[Pending] Step 2" };

        var context = AiGoalService.BuildJournalPromptContext(
            goalTitle: "Learn Spanish",
            completedSteps: 1,
            totalSteps: 2,
            recentMilestone: "Finish A1 Course",
            category: "Learning & Skills",
            description: "Conversational fluency in 6 months",
            allMilestones: allMilestones,
            currentJournalDraft: "Feeling confident about vocabulary today.");

        Assert.Contains("Learn Spanish", context);
        Assert.Contains("1/2 milestone steps completed", context);
        Assert.Contains("Finish A1 Course", context);
        Assert.Contains("Learning & Skills", context);
        Assert.Contains("Conversational fluency", context);
        Assert.Contains("[Completed] Step 1", context);
        Assert.Contains("Feeling confident about vocabulary", context);
    }

    [Fact]
    public async Task GenerateGoalDetailsAsync_WithAllNonBlankFields_PreservesAndIncorporatesCustomFields()
    {
        var targetDate = new DateTime(2026, 10, 20);
        var existing = new List<string> { "Step One Already Done" };

        var result = await _aiGoalService.GenerateGoalDetailsAsync(
            goalTitleOrPrompt: "Custom Project Plan",
            category: "Finance & Wealth",
            targetDate: targetDate,
            description: "Custom user description providing specific details",
            existingMilestones: existing,
            newMilestoneDraft: "Next Pending Step");

        Assert.NotNull(result);
        Assert.Equal("Finance & Wealth", result.Category);
        Assert.Equal(targetDate, result.TargetDate);
        Assert.Equal("Custom user description providing specific details", result.Description);
        Assert.Contains(result.SuggestedMilestones, m => m.Contains("Step One Already Done", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SuggestedMilestones, m => m.Contains("Next Pending Step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildGoalPromptContext_IncludesAvailableTags()
    {
        var tags = new List<string> { "Work", "Urgent", "Personal" };
        var context = AiGoalService.BuildGoalPromptContext(
            goalTitleOrPrompt: "Complete Q3 Audit",
            category: null,
            targetDate: null,
            description: null,
            existingMilestones: null,
            newMilestoneDraft: null,
            availableTags: tags);

        Assert.Contains("Available User Tags / Categories: Work, Urgent, Personal", context);
    }

    [Fact]
    public void BuildMilestonesPromptContext_IncludesAvailableTags()
    {
        var tags = new List<string> { "Frontend", "UI/UX" };
        var context = AiGoalService.BuildMilestonesPromptContext(
            goalTitle: "Redesign Landing Page",
            category: "Design",
            description: "Modern look",
            existingMilestones: null,
            targetDate: null,
            newMilestoneDraft: null,
            availableTags: tags);

        Assert.Contains("Available User Tags / Categories: Frontend, UI/UX", context);
    }

    [Fact]
    public void BuildJournalPromptContext_IncludesAvailableTags()
    {
        var tags = new List<string> { "Habits", "Fitness" };
        var context = AiGoalService.BuildJournalPromptContext(
            goalTitle: "Morning Routine",
            completedSteps: 2,
            totalSteps: 5,
            recentMilestone: "Wake up at 6am",
            category: "Health & Fitness",
            description: "Build consistency",
            allMilestones: null,
            currentJournalDraft: "Great day!",
            availableTags: tags);

        Assert.Contains("Available User Tags / Categories: Habits, Fitness", context);
    }

    [Fact]
    public async Task GenerateGoalDetailsAsync_WithMatchingAvailableTag_PicksAvailableTagAsCategory()
    {
        var availableTags = new List<string> { "Mobile Dev", "Cloud Architecture", "Finance" };

        var result = await _aiGoalService.GenerateGoalDetailsAsync(
            goalTitleOrPrompt: "Build Mobile Dev application in Avalonia",
            availableTags: availableTags);

        Assert.NotNull(result);
        Assert.Equal("Mobile Dev", result.Category);
    }
}


