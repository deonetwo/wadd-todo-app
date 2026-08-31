using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class AiGoalService : IAiGoalService
{
    private readonly HttpClient _httpClient;

    public string Provider { get; set; } = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string? CustomBaseUrl { get; set; }
    public string? CustomModel { get; set; }

    public string GeminiApiKey
    {
        get => ApiKey;
        set => ApiKey = value?.Trim() ?? string.Empty;
    }

    public AiGoalService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();

        // Load saved AI settings if available
        var settings = AppSettingsHelper.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.AiProvider))
        {
            Provider = settings.AiProvider.Trim();
        }
        if (!string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            ApiKey = settings.AiApiKey.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
        {
            ApiKey = settings.GeminiApiKey.Trim();
        }
        CustomBaseUrl = settings.AiCustomBaseUrl;
        CustomModel = settings.AiCustomModel;
    }

    public async Task<GoalGenerationResult> GenerateGoalDetailsAsync(string goalTitleOrPrompt)
    {
        var rawPrompt = goalTitleOrPrompt?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            return new GoalGenerationResult
            {
                Title = "New Life Goal",
                Category = "Personal Growth",
                TargetDate = DateTime.Today.AddMonths(3),
                Description = "Define your vision, clear strategy, and daily habits to achieve this goal.",
                SuggestedMilestones = new List<string> { "Research and create initial plan", "Establish weekly routine", "Complete first major milestone", "Review and refine progress" },
                IsLiveAi = false,
                SourceLabel = "Smart Offline Engine"
            };
        }

        if (IsLiveAiAvailable())
        {
            try
            {
                var liveResult = IsGeminiProvider()
                    ? await GenerateGoalWithGeminiAsync(rawPrompt)
                    : await GenerateGoalWithOpenAiAsync(rawPrompt);

                if (liveResult != null)
                {
                    liveResult.IsLiveAi = true;
                    liveResult.SourceLabel = IsGeminiProvider()
                        ? $"Google Gemini ({(!string.IsNullOrWhiteSpace(CustomModel) ? CustomModel : "gemini-2.0-flash")})"
                        : $"{Provider} ({(!string.IsNullOrWhiteSpace(CustomModel) ? CustomModel : GetDefaultModelForProvider(Provider))})";
                    return liveResult;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[AiGoalService] Live AI call failed, falling back to smart heuristic: {ex.Message}");
            }
        }

        var offline = GenerateGoalSmartOffline(rawPrompt);
        offline.IsLiveAi = false;
        offline.SourceLabel = "Smart Offline Engine";
        return offline;
    }

    public async Task<IReadOnlyList<string>> GenerateMilestonesAsync(string goalTitle, string? category, string? description, IReadOnlyList<string>? existingMilestones = null)
    {
        var title = goalTitle?.Trim() ?? "My Goal";

        if (IsLiveAiAvailable())
        {
            try
            {
                var liveMilestones = IsGeminiProvider()
                    ? await GenerateMilestonesWithGeminiAsync(title, category, description, existingMilestones)
                    : await GenerateMilestonesWithOpenAiAsync(title, category, description, existingMilestones);

                if (liveMilestones != null && liveMilestones.Count > 0)
                {
                    return liveMilestones;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[AiGoalService] Live AI milestones call failed, falling back to smart heuristic: {ex.Message}");
            }
        }

        return GenerateMilestonesSmartOffline(title, category, description, existingMilestones);
    }

    public async Task<JournalDraftResult> GenerateJournalPromptAsync(string goalTitle, int completedSteps, int totalSteps, string? recentMilestone = null)
    {
        var title = goalTitle?.Trim() ?? "My Goal";

        if (IsLiveAiAvailable())
        {
            try
            {
                var liveJournal = IsGeminiProvider()
                    ? await GenerateJournalWithGeminiAsync(title, completedSteps, totalSteps, recentMilestone)
                    : await GenerateJournalWithOpenAiAsync(title, completedSteps, totalSteps, recentMilestone);

                if (liveJournal != null)
                {
                    liveJournal.IsLiveAi = true;
                    liveJournal.SourceLabel = IsGeminiProvider()
                        ? $"Google Gemini ({(!string.IsNullOrWhiteSpace(CustomModel) ? CustomModel : "gemini-2.0-flash")})"
                        : $"{Provider} ({(!string.IsNullOrWhiteSpace(CustomModel) ? CustomModel : GetDefaultModelForProvider(Provider))})";
                    return liveJournal;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[AiGoalService] Live AI journal call failed, falling back to smart heuristic: {ex.Message}");
            }
        }

        var offline = GenerateJournalSmartOffline(title, completedSteps, totalSteps, recentMilestone);
        offline.IsLiveAi = false;
        offline.SourceLabel = "Smart Offline Engine";
        return offline;
    }

    public async Task<IReadOnlyList<AiModelOption>> GetAvailableModelsAsync(string? provider = null, string? apiKey = null, string? customBaseUrl = null)
    {
        var targetProvider = (provider ?? Provider ?? "Gemini").Trim();
        var targetKey = (apiKey ?? ApiKey ?? string.Empty).Trim();
        var targetBaseUrl = (customBaseUrl ?? CustomBaseUrl ?? string.Empty).Trim();

        try
        {
            var liveModels = await FetchLiveModelsAsync(targetProvider, targetKey, targetBaseUrl);
            if (liveModels != null && liveModels.Count > 0)
            {
                return liveModels;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[AiGoalService] Fetch live models failed for {targetProvider}: {ex.Message}");
        }

        return GetCuratedFallbackModels(targetProvider);
    }

    public async Task<(bool Success, string Message, string? ModelName)> TestConnectionAsync(string? provider = null, string? apiKey = null, string? model = null, string? customBaseUrl = null)
    {
        var targetProvider = (provider ?? Provider ?? "Gemini").Trim();
        var targetKey = (apiKey ?? ApiKey ?? string.Empty).Trim();
        var targetModel = (model ?? CustomModel ?? string.Empty).Trim();
        var targetBaseUrl = (customBaseUrl ?? CustomBaseUrl ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(targetKey) && !targetProvider.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "No API key entered. Please enter your API key first.", null);
        }

        if (targetProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) || targetProvider.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase))
        {
            var effectiveModel = !string.IsNullOrWhiteSpace(targetModel) ? targetModel : "gemini-2.0-flash";
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = "Ping" }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens = 2
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{effectiveModel}:generateContent?key={Uri.EscapeDataString(targetKey)}";
                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.PostAsync(url, jsonContent, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Connected successfully to Google Gemini ({effectiveModel})! Live AI is active.", effectiveModel);
                }

                var errorText = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;
                return (false, $"Gemini error ({statusCode}): {GetCleanErrorMessage(errorText, statusCode)}", effectiveModel);
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}", effectiveModel);
            }
        }
        else
        {
            var (endpoint, defaultModel) = GetProviderEndpointAndDefault(targetProvider, targetBaseUrl);
            var effectiveModel = !string.IsNullOrWhiteSpace(targetModel) ? targetModel : defaultModel;

            try
            {
                var requestBody = new
                {
                    model = effectiveModel,
                    messages = new[]
                    {
                        new { role = "user", content = "Ping" }
                    },
                    max_tokens = 2
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(targetKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetKey);
                }
                if (targetProvider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/dewanto/wadd-todo-app");
                    request.Headers.TryAddWithoutValidation("X-Title", "Wadd ToDo");
                }

                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Connected successfully to {targetProvider} ({effectiveModel})! Live AI is active.", effectiveModel);
                }

                var errorText = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;
                return (false, $"{targetProvider} error ({statusCode}): {GetCleanErrorMessage(errorText, statusCode)}", effectiveModel);
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}", effectiveModel);
            }
        }
    }

    private static string GetCleanErrorMessage(string rawResponse, int statusCode)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(rawResponse))
            {
                using var doc = JsonDocument.Parse(rawResponse);
                if (doc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    if (errorEl.ValueKind == JsonValueKind.Object && errorEl.TryGetProperty("message", out var msgProp))
                    {
                        return msgProp.GetString() ?? $"HTTP {statusCode}";
                    }
                    if (errorEl.ValueKind == JsonValueKind.String)
                    {
                        return errorEl.GetString() ?? $"HTTP {statusCode}";
                    }
                }
            }
        }
        catch { }

        return statusCode switch
        {
            401 => "Invalid API key or unauthorized.",
            403 => "Forbidden or quota / credits exceeded.",
            404 => "Model or endpoint not found.",
            429 => "Rate limit exceeded or out of credits.",
            _ => $"HTTP {statusCode}"
        };
    }

    public static string GetDefaultModelForProvider(string? provider)
    {
        return (provider ?? string.Empty).ToLowerInvariant() switch
        {
            "openrouter" => "openai/gpt-4o-mini",
            "openai" => "gpt-4o-mini",
            "groq" => "llama-3.3-70b-versatile",
            "deepseek" => "deepseek-chat",
            "custom" => "llama3.2",
            "gemini" => "gemini-2.0-flash",
            _ => "openai/gpt-4o-mini"
        };
    }

    private static (string Endpoint, string DefaultModel) GetProviderEndpointAndDefault(string provider, string? customBaseUrl)
    {
        var prov = (provider ?? string.Empty).Trim().ToLowerInvariant();

        switch (prov)
        {
            case "openrouter":
                return ("https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4o-mini");

            case "groq":
                return ("https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile");

            case "deepseek":
                return ("https://api.deepseek.com/chat/completions", "deepseek-chat");

            case "custom":
                var baseUri = string.IsNullOrWhiteSpace(customBaseUrl)
                    ? "https://api.openai.com/v1"
                    : customBaseUrl.Trim().TrimEnd('/');
                if (!baseUri.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    baseUri += "/chat/completions";
                }
                return (baseUri, "gpt-4o-mini");

            case "openai":
            default:
                return ("https://api.openai.com/v1/chat/completions", "gpt-4o-mini");
        }
    }

    private bool IsLiveAiAvailable()
    {
        if (Provider.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(CustomBaseUrl) || !string.IsNullOrWhiteSpace(ApiKey);
        }
        return !string.IsNullOrWhiteSpace(ApiKey);
    }

    private bool IsGeminiProvider()
    {
        return string.IsNullOrWhiteSpace(Provider) || Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) || Provider.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase);
    }

    #region Gemini API Live Generation

    private async Task<GoalGenerationResult?> GenerateGoalWithGeminiAsync(string userPrompt)
    {
        var systemPrompt = @"You are a world-class life coach and productivity expert.
Given a user's goal idea or title, output a clean JSON object with:
- title: concise, motivating goal title (max 50 chars)
- category: one of 'Health & Fitness', 'Career & Business', 'Finance & Wealth', 'Learning & Skills', 'Personal Growth', 'Travel & Lifestyle'
- estimatedDays: realistic integer number of days to achieve this goal (e.g. 30, 60, 90, 180, 365)
- description: a concise, inspiring 2-3 sentence vision and strategy breakdown explaining why it matters and the key habit required
- suggestedMilestones: an array of 3 to 5 sequential, highly actionable milestone steps

Return strictly valid JSON only without markdown code fences.";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = $"{systemPrompt}\n\nGoal Idea: \"{userPrompt}\"" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json"
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, jsonContent);
        if (!response.IsSuccessStatusCode)
        {
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
            response = await _httpClient.PostAsync(url, jsonContent);
        }

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseGoalGenerationJson(text, userPrompt);
    }

    private async Task<List<string>?> GenerateMilestonesWithGeminiAsync(string goalTitle, string? category, string? description, IReadOnlyList<string>? existingMilestones = null)
    {
        string promptText;
        if (existingMilestones != null && existingMilestones.Count > 0)
        {
            var existingList = string.Join("\n", existingMilestones.Select((m, i) => $"{i + 1}. {m}"));
            promptText = $@"You are an expert goal decomposition assistant.
Goal: ""{goalTitle}""
Category: ""{category}""
Vision: ""{description}""

The user ALREADY HAS the following milestones in their plan:
{existingList}

TASK: Generate an array of 2 to 4 sequential, concrete NEXT milestone steps that logically continue and build upon their current progress without repeating or duplicating any of the existing steps.
Return strictly valid JSON only: {{ ""milestones"": [""Next Step 1..."", ""Next Step 2..."", ...] }}";
        }
        else
        {
            promptText = $@"You are an expert goal decomposition assistant.
Given a goal title, category, and vision, generate an array of 3 to 5 sequential, chronological, and concrete milestone steps to achieve it.
Goal: ""{goalTitle}""
Category: ""{category}""
Vision: ""{description}""
Return strictly valid JSON only: {{ ""milestones"": [""Step 1..."", ""Step 2..."", ...] }}";
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = promptText }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json"
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, jsonContent);
        if (!response.IsSuccessStatusCode)
        {
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
            response = await _httpClient.PostAsync(url, jsonContent);
        }

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseMilestonesJson(text);
    }

    private async Task<JournalDraftResult?> GenerateJournalWithGeminiAsync(string goalTitle, int completedSteps, int totalSteps, string? recentMilestone)
    {
        var systemPrompt = @"You are an encouraging, insightful reflection coach.
Given a user's goal progress, generate a reflective journal entry draft with:
- title: engaging reflection title (e.g. 'Finding Momentum', 'Lessons from Step 2', 'Weekly Check-in')
- content: a 2-3 paragraph inspiring reflection asking 1-2 thought-provoking questions, acknowledging progress made, and encouraging next steps.

Return strictly valid JSON only: { ""title"": ""..."", ""content"": ""..."" }";

        var context = $"Goal: \"{goalTitle}\"\nProgress: {completedSteps}/{totalSteps} steps completed.";
        if (!string.IsNullOrWhiteSpace(recentMilestone))
        {
            context += $"\nLatest milestone: \"{recentMilestone}\"";
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = $"{systemPrompt}\n\n{context}" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.6,
                responseMimeType = "application/json"
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
        var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, jsonContent);
        if (!response.IsSuccessStatusCode)
        {
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Uri.EscapeDataString(ApiKey)}";
            response = await _httpClient.PostAsync(url, jsonContent);
        }

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseJournalJson(text);
    }

    #endregion

    #region OpenAI & Multi-Provider Compatible Generation

    private (string Endpoint, string Model) ResolveOpenAiConfig()
    {
        var prov = (Provider ?? string.Empty).Trim().ToLowerInvariant();

        switch (prov)
        {
            case "openrouter":
                return ("https://openrouter.ai/api/v1/chat/completions",
                    string.IsNullOrWhiteSpace(CustomModel) ? "openai/gpt-4o-mini" : CustomModel.Trim());

            case "groq":
                return ("https://api.groq.com/openai/v1/chat/completions",
                    string.IsNullOrWhiteSpace(CustomModel) ? "llama-3.3-70b-versatile" : CustomModel.Trim());

            case "deepseek":
                return ("https://api.deepseek.com/chat/completions",
                    string.IsNullOrWhiteSpace(CustomModel) ? "deepseek-chat" : CustomModel.Trim());

            case "custom":
                var baseUri = string.IsNullOrWhiteSpace(CustomBaseUrl)
                    ? "https://api.openai.com/v1"
                    : CustomBaseUrl.Trim().TrimEnd('/');
                if (!baseUri.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    baseUri += "/chat/completions";
                }
                return (baseUri,
                    string.IsNullOrWhiteSpace(CustomModel) ? "gpt-4o-mini" : CustomModel.Trim());

            case "openai":
            default:
                return ("https://api.openai.com/v1/chat/completions",
                    string.IsNullOrWhiteSpace(CustomModel) ? "gpt-4o-mini" : CustomModel.Trim());
        }
    }

    private async Task<string?> SendOpenAiChatRequestAsync(string systemPrompt, string userPrompt)
    {
        var prov = (Provider ?? string.Empty).Trim().ToLowerInvariant();
        var (endpoint, model) = ResolveOpenAiConfig();

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.5,
            response_format = new { type = "json_object" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        if (prov == "openrouter")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/deonetwo/wadd-todo-app");
            request.Headers.TryAddWithoutValidation("X-Title", "Wadd ToDo");
        }

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Some local LLMs (e.g. older Ollama) might reject response_format parameter. Retry without it.
            var fallbackBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.5
            };

            using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, endpoint);
            fallbackReq.Content = new StringContent(JsonSerializer.Serialize(fallbackBody), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                fallbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }

            if (prov == "openrouter")
            {
                fallbackReq.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/deonetwo/wadd-todo-app");
                fallbackReq.Headers.TryAddWithoutValidation("X-Title", "Wadd ToDo");
            }

            response = await _httpClient.SendAsync(fallbackReq);
        }

        if (!response.IsSuccessStatusCode) return null;

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content;
    }

    private async Task<GoalGenerationResult?> GenerateGoalWithOpenAiAsync(string userPrompt)
    {
        var systemPrompt = @"You are a world-class life coach and productivity expert.
Given a user's goal idea or title, output a clean JSON object with:
- title: concise, motivating goal title (max 50 chars)
- category: one of 'Health & Fitness', 'Career & Business', 'Finance & Wealth', 'Learning & Skills', 'Personal Growth', 'Travel & Lifestyle'
- estimatedDays: realistic integer number of days to achieve this goal (e.g. 30, 60, 90, 180, 365)
- description: a concise, inspiring 2-3 sentence vision and strategy breakdown explaining why it matters and the key habit required
- suggestedMilestones: an array of 3 to 5 sequential, highly actionable milestone steps

Return strictly valid JSON only without markdown code fences.";

        var text = await SendOpenAiChatRequestAsync(systemPrompt, $"Goal Idea: \"{userPrompt}\"");
        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseGoalGenerationJson(text, userPrompt);
    }

    private async Task<List<string>?> GenerateMilestonesWithOpenAiAsync(string goalTitle, string? category, string? description, IReadOnlyList<string>? existingMilestones = null)
    {
        string systemPrompt;
        string userPrompt;

        if (existingMilestones != null && existingMilestones.Count > 0)
        {
            var existingList = string.Join("\n", existingMilestones.Select((m, i) => $"{i + 1}. {m}"));
            systemPrompt = @"You are an expert goal decomposition assistant.
The user already has milestones for their goal. Generate 2 to 4 sequential, concrete NEXT milestone steps that logically continue and build upon their current progress without repeating or duplicating any of the existing steps.
Return strictly valid JSON only: { ""milestones"": [""Next Step 1..."", ""Next Step 2..."", ...] }";
            userPrompt = $"Goal: \"{goalTitle}\"\nCategory: \"{category}\"\nVision: \"{description}\"\n\nExisting Milestones:\n{existingList}";
        }
        else
        {
            systemPrompt = @"You are an expert goal decomposition assistant.
Given a goal title, category, and vision, generate an array of 3 to 5 sequential, chronological, and concrete milestone steps to achieve it.
Return strictly valid JSON only: { ""milestones"": [""Step 1..."", ""Step 2..."", ...] }";
            userPrompt = $"Goal: \"{goalTitle}\"\nCategory: \"{category}\"\nVision: \"{description}\"";
        }

        var text = await SendOpenAiChatRequestAsync(systemPrompt, userPrompt);
        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseMilestonesJson(text);
    }

    private async Task<JournalDraftResult?> GenerateJournalWithOpenAiAsync(string goalTitle, int completedSteps, int totalSteps, string? recentMilestone)
    {
        var systemPrompt = @"You are an encouraging, insightful reflection coach.
Given a user's goal progress, generate a reflective journal entry draft with:
- title: engaging reflection title (e.g. 'Finding Momentum', 'Lessons from Step 2', 'Weekly Check-in')
- content: a 2-3 paragraph inspiring reflection asking 1-2 thought-provoking questions, acknowledging progress made, and encouraging next steps.

Return strictly valid JSON only: { ""title"": ""..."", ""content"": ""..."" }";

        var context = $"Goal: \"{goalTitle}\"\nProgress: {completedSteps}/{totalSteps} steps completed.";
        if (!string.IsNullOrWhiteSpace(recentMilestone))
        {
            context += $"\nLatest milestone: \"{recentMilestone}\"";
        }

        var text = await SendOpenAiChatRequestAsync(systemPrompt, context);
        if (string.IsNullOrWhiteSpace(text)) return null;

        return ParseJournalJson(text);
    }

    #endregion

    #region JSON Parsing Helpers

    private static GoalGenerationResult ParseGoalGenerationJson(string text, string fallbackTitle)
    {
        var cleanJson = CleanJsonString(text);
        using var parsedGoal = JsonDocument.Parse(cleanJson);
        var root = parsedGoal.RootElement;

        var result = new GoalGenerationResult
        {
            Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? fallbackTitle : fallbackTitle,
            Category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "Personal Growth" : "Personal Growth",
            Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty
        };

        int days = 90;
        if (root.TryGetProperty("estimatedDays", out var ed) && ed.TryGetInt32(out var parsedDays))
        {
            days = Math.Clamp(parsedDays, 7, 3650);
        }
        result.TargetDate = DateTime.Today.AddDays(days);

        if (root.TryGetProperty("suggestedMilestones", out var sm) && sm.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sm.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result.SuggestedMilestones.Add(s.Trim());
                }
            }
        }

        return result;
    }

    private static List<string>? ParseMilestonesJson(string text)
    {
        var cleanJson = CleanJsonString(text);
        using var parsed = JsonDocument.Parse(cleanJson);
        if (parsed.RootElement.TryGetProperty("milestones", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }
            return list;
        }
        return null;
    }

    private static JournalDraftResult ParseJournalJson(string text)
    {
        var cleanJson = CleanJsonString(text);
        using var parsed = JsonDocument.Parse(cleanJson);
        var root = parsed.RootElement;

        return new JournalDraftResult
        {
            Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Progress Reflection" : "Progress Reflection",
            Content = root.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty
        };
    }

    private static string CleanJsonString(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(7);
        }
        else if (text.StartsWith("```"))
        {
            text = text.Substring(3);
        }

        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - 3);
        }

        return text.Trim();
    }

    #endregion

    #region Smart Offline Heuristic Engine

    private static GoalGenerationResult GenerateGoalSmartOffline(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        var category = DetectCategory(lower);
        var (days, date) = EstimateTimeline(lower);
        var (title, description, milestones) = GenerateDomainTemplates(prompt, category);

        return new GoalGenerationResult
        {
            Title = title,
            Category = category,
            TargetDate = date,
            Description = description,
            SuggestedMilestones = milestones
        };
    }

    private static string DetectCategory(string lower)
    {
        if (ContainsAny(lower, "run", "marathon", "workout", "weight", "diet", "gym", "yoga", "health", "muscle", "walk", "cycle", "swim", "fitness", "sleep", "water", "cardio"))
            return "Health & Fitness";

        if (ContainsAny(lower, "job", "career", "promotion", "business", "startup", "launch", "sales", "client", "company", "interview", "resume", "marketing", "lead", "revenue", "product"))
            return "Career & Business";

        if (ContainsAny(lower, "save", "money", "invest", "budget", "debt", "stock", "crypto", "fund", "property", "real estate", "portfolio", "net worth", "emergency fund"))
            return "Finance & Wealth";

        if (ContainsAny(lower, "learn", "study", "master", "language", "python", "c#", "code", "book", "guitar", "piano", "course", "degree", "exam", "cert", "avalonia", "japanese", "spanish"))
            return "Learning & Skills";

        if (ContainsAny(lower, "travel", "trip", "visit", "vacation", "japan", "europe", "country", "flight", "passport", "adventure", "explore"))
            return "Travel & Lifestyle";

        return "Personal Growth";
    }

    private static (int Days, DateTime TargetDate) EstimateTimeline(string lower)
    {
        int days = 90; // Default 3 months

        if (ContainsAny(lower, "year", "annual", "365", "long term", "mastery"))
            days = 365;
        else if (ContainsAny(lower, "half year", "6 month", "semester", "marathon", "fluent"))
            days = 180;
        else if (ContainsAny(lower, "month", "30 day", "habit", "sprint", "fast", "starter"))
            days = 30;
        else if (ContainsAny(lower, "quarter", "3 month", "90 day", "season"))
            days = 90;

        return (days, DateTime.Today.AddDays(days));
    }

    private static (string Title, string Description, List<string> Milestones) GenerateDomainTemplates(string prompt, string category)
    {
        var cleanTitle = CapitalizeWords(prompt.Trim());

        switch (category)
        {
            case "Health & Fitness":
                return (
                    cleanTitle,
                    $"Establish a consistent, disciplined training routine for '{cleanTitle}'. Focus on daily physical consistency, optimal nutrition, and tracking gradual improvements week by week.",
                    new List<string>
                    {
                        "Assess baseline fitness & schedule weekly training blocks",
                        "Complete first 30 days of consistent training without missing",
                        "Hit intermediate milestone target and review recovery habits",
                        "Final push & achieve primary fitness milestone"
                    }
                );

            case "Career & Business":
                return (
                    cleanTitle,
                    $"Execute a structured roadmap to accelerate professional growth and achieve '{cleanTitle}'. Align daily high-leverage actions with long-term career impact.",
                    new List<string>
                    {
                        "Define core requirements, scope, and key stakeholders",
                        "Build prototype / complete initial deliverable phase",
                        "Collect feedback, iterate, and refine strategic approach",
                        "Launch or finalize project for full career impact"
                    }
                );

            case "Finance & Wealth":
                return (
                    cleanTitle,
                    $"Take full control of financial freedom and execute strategic saving/investing to reach '{cleanTitle}'. Optimize expenses and automate consistent monthly contributions.",
                    new List<string>
                    {
                        "Audit current finances & set strict monthly allocation targets",
                        "Automate savings and eliminate unnecessary recurring expenses",
                        "Reach 50% of target financial milestone",
                        "Reach 100% target and establish next wealth horizon"
                    }
                );

            case "Learning & Skills":
                return (
                    cleanTitle,
                    $"Master the core fundamentals and practical applications of '{cleanTitle}'. Dedicate 30-45 minutes of deliberate practice daily to build enduring mastery.",
                    new List<string>
                    {
                        "Gather top study resources, documentation, and roadmap",
                        "Master foundational concepts and complete practical exercises",
                        "Build a real-world capstone project to cement learning",
                        "Review, document lessons learned, and share knowledge"
                    }
                );

            case "Travel & Lifestyle":
                return (
                    cleanTitle,
                    $"Plan and experience an unforgettable adventure for '{cleanTitle}'. Balance thoughtful planning with spontaneous exploration.",
                    new List<string>
                    {
                        "Research itinerary, dates, and estimate total budget",
                        "Book key travel arrangements, accommodation, and essentials",
                        "Finalize packing checklist and daily excursion plans",
                        "Embark on the trip and capture highlights in journal"
                    }
                );

            default: // Personal Growth
                return (
                    cleanTitle,
                    $"Cultivate transformative habits and intentional focus to accomplish '{cleanTitle}'. Emphasize small daily wins that compound into massive long-term results.",
                    new List<string>
                    {
                        "Set up environment and define non-negotiable daily habits",
                        "Maintain consistency through the first 21 days",
                        "Overcome initial friction and optimize daily workflow",
                        "Celebrate milestone achievement and integrate into identity"
                    }
                );
        }
    }

    private static IReadOnlyList<string> GenerateMilestonesSmartOffline(string title, string? category, string? description, IReadOnlyList<string>? existingMilestones = null)
    {
        var cat = category ?? DetectCategory(title.ToLowerInvariant());
        var (_, _, milestones) = GenerateDomainTemplates(title, cat);

        if (existingMilestones != null && existingMilestones.Count > 0)
        {
            var filtered = milestones.Where(m => !existingMilestones.Any(e => e.Contains(m, StringComparison.OrdinalIgnoreCase) || m.Contains(e, StringComparison.OrdinalIgnoreCase))).ToList();
            if (filtered.Count > 0) return filtered;

            return new List<string>
            {
                $"Review, refine, and optimize existing milestones for '{title}'",
                $"Finalize capstone and celebrate milestone achievement"
            };
        }

        return milestones;
    }

    private static JournalDraftResult GenerateJournalSmartOffline(string title, int completedSteps, int totalSteps, string? recentMilestone)
    {
        double pct = totalSteps > 0 ? (double)completedSteps / totalSteps * 100.0 : 0;
        string journalTitle;
        string content;

        if (pct >= 100)
        {
            journalTitle = $"Goal Achieved: Celebrating {title}!";
            content = $"Today marks a major milestone—completing '{title}'! Looking back at where this journey started, every small step and consistent effort has compounded into success.\n\nKey Reflections:\n• What was the most rewarding breakthrough along the way?\n• How has this achievement elevated confidence for the next chapter?";
        }
        else if (pct >= 50)
        {
            journalTitle = $"Halfway Check-in: Building Momentum on {title}";
            content = $"Progress check-in on '{title}' ({completedSteps}/{totalSteps} steps completed - {pct:0}% done).\n\nReflections for today:\n• The momentum is palpable. What routine or mindset shift has made the biggest difference?\n• What is one obstacle to anticipate in the next phase, and how can it be proactively addressed?";
        }
        else if (completedSteps > 0)
        {
            journalTitle = $"First Steps & Early Wins: {title}";
            content = $"Taking the initial steps toward '{title}'. Completing {completedSteps} step(s) has laid the initial groundwork.\n\nReflections for today:\n• The hardest part of any journey is starting. How did taking action feel today?\n• What is the single next milestone step to tackle tomorrow?";
        }
        else
        {
            journalTitle = $"Setting Intentions for {title}";
            content = $"Beginning the journey toward '{title}'. Clear vision and daily discipline will turn this aspiration into reality.\n\nReflections for today:\n• Why is this goal deeply meaningful to me right now?\n• What is one small action I can take within the next 24 hours to create immediate momentum?";
        }

        return new JournalDraftResult
        {
            Title = journalTitle,
            Content = content
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string CapitalizeWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
        }
        return string.Join(" ", words);
    }

    #endregion

    #region Model Discovery & Curated Catalogs

    private async Task<List<AiModelOption>?> FetchLiveModelsAsync(string provider, string apiKey, string customBaseUrl)
    {
        var prov = provider.ToLowerInvariant();
        if (prov == "gemini")
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return null;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var modelsArray)) return null;

            var list = new List<AiModelOption>();
            foreach (var item in modelsArray.EnumerateArray())
            {
                var rawName = item.GetProperty("name").GetString() ?? string.Empty;
                var id = rawName.StartsWith("models/") ? rawName.Substring(7) : rawName;
                if (!id.Contains("gemini", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Contains("embedding", StringComparison.OrdinalIgnoreCase) || id.Contains("aqa", StringComparison.OrdinalIgnoreCase)) continue;

                var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? id : id;
                var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                bool isRec = id.Equals("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase) || id.Equals("gemini-1.5-flash", StringComparison.OrdinalIgnoreCase);

                list.Add(new AiModelOption { Id = id, Name = displayName, Description = desc, IsRecommended = isRec });
            }
            return list.OrderByDescending(x => x.IsRecommended).ThenBy(x => x.Name).ToList();
        }

        if (prov == "openrouter")
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/deonetwo/wadd-todo-app");
            req.Headers.TryAddWithoutValidation("X-Title", "Wadd ToDo");

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArray)) return null;

            var list = new List<AiModelOption>();
            foreach (var item in dataArray.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                bool isRec = id.Equals("openai/gpt-4o-mini", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("anthropic/claude-3.5-sonnet", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("deepseek/deepseek-r1", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("meta-llama/llama-3.3-70b-instruct", StringComparison.OrdinalIgnoreCase);

                list.Add(new AiModelOption { Id = id, Name = name, Description = desc, IsRecommended = isRec });
            }
            return list.OrderByDescending(x => x.IsRecommended).ThenBy(x => x.Name).ToList();
        }

        if (prov == "openai" || prov == "groq" || prov == "deepseek" || prov == "custom")
        {
            string url;
            if (prov == "openai") url = "https://api.openai.com/v1/models";
            else if (prov == "groq") url = "https://api.groq.com/openai/v1/models";
            else if (prov == "deepseek") url = "https://api.deepseek.com/models";
            else
            {
                var baseUri = string.IsNullOrWhiteSpace(customBaseUrl) ? "https://api.openai.com/v1" : customBaseUrl.Trim().TrimEnd('/');
                if (baseUri.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    baseUri = baseUri.Substring(0, baseUri.Length - "/chat/completions".Length);
                }
                url = $"{baseUri}/models";
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArray)) return null;

            var list = new List<AiModelOption>();
            foreach (var item in dataArray.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (prov == "openai")
                {
                    if (!id.StartsWith("gpt") && !id.StartsWith("o1") && !id.StartsWith("o3") && !id.StartsWith("chatgpt")) continue;
                }

                bool isRec = id.Contains("gpt-4o-mini", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("llama-3.3-70b-versatile", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("deepseek-chat", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("llama3.2", StringComparison.OrdinalIgnoreCase);

                list.Add(new AiModelOption { Id = id, Name = id, Description = string.Empty, IsRecommended = isRec });
            }
            return list.OrderByDescending(x => x.IsRecommended).ThenBy(x => x.Id).ToList();
        }

        return null;
    }

    public static IReadOnlyList<AiModelOption> GetCuratedFallbackModels(string provider)
    {
        var prov = (provider ?? string.Empty).Trim().ToLowerInvariant();
        switch (prov)
        {
            case "gemini":
                return new List<AiModelOption>
                {
                    new() { Id = "gemini-2.0-flash", Name = "Gemini 2.0 Flash", Description = "Fast & versatile Next-Gen model", IsRecommended = true },
                    new() { Id = "gemini-2.0-flash-lite", Name = "Gemini 2.0 Flash Lite", Description = "Ultra-fast low latency" },
                    new() { Id = "gemini-1.5-flash", Name = "Gemini 1.5 Flash", Description = "High-speed multimodal" },
                    new() { Id = "gemini-1.5-pro", Name = "Gemini 1.5 Pro", Description = "Complex reasoning & coding" }
                };

            case "openrouter":
                return new List<AiModelOption>
                {
                    new() { Id = "openai/gpt-4o-mini", Name = "OpenAI: GPT-4o-mini", Description = "Affordable, smart, fast", IsRecommended = true },
                    new() { Id = "anthropic/claude-3.5-sonnet", Name = "Anthropic: Claude 3.5 Sonnet", Description = "Top-tier coding & reasoning", IsRecommended = true },
                    new() { Id = "deepseek/deepseek-r1", Name = "DeepSeek: R1", Description = "Open reasoning model", IsRecommended = true },
                    new() { Id = "google/gemini-2.0-flash-001", Name = "Google: Gemini 2.0 Flash", Description = "High-speed Google multimodal" },
                    new() { Id = "meta-llama/llama-3.3-70b-instruct", Name = "Meta: Llama 3.3 70B Instruct", Description = "Open flagship 70B model" },
                    new() { Id = "mistralai/mistral-large-2411", Name = "Mistral: Mistral Large", Description = "Flagship reasoning" }
                };

            case "groq":
                return new List<AiModelOption>
                {
                    new() { Id = "llama-3.3-70b-versatile", Name = "Llama 3.3 70B Versatile", Description = "High intelligence & speed", IsRecommended = true },
                    new() { Id = "llama-3.1-8b-instant", Name = "Llama 3.1 8B Instant", Description = "Ultra-fast response" },
                    new() { Id = "mixtral-8x7b-32768", Name = "Mixtral 8x7B", Description = "MoE architecture" },
                    new() { Id = "gemma2-9b-it", Name = "Gemma 2 9B IT", Description = "Google open model" }
                };

            case "deepseek":
                return new List<AiModelOption>
                {
                    new() { Id = "deepseek-chat", Name = "DeepSeek-V3 (deepseek-chat)", Description = "General reasoning & chat", IsRecommended = true },
                    new() { Id = "deepseek-reasoner", Name = "DeepSeek-R1 (deepseek-reasoner)", Description = "Deep Chain-of-Thought Reasoning" }
                };

            case "custom":
                return new List<AiModelOption>
                {
                    new() { Id = "llama3.2", Name = "Llama 3.2", Description = "Meta open weight model", IsRecommended = true },
                    new() { Id = "mistral", Name = "Mistral 7B", Description = "Fast lightweight model" },
                    new() { Id = "deepseek-r1", Name = "DeepSeek R1 Local", Description = "Reasoning model" },
                    new() { Id = "qwen2.5", Name = "Qwen 2.5", Description = "Alibaba open model" },
                    new() { Id = "gpt-4o-mini", Name = "GPT-4o-mini", Description = "Custom proxy default" }
                };

            case "openai":
            default:
                return new List<AiModelOption>
                {
                    new() { Id = "gpt-4o-mini", Name = "GPT-4o-mini", Description = "Fast, affordable, intelligent", IsRecommended = true },
                    new() { Id = "gpt-4o", Name = "GPT-4o", Description = "Flagship omni-model" },
                    new() { Id = "gpt-4-turbo", Name = "GPT-4 Turbo", Description = "High capability GPT-4" },
                    new() { Id = "gpt-3.5-turbo", Name = "GPT-3.5 Turbo", Description = "Legacy fast model" },
                    new() { Id = "o1-mini", Name = "o1-mini", Description = "Fast reasoning model" }
                };
        }
    }

    #endregion
}

