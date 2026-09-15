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
}


