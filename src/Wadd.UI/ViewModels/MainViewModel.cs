using System.IO;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Enums;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;
using Wadd.Core.Models;
using Wadd.Services;
using Wadd.UI.Localization;
using Wadd.UI.Views;

namespace Wadd.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITodoService _todoService;
    private readonly IThemeService _themeService;
    private readonly ISyncService _syncService;
    private readonly IExportService _exportService;
    private readonly IGoalService _goalService;
    private readonly IStartupService _startupService;
    private readonly IAiGoalService _aiGoalService;
    private readonly INotificationService _notificationService;
    private readonly IAudioService _audioService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomAiProvider))]
    [NotifyPropertyChangedFor(nameof(IsOpenRouterAiProvider))]
    [NotifyPropertyChangedFor(nameof(ShowCustomModelField))]
    [NotifyPropertyChangedFor(nameof(ShowCustomBaseUrlField))]
    [NotifyPropertyChangedFor(nameof(AiKeyWatermark))]
    [NotifyPropertyChangedFor(nameof(AiKeyHelpText))]
    private string _aiProvider = "Gemini";

    partial void OnAiProviderChanged(string value)
    {
        _aiGoalService.Provider = value;
        SelectedAiProviderOption = AiProviderOptions.FirstOrDefault(x => x.Id.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? AiProviderOptions[0];

        // Autofill default model for this provider if empty or matching a known default
        if (string.IsNullOrWhiteSpace(AiCustomModel) || IsKnownDefaultModel(AiCustomModel))
        {
            AiCustomModel = GetDefaultModelForProvider(value);
        }

        SaveUserSettings();
        _ = LoadAvailableModelsAsync(forceLive: true);
    }

    public List<AiProviderOption> AiProviderOptions { get; } = new()
    {
        new AiProviderOption { Id = "Gemini", Name = "Google Gemini", Description = "Gemini 2.0 Flash / 1.5 Flash (Google AI Studio)", KeyWatermark = "Enter Gemini API key (AIzaSy...)", KeyHelpText = "Get a free API key at aistudio.google.com", DefaultModel = "gemini-2.0-flash" },
        new AiProviderOption { Id = "OpenRouter", Name = "OpenRouter", Description = "Unified API for 100+ models (Claude 3.5, DeepSeek, GPT-4o)", KeyWatermark = "Enter OpenRouter API key (sk-or-v1-...)", KeyHelpText = "Get an API key at openrouter.ai", DefaultModel = "openai/gpt-4o-mini" },
        new AiProviderOption { Id = "OpenAI", Name = "OpenAI (ChatGPT)", Description = "GPT-4o mini / GPT-4o Models", KeyWatermark = "Enter OpenAI API key (sk-proj-...)", KeyHelpText = "Get an API key at platform.openai.com", DefaultModel = "gpt-4o-mini" },
        new AiProviderOption { Id = "Groq", Name = "Groq Cloud", Description = "Llama 3.3 70B (Ultra-fast inference)", KeyWatermark = "Enter Groq API key (gsk_...)", KeyHelpText = "Get a free API key at console.groq.com", DefaultModel = "llama-3.3-70b-versatile" },
        new AiProviderOption { Id = "DeepSeek", Name = "DeepSeek", Description = "DeepSeek-V3 / DeepSeek-R1 Models", KeyWatermark = "Enter DeepSeek API key (sk-...)", KeyHelpText = "Get an API key at platform.deepseek.com", DefaultModel = "deepseek-chat" },
        new AiProviderOption { Id = "Custom", Name = "Custom / Local AI", Description = "OpenAI-Compatible (Ollama, LM Studio, LocalAI)", KeyWatermark = "Enter API key (if required)", KeyHelpText = "Enter custom endpoint URL and model name below", DefaultModel = "llama3.2" }
    };

    [ObservableProperty]
    private AiProviderOption? _selectedAiProviderOption;

    partial void OnSelectedAiProviderOptionChanged(AiProviderOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id) && value.Id != AiProvider)
        {
            AiProvider = value.Id;
        }
    }

    public bool IsCustomAiProvider => AiProvider.Equals("Custom", StringComparison.OrdinalIgnoreCase);
    public bool IsOpenRouterAiProvider => AiProvider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase);
    public bool ShowCustomModelField => true;
    public bool ShowCustomBaseUrlField => IsCustomAiProvider;

    public string AiKeyWatermark => SelectedAiProviderOption?.KeyWatermark ?? "Enter API key...";
    public string AiKeyHelpText => SelectedAiProviderOption?.KeyHelpText ?? string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<AiModelOption> AvailableModels { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<AiModelOption> FilteredModels { get; } = new();

    [ObservableProperty]
    private string _modelSearchQuery = string.Empty;

    partial void OnModelSearchQueryChanged(string value)
    {
        FilterAvailableModels();
    }

    [RelayCommand]
    private void ClearModelSearch()
    {
        ModelSearchQuery = string.Empty;
    }

    private void FilterAvailableModels()
    {
        var query = ModelSearchQuery?.Trim() ?? string.Empty;
        FilteredModels.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var m in AvailableModels)
            {
                FilteredModels.Add(m);
            }
        }
        else
        {
            foreach (var m in AvailableModels)
            {
                if (m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredModels.Add(m);
                }
            }
        }
    }

    [ObservableProperty]
    private AiModelOption? _selectedModelOption;

    partial void OnSelectedModelOptionChanged(AiModelOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id) && value.Id != AiCustomModel)
        {
            AiCustomModel = value.Id;
        }
    }

    [ObservableProperty]
    private bool _isLoadingModels;

    [ObservableProperty]
    private string _aiApiKey = string.Empty;

    partial void OnAiApiKeyChanged(string value)
    {
        _aiGoalService.ApiKey = value;

        // Autofill default model when API key is entered if model is empty or default
        if (!string.IsNullOrWhiteSpace(value) && (string.IsNullOrWhiteSpace(AiCustomModel) || IsKnownDefaultModel(AiCustomModel)))
        {
            AiCustomModel = GetDefaultModelForProvider(AiProvider);
        }

        SaveUserSettings();
        _ = LoadAvailableModelsAsync(forceLive: true);
    }

    [ObservableProperty]
    private string _aiCustomBaseUrl = string.Empty;

    partial void OnAiCustomBaseUrlChanged(string value)
    {
        _aiGoalService.CustomBaseUrl = value;
        SaveUserSettings();
        if (IsCustomAiProvider)
        {
            _ = LoadAvailableModelsAsync(forceLive: true);
        }
    }

    [ObservableProperty]
    private string _aiCustomModel = string.Empty;

    partial void OnAiCustomModelChanged(string value)
    {
        _aiGoalService.CustomModel = value;
        if (!string.IsNullOrWhiteSpace(value) && (SelectedModelOption == null || !SelectedModelOption.Id.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            var match = AvailableModels.FirstOrDefault(x => x.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedModelOption = match;
            }
            else
            {
                var customOpt = new AiModelOption { Id = value, Name = value, Description = "Custom Selected" };
                AvailableModels.Insert(0, customOpt);
                FilterAvailableModels();
                SelectedModelOption = customOpt;
            }
        }
        SaveUserSettings();
    }

    [RelayCommand]
    public async Task RefreshAvailableModelsAsync()
    {
        await LoadAvailableModelsAsync(forceLive: true);
    }

    public async Task LoadAvailableModelsAsync(bool forceLive = false)
    {
        if (IsLoadingModels) return;
        IsLoadingModels = true;

        try
        {
            var models = await _aiGoalService.GetAvailableModelsAsync(AiProvider, AiApiKey, AiCustomBaseUrl);
            AvailableModels.Clear();
            foreach (var m in models)
            {
                AvailableModels.Add(m);
            }
            FilterAvailableModels();

            if (!string.IsNullOrWhiteSpace(AiCustomModel))
            {
                var match = AvailableModels.FirstOrDefault(x => x.Id.Equals(AiCustomModel.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    SelectedModelOption = match;
                }
                else
                {
                    var customOpt = new AiModelOption { Id = AiCustomModel.Trim(), Name = AiCustomModel.Trim(), Description = "Custom Selected" };
                    AvailableModels.Insert(0, customOpt);
                    FilterAvailableModels();
                    SelectedModelOption = customOpt;
                }
            }
            else
            {
                var defaultModel = GetDefaultModelForProvider(AiProvider);
                var match = AvailableModels.FirstOrDefault(x => x.Id.Equals(defaultModel, StringComparison.OrdinalIgnoreCase)) ?? AvailableModels.FirstOrDefault();
                if (match != null)
                {
                    SelectedModelOption = match;
                    AiCustomModel = match.Id;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("MainViewModel", "LoadAvailableModelsAsync error", ex);
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    [ObservableProperty]
    private bool _isTestingAiConnection;

    [ObservableProperty]
    private string? _aiConnectionStatusMessage;

    [ObservableProperty]
    private bool? _isAiConnectionSuccess;

    [RelayCommand]
    public async Task TestAiConnectionAsync()
    {
        if (IsTestingAiConnection) return;

        try
        {
            IsTestingAiConnection = true;
            AiConnectionStatusMessage = "Testing live API connection...";
            ShowStatusBubble("Testing live API connection...", NotificationBubbleType.Info);
            IsAiConnectionSuccess = null;

            var (success, message, modelName) = await _aiGoalService.TestConnectionAsync(
                AiProvider,
                AiApiKey,
                AiCustomModel,
                AiCustomBaseUrl);

            IsAiConnectionSuccess = success;
            AiConnectionStatusMessage = message;
            ShowStatusBubble(message, success ? NotificationBubbleType.Success : NotificationBubbleType.Error);
        }
        catch (Exception ex)
        {
            IsAiConnectionSuccess = false;
            AiConnectionStatusMessage = $"Connection test failed: {ex.Message}";
            ShowStatusBubble($"Connection test failed: {ex.Message}", NotificationBubbleType.Error);
        }
        finally
        {
            IsTestingAiConnection = false;
        }
    }

    [RelayCommand]
    private void AutofillDefaultModel()
    {
        AiCustomModel = GetDefaultModelForProvider(AiProvider);
        var match = AvailableModels.FirstOrDefault(x => x.Id.Equals(AiCustomModel, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectedModelOption = match;
        }
    }

    [RelayCommand]
    public void SelectModelOption(AiModelOption? option)
    {
        if (option != null && !string.IsNullOrWhiteSpace(option.Id))
        {
            SelectedModelOption = option;
            AiCustomModel = option.Id;
        }
    }

    [ObservableProperty]
    private bool _isAiProviderPickerSheetOpen;

    [RelayCommand]
    private void OpenAiProviderPicker()
    {
        IsAiProviderPickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseAiProviderPicker()
    {
        IsAiProviderPickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectAiProviderFromSheet(AiProviderOption? option)
    {
        if (option != null)
        {
            SelectedAiProviderOption = option;
            IsAiProviderPickerSheetOpen = false;
        }
    }

    [ObservableProperty]
    private bool _isAiModelPickerSheetOpen;

    [RelayCommand]
    private void OpenAiModelPicker()
    {
        IsAiModelPickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseAiModelPicker()
    {
        IsAiModelPickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectAiModelOptionFromSheet(AiModelOption? option)
    {
        if (option != null)
        {
            SelectModelOption(option);
            IsAiModelPickerSheetOpen = false;
        }
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

    public static bool IsKnownDefaultModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return true;
        var clean = model.Trim().ToLowerInvariant();
        return clean is "gemini-2.0-flash" or "gemini-1.5-flash"
            or "openai/gpt-4o-mini" or "gpt-4o-mini" or "gpt-4o"
            or "llama-3.3-70b-versatile" or "llama-3.1-70b-versatile" or "llama3.2"
            or "deepseek-chat" or "deepseek-reasoner";
    }

    // Compatibility property
    public string GeminiApiKey
    {
        get => AiApiKey;
        set => AiApiKey = value;
    }

    public bool IsDesktopPlatform => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();
    public bool IsWindowsPlatform => OperatingSystem.IsWindows();
    public bool IsAndroidPlatform => OperatingSystem.IsAndroid();

    public event EventHandler<bool>? TrayIconVisibilityChanged;

    [ObservableProperty]
    private bool _autoStartOnBoot;

    partial void OnAutoStartOnBootChanged(bool value)
    {
        if (_startupService.IsSupported && _startupService.IsAutoStartEnabled() != value)
        {
            _startupService.SetAutoStart(value, StartMinimized);
        }
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _startMinimized;

    partial void OnStartMinimizedChanged(bool value)
    {
        if (AutoStartOnBoot)
        {
            _startupService.SetAutoStart(AutoStartOnBoot, value);
        }
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _minimizeToTray = true;

    partial void OnMinimizeToTrayChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _closeToTray = true;

    partial void OnCloseToTrayChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _enableTrayIcon = true;

    partial void OnEnableTrayIconChanged(bool value)
    {
        SaveUserSettings();
        TrayIconVisibilityChanged?.Invoke(this, value);
    }

    [ObservableProperty]
    private bool _enableNotifications = true;

    partial void OnEnableNotificationsChanged(bool value)
    {
        SaveUserSettings();
        if (value)
        {
            _ = _notificationService.RequestPermissionAsync();
        }
    }

    [ObservableProperty]
    private bool _notifyOnTaskReminder = true;

    partial void OnNotifyOnTaskReminderChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _notifyOnOverdueTasks = true;

    partial void OnNotifyOnOverdueTasksChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _notifyOnTaskDueDate = true;

    partial void OnNotifyOnTaskDueDateChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private int _notificationLeadTimeMinutes = 0;

    partial void OnNotificationLeadTimeMinutesChanged(int value)
    {
        SelectedNotificationLeadTimeOption = NotificationLeadTimeOptions.FirstOrDefault(x => x.Minutes == value) ?? NotificationLeadTimeOptions[0];
        SaveUserSettings();
    }

    public List<NotificationLeadTimeOption> NotificationLeadTimeOptions { get; } = new()
    {
        new NotificationLeadTimeOption { Minutes = 0, Name = "At time of event", Description = "Remind me right when the time arrives" },
        new NotificationLeadTimeOption { Minutes = 5, Name = "5 minutes before", Description = "Remind me 5 minutes in advance" },
        new NotificationLeadTimeOption { Minutes = 10, Name = "10 minutes before", Description = "Remind me 10 minutes in advance" },
        new NotificationLeadTimeOption { Minutes = 15, Name = "15 minutes before", Description = "Remind me 15 minutes in advance" },
        new NotificationLeadTimeOption { Minutes = 30, Name = "30 minutes before", Description = "Remind me 30 minutes in advance" },
        new NotificationLeadTimeOption { Minutes = 60, Name = "1 hour before", Description = "Remind me 1 hour in advance" }
    };

    [ObservableProperty]
    private NotificationLeadTimeOption? _selectedNotificationLeadTimeOption;

    partial void OnSelectedNotificationLeadTimeOptionChanged(NotificationLeadTimeOption? value)
    {
        if (value != null && value.Minutes != NotificationLeadTimeMinutes)
        {
            NotificationLeadTimeMinutes = value.Minutes;
        }
    }

    [ObservableProperty]
    private int _notificationRepeatIntervalMinutes = 0;

    partial void OnNotificationRepeatIntervalMinutesChanged(int value)
    {
        SelectedNotificationRepeatIntervalOption = NotificationRepeatIntervalOptions.FirstOrDefault(x => x.Minutes == value) ?? NotificationRepeatIntervalOptions[0];
        SaveUserSettings();
    }

    public List<NotificationRepeatIntervalOption> NotificationRepeatIntervalOptions { get; } = new()
    {
        new NotificationRepeatIntervalOption { Minutes = 0, Name = "Don't repeat (once only)", Description = "Remind me only once when the reminder or due date arrives" },
        new NotificationRepeatIntervalOption { Minutes = 30, Name = "Every 30 minutes", Description = "Remind me every 30 minutes until finished" },
        new NotificationRepeatIntervalOption { Minutes = 60, Name = "Every 1 hour", Description = "Remind me every hour until finished" },
        new NotificationRepeatIntervalOption { Minutes = 120, Name = "Every 2 hours", Description = "Remind me every 2 hours until finished" },
        new NotificationRepeatIntervalOption { Minutes = 180, Name = "Every 3 hours", Description = "Remind me every 3 hours until finished" },
        new NotificationRepeatIntervalOption { Minutes = 300, Name = "Every 5 hours", Description = "Remind me every 5 hours until finished" }
    };

    [ObservableProperty]
    private NotificationRepeatIntervalOption? _selectedNotificationRepeatIntervalOption;

    partial void OnSelectedNotificationRepeatIntervalOptionChanged(NotificationRepeatIntervalOption? value)
    {
        if (value != null && value.Minutes != NotificationRepeatIntervalMinutes)
        {
            NotificationRepeatIntervalMinutes = value.Minutes;
        }
    }

    [ObservableProperty]
    private bool _playNotificationSound = true;

    partial void OnPlayNotificationSoundChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _playTaskCompletedSound = true;

    partial void OnPlayTaskCompletedSoundChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _windowsToastNotifications = true;

    partial void OnWindowsToastNotificationsChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _androidVibration = true;

    partial void OnAndroidVibrationChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _androidHighPriorityChannel = true;

    partial void OnAndroidHighPriorityChannelChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private bool _androidStickyReminders;

    partial void OnAndroidStickyRemindersChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    private string _testNotificationFeedback = string.Empty;

    [RelayCommand]
    public async Task SendTestNotificationAsync()
    {
        try
        {
            if (EnableNotifications)
            {
                await _notificationService.RequestPermissionAsync();
            }
            var title = "Wadd Reminder (Test)";
            var message = "Notifications are enabled and working.";
            await _notificationService.ShowNotificationAsync(title, message, "test-notification");
            TestNotificationFeedback = $"Test notification sent ({DateTime.Now:HH:mm:ss}).";
            ShowStatusBubble("Test notification sent.", NotificationBubbleType.Success);
        }
        catch (Exception ex)
        {
            TestNotificationFeedback = $"Failed to send test notification: {ex.Message}";
            ShowStatusBubble($"Failed to send test notification: {ex.Message}", NotificationBubbleType.Error);
        }
    }

    private DispatcherTimer? _reminderTimer;
    private readonly HashSet<Guid> _notifiedReminderIds = new();
    private readonly HashSet<Guid> _notifiedDueDateIds = new();
    private readonly HashSet<Guid> _notifiedOverdueIds = new();
    private readonly Dictionary<Guid, DateTime> _lastNotifiedTimes = new();
    private DateTime _lastDueDateCheckDay = DateTime.Today;
    private bool _isInitialNotificationSeeded;

    private void StartReminderChecker()
    {
        // When the app opens, seed existing past reminders, overdue tasks, and due-today tasks
        // so Wadd never shows unwanted pop-up notifications immediately upon launching.
        SeedInitialNotificationState();

        if (EnableNotifications)
        {
            _ = _notificationService.RequestPermissionAsync();
        }

        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
    }

    public void SeedInitialNotificationState()
    {
        var now = DateTime.Now;
        var today = now.Date;

        foreach (var item in TodoItems)
        {
            if (item.IsCompleted) continue;

            if (item.ReminderAt.HasValue && item.ReminderAt.Value <= now)
            {
                _notifiedReminderIds.Add(item.Id);
                if (!_lastNotifiedTimes.ContainsKey(item.Id))
                {
                    _lastNotifiedTimes[item.Id] = now;
                }
            }

            bool isOverdue = (item.DueDate.HasValue && item.DueDate.Value.Date < today) ||
                             (item.ReminderAt.HasValue && item.ReminderAt.Value.Date < today);
            if (isOverdue)
            {
                _notifiedOverdueIds.Add(item.Id);
                if (!_lastNotifiedTimes.ContainsKey(item.Id))
                {
                    _lastNotifiedTimes[item.Id] = now;
                }
            }

            if (item.DueDate.HasValue && item.DueDate.Value.Date == today)
            {
                _notifiedDueDateIds.Add(item.Id);
                if (!_lastNotifiedTimes.ContainsKey(item.Id))
                {
                    _lastNotifiedTimes[item.Id] = now;
                }
            }
        }
    }

    private enum TaskNotificationCategory
    {
        Reminder,
        Overdue,
        DueToday
    }

    public void CheckReminders()
    {
        if (!EnableNotifications) return;

        var now = DateTime.Now;
        var today = now.Date;

        if (today != _lastDueDateCheckDay)
        {
            _lastDueDateCheckDay = today;
            _notifiedDueDateIds.Clear();
            _notifiedReminderIds.Clear();
            _notifiedOverdueIds.Clear();
        }

        var pendingAlerts = new List<(TodoItemViewModel Item, TaskNotificationCategory Category, string Title, string Body)>();

        foreach (var item in TodoItems)
        {
            if (item.IsCompleted)
            {
                _lastNotifiedTimes.Remove(item.Id);
                _notifiedDueDateIds.Remove(item.Id);
                _notifiedReminderIds.Remove(item.Id);
                _notifiedOverdueIds.Remove(item.Id);
                continue;
            }

            bool shouldRepeat = NotificationRepeatIntervalMinutes > 0
                && _lastNotifiedTimes.TryGetValue(item.Id, out var lastTime)
                && (now - lastTime).TotalMinutes >= NotificationRepeatIntervalMinutes;

            // 1. Check Scheduled Task Reminders for today
            if (NotifyOnTaskReminder && item.ReminderAt.HasValue)
            {
                var triggerTime = item.ReminderAt.Value.AddMinutes(-NotificationLeadTimeMinutes);
                if (triggerTime <= now)
                {
                    if (!_notifiedReminderIds.Contains(item.Id))
                    {
                        _notifiedReminderIds.Add(item.Id);
                        _lastNotifiedTimes[item.Id] = now;
                        var body = item.HasDescription
                            ? $"{item.Title} - {item.DescriptionPreview}"
                            : item.Title;
                        pendingAlerts.Add((item, TaskNotificationCategory.Reminder, $"Reminder: {item.Title}", body));
                        continue;
                    }
                    else if (shouldRepeat)
                    {
                        _lastNotifiedTimes[item.Id] = now;
                        var body = item.HasDescription
                            ? $"{item.Title} - {item.DescriptionPreview}"
                            : item.Title;
                        pendingAlerts.Add((item, TaskNotificationCategory.Reminder, $"Reminder: {item.Title}", body));
                        continue;
                    }
                    else if (!_lastNotifiedTimes.ContainsKey(item.Id))
                    {
                        _lastNotifiedTimes[item.Id] = now;
                    }
                }
            }

            // 2. Check Overdue Tasks
            if (NotifyOnOverdueTasks)
            {
                bool isOverdue = (item.DueDate.HasValue && item.DueDate.Value.Date < today) ||
                                 (item.ReminderAt.HasValue && item.ReminderAt.Value.Date < today);

                if (isOverdue)
                {
                    if (!_notifiedOverdueIds.Contains(item.Id))
                    {
                        _notifiedOverdueIds.Add(item.Id);
                        _lastNotifiedTimes[item.Id] = now;
                        var body = item.HasDescription
                            ? $"{item.Title} - {item.DescriptionPreview}"
                            : item.Title;
                        pendingAlerts.Add((item, TaskNotificationCategory.Overdue, $"Overdue: {item.Title}", body));
                        continue;
                    }
                    else if (shouldRepeat)
                    {
                        _lastNotifiedTimes[item.Id] = now;
                        var body = item.HasDescription
                            ? $"{item.Title} - {item.DescriptionPreview}"
                            : item.Title;
                        pendingAlerts.Add((item, TaskNotificationCategory.Overdue, $"Overdue: {item.Title}", body));
                        continue;
                    }
                    else if (!_lastNotifiedTimes.ContainsKey(item.Id))
                    {
                        _lastNotifiedTimes[item.Id] = now;
                    }
                }
            }

            // 3. Check Due Date Today Alerts
            if (NotifyOnTaskDueDate && item.DueDate.HasValue && item.DueDate.Value.Date == today)
            {
                if (!_notifiedDueDateIds.Contains(item.Id))
                {
                    _notifiedDueDateIds.Add(item.Id);
                    _lastNotifiedTimes[item.Id] = now;
                    var body = item.HasDescription
                        ? $"{item.Title} - {item.DescriptionPreview}"
                        : item.Title;
                    pendingAlerts.Add((item, TaskNotificationCategory.DueToday, $"Due Today: {item.Title}", body));
                }
                else if (shouldRepeat)
                {
                    _lastNotifiedTimes[item.Id] = now;
                    var body = item.HasDescription
                        ? $"{item.Title} - {item.DescriptionPreview}"
                        : item.Title;
                    pendingAlerts.Add((item, TaskNotificationCategory.DueToday, $"Due Today: {item.Title}", body));
                }
                else if (!_lastNotifiedTimes.ContainsKey(item.Id))
                {
                    _lastNotifiedTimes[item.Id] = now;
                }
            }
        }

        if (pendingAlerts.Count == 0)
        {
            return;
        }

        if (pendingAlerts.Count == 1)
        {
            var single = pendingAlerts[0];
            _ = _notificationService.ShowNotificationAsync(single.Title, single.Body, single.Item.Id.ToString());
            return;
        }

        // Multiple notifications triggered simultaneously -> Bundle into a single summary notification
        // to prevent sound spam, screen clutter, and OS throttling.
        var totalCount = pendingAlerts.Count;
        string bundleTitle;

        bool allReminders = pendingAlerts.All(p => p.Category == TaskNotificationCategory.Reminder);
        bool allOverdue = pendingAlerts.All(p => p.Category == TaskNotificationCategory.Overdue);
        bool allDueToday = pendingAlerts.All(p => p.Category == TaskNotificationCategory.DueToday);

        if (allReminders)
        {
            bundleTitle = $"{totalCount} Task Reminders";
        }
        else if (allOverdue)
        {
            bundleTitle = $"{totalCount} Overdue Tasks";
        }
        else if (allDueToday)
        {
            bundleTitle = $"{totalCount} Tasks Due Today";
        }
        else
        {
            bundleTitle = $"{totalCount} Task Reminders";
        }

        const int maxDisplayItems = 3;
        var displayLines = pendingAlerts.Take(maxDisplayItems)
            .Select(p => $"• {p.Item.Title}");

        string bundleBody;
        if (totalCount <= maxDisplayItems)
        {
            bundleBody = string.Join("\n", displayLines);
        }
        else
        {
            var remaining = totalCount - maxDisplayItems;
            bundleBody = string.Join("\n", displayLines) + $"\n+ {remaining} more tasks";
        }

        _ = _notificationService.ShowNotificationAsync(bundleTitle, bundleBody, "tasks-summary");
    }

    [ObservableProperty]
    private GoalsViewModel _goalsVM;

    partial void OnGoalsVMChanged(GoalsViewModel value)
    {
        if (value != null)
        {
            value.StatusNotificationRequested = (msg, type) => ShowStatusBubble(msg, type);
            value.DataMutated += () => RequestDebouncedAutoSync();
            value.GetAvailableTagsFunc = () => ExistingCategories;
        }
    }

    private readonly HashSet<Guid> _togglingTaskIds = new();

    private int _periodicSyncTicks;
    private DispatcherTimer? _autoSyncDebounceTimer;

    [ObservableProperty]
    private string _title = "Wadd - ToDo Application";

    [ObservableProperty]
    private bool _isConflictDialogVisible;

    [ObservableProperty] private bool _isDeleteConfirmationOpen;
    [ObservableProperty] private string _deleteConfirmationTitle = string.Empty;
    [ObservableProperty] private string _deleteConfirmationMessage = string.Empty;
    [ObservableProperty] private string _deleteConfirmationItemName = string.Empty;
    [ObservableProperty] private string _deleteConfirmationItemDetails = string.Empty;
    private TaskCompletionSource<bool>? _deleteConfirmationTcs;

    [ObservableProperty]
    private string _currentDateFormatted = DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant();

    [ObservableProperty]
    private string _currentDateFull = DateTime.Now.ToString("dddd, MMMM d, yyyy");

    public ObservableCollection<NotificationBubbleItem> StatusNotifications { get; } = new();

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public void ShowStatusBubble(string message, NotificationBubbleType? type = null)
    {
        if (string.IsNullOrWhiteSpace(message) || message == "Ready")
            return;

        var resolvedType = type ?? InferBubbleType(message);

        Action addAction = () =>
        {
            var notif = new NotificationBubbleItem(message, resolvedType);
            StatusNotifications.Add(notif);

            Task.Run(async () =>
            {
                await Task.Delay(4000);
                try
                {
                    if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
                    {
                        notif.Opacity = 0.0;
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => notif.Opacity = 0.0);
                    }
                }
                catch { }

                await Task.Delay(450);
                try
                {
                    if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
                    {
                        StatusNotifications.Remove(notif);
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => StatusNotifications.Remove(notif));
                    }
                }
                catch { }
            });
        };

        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            addAction();
        }
        else
        {
            Dispatcher.UIThread.Post(addAction);
        }
    }

    public static NotificationBubbleType InferBubbleType(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return NotificationBubbleType.Info;

        var lower = message.ToLowerInvariant();
        if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("failure"))
        {
            return NotificationBubbleType.Error;
        }

        if (lower.Contains("warning") || lower.Contains("cannot") || lower.Contains("exceeded") ||
            lower.Contains("require your review") || lower.Contains("past"))
        {
            return NotificationBubbleType.Warning;
        }

        if (lower.Contains("success") || lower.Contains("successfully") || lower.StartsWith("✓"))
        {
            return NotificationBubbleType.Success;
        }

        return NotificationBubbleType.Info;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value == "Ready" ||
            value.StartsWith("Loaded ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Syncing", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Sync finished.", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Signing in", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ShowStatusBubble(value);
    }

    [ObservableProperty]
    private ThemeMode _currentThemeMode = ThemeMode.System;

    [ObservableProperty]
    private string _currentThemeLabel = "System Default";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        UpdateSubCollections();
        UpdateSearchResults();
    }

    [ObservableProperty]
    private bool _showNotePreviewsInList = true;

    partial void OnShowNotePreviewsInListChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandardTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsFocusTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsCompletedFirstTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsTodayCompletedOnlyTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsTodayUpcomingOnlyTasksLayout))]
    [NotifyPropertyChangedFor(nameof(TodayTasksGridRow))]
    [NotifyPropertyChangedFor(nameof(CompletedTodayGridRow))]
    [NotifyPropertyChangedFor(nameof(UpcomingTasksGridRow))]
    [NotifyPropertyChangedFor(nameof(ShowUpcomingSection))]
    [NotifyPropertyChangedFor(nameof(ShowCompletedTodaySection))]
    [NotifyPropertyChangedFor(nameof(IsCompletedTodaySectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsUpcomingSectionVisible))]
    private string _tasksViewLayout = "Standard";

    partial void OnTasksViewLayoutChanged(string value)
    {
        SaveUserSettings();
    }

    public List<TasksLayoutOption> TasksLayoutOptions { get; } = new()
    {
        new TasksLayoutOption { Id = "Standard", Name = "Standard (Today → Completed → Upcoming)", Description = "Shows Today, Completed Today, and Upcoming tasks" },
        new TasksLayoutOption { Id = "Focus", Name = "Focus (Today → Upcoming → Completed)", Description = "Shows Today, Upcoming, and Completed tasks at bottom" },
        new TasksLayoutOption { Id = "CompletedFirst", Name = "Log First (Completed → Today → Upcoming)", Description = "Shows Completed Today at top, then Today and Upcoming" },
        new TasksLayoutOption { Id = "TodayCompletedOnly", Name = "Today & Completed Only", Description = "Shows Today Tasks and Completed Today (Hides Upcoming)" },
        new TasksLayoutOption { Id = "TodayUpcomingOnly", Name = "Today & Upcoming Only", Description = "Shows Today Tasks and Upcoming Tasks (Hides Completed)" }
    };

    [ObservableProperty]
    private TasksLayoutOption? _selectedTasksLayoutOption;

    [ObservableProperty]
    private bool _isTasksLayoutPickerSheetOpen;

    [RelayCommand]
    private void OpenTasksLayoutPicker()
    {
        IsTasksLayoutPickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseTasksLayoutPicker()
    {
        IsTasksLayoutPickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectTasksLayoutOption(TasksLayoutOption? option)
    {
        if (option != null)
        {
            SelectedTasksLayoutOption = option;
            IsTasksLayoutPickerSheetOpen = false;
        }
    }

    public List<LanguageOption> LanguageOptions { get; } = new()
    {
        new LanguageOption { Id = "system", Name = "System Default / Default Sistem", Description = "Follow device system language" },
        new LanguageOption { Id = "en", Name = "English", Description = "English (United States)" },
        new LanguageOption { Id = "id", Name = "Bahasa Indonesia", Description = "Indonesian" }
    };

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    [ObservableProperty]
    private string _language = "system";

    [ObservableProperty]
    private bool _isLanguagePickerSheetOpen;

    [RelayCommand]
    private void OpenLanguagePicker()
    {
        IsLanguagePickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseLanguagePicker()
    {
        IsLanguagePickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectLanguageOption(LanguageOption? option)
    {
        if (option != null)
        {
            SelectedLanguageOption = option;
            IsLanguagePickerSheetOpen = false;
        }
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id))
        {
            Language = value.Id;
            LocalizationManager.Instance.SetLanguage(value.Id);
            SaveUserSettings();
        }
    }

    partial void OnSelectedTasksLayoutOptionChanged(TasksLayoutOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id))
        {
            TasksViewLayout = value.Id;
        }
    }

    public bool IsStandardTasksLayout => TasksViewLayout == "Standard";
    public bool IsFocusTasksLayout => TasksViewLayout == "Focus";
    public bool IsCompletedFirstTasksLayout => TasksViewLayout == "CompletedFirst";
    public bool IsTodayCompletedOnlyTasksLayout => TasksViewLayout == "TodayCompletedOnly";
    public bool IsTodayUpcomingOnlyTasksLayout => TasksViewLayout == "TodayUpcomingOnly";

    public int TodayTasksGridRow => TasksViewLayout switch
    {
        "CompletedFirst" => 1,
        _ => 0
    };

    public int CompletedTodayGridRow => TasksViewLayout switch
    {
        "Focus" => 2,
        "CompletedFirst" => 0,
        _ => 1
    };

    public int UpcomingTasksGridRow => TasksViewLayout switch
    {
        "Focus" => 1,
        "CompletedFirst" => 2,
        "TodayUpcomingOnly" => 1,
        _ => 2
    };

    public bool ShowUpcomingSection => TasksViewLayout != "TodayCompletedOnly";
    public bool ShowCompletedTodaySection => TasksViewLayout != "TodayUpcomingOnly";

    public bool IsCompletedTodaySectionVisible => HasCompletedTodayTodoItems && ShowCompletedTodaySection;
    public bool IsUpcomingSectionVisible => ShowUpcomingSection;

    [RelayCommand]
    private void SetTasksViewLayout(string layout)
    {
        if (!string.IsNullOrWhiteSpace(layout))
        {
            TasksViewLayout = layout;
            SelectedTasksLayoutOption = TasksLayoutOptions.FirstOrDefault(x => x.Id == layout) ?? TasksLayoutOptions[0];
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpcomingTasksRangeLabel))]
    [NotifyPropertyChangedFor(nameof(IsUpcomingRangeFilterActive))]
    private string _upcomingTasksRange = "All";

    partial void OnUpcomingTasksRangeChanged(string value)
    {
        SelectedUpcomingTasksRangeOption = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == value) ?? UpcomingTasksRangeOptions[^1];
        UpdateSubCollections();
        SaveUserSettings();
    }

    public List<UpcomingTasksRangeOption> UpcomingTasksRangeOptions { get; } = new()
    {
        new UpcomingTasksRangeOption { Id = "Tomorrow", Name = "Tomorrow Only", Description = "Tasks due or reminded for tomorrow" },
        new UpcomingTasksRangeOption { Id = "Next3Days", Name = "Next 3 Days", Description = "Tasks due or reminded within the next 3 days" },
        new UpcomingTasksRangeOption { Id = "ThisWeek", Name = "This Week", Description = "Tasks due or reminded by end of this week (Sunday)" },
        new UpcomingTasksRangeOption { Id = "Next7Days", Name = "Next 7 Days", Description = "Tasks due or reminded within the next 7 days" },
        new UpcomingTasksRangeOption { Id = "ThisMonth", Name = "This Month", Description = "Tasks due or reminded by end of this calendar month" },
        new UpcomingTasksRangeOption { Id = "Next30Days", Name = "Next 30 Days", Description = "Tasks due or reminded within the next 30 days" },
        new UpcomingTasksRangeOption { Id = "All", Name = "All Upcoming", Description = "All future scheduled tasks without date limit" }
    };

    [ObservableProperty]
    private UpcomingTasksRangeOption? _selectedUpcomingTasksRangeOption;

    partial void OnSelectedUpcomingTasksRangeOptionChanged(UpcomingTasksRangeOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id) && value.Id != UpcomingTasksRange)
        {
            UpcomingTasksRange = value.Id;
        }
    }

    public string UpcomingTasksRangeLabel => UpcomingTasksRange switch
    {
        "Tomorrow" => LocalizationManager.Instance["Tasks_Upcoming_Tomorrow"],
        "Next3Days" => LocalizationManager.Instance["Tasks_Upcoming_3Days"],
        "ThisWeek" => LocalizationManager.Instance["Tasks_Upcoming_7Days"],
        "Next7Days" => LocalizationManager.Instance["Tasks_Upcoming_7Days"],
        "ThisMonth" => LocalizationManager.Instance["Tasks_Upcoming_Month"],
        "Next30Days" => LocalizationManager.Instance["Tasks_Upcoming_14Days"],
        _ => LocalizationManager.Instance["Tasks_Upcoming_All"]
    };

    public bool IsUpcomingRangeFilterActive => UpcomingTasksRange != "All";

    [ObservableProperty]
    private bool _isUpcomingTasksRangePickerSheetOpen;

    [RelayCommand]
    private void OpenUpcomingTasksRangePicker()
    {
        IsUpcomingTasksRangePickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseUpcomingTasksRangePicker()
    {
        IsUpcomingTasksRangePickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectUpcomingTasksRangeOption(UpcomingTasksRangeOption? option)
    {
        if (option != null)
        {
            SelectedUpcomingTasksRangeOption = option;
            UpcomingTasksRange = option.Id;
            IsUpcomingTasksRangePickerSheetOpen = false;
        }
    }

    [RelayCommand]
    private void SetUpcomingTasksRange(string range)
    {
        if (!string.IsNullOrWhiteSpace(range))
        {
            UpcomingTasksRange = range;
            SelectedUpcomingTasksRangeOption = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == range) ?? UpcomingTasksRangeOptions[^1];
        }
    }

    [ObservableProperty]
    private bool _isNoteComposerExpanded;

    [RelayCommand]
    private void ToggleNoteComposer()
    {
        IsNoteComposerExpanded = !IsNoteComposerExpanded;
    }

    [RelayCommand]
    private void ExpandNoteComposer()
    {
        IsNoteComposerExpanded = true;
    }

    [ObservableProperty]
    private TodoItemViewModel? _selectedDetailTask;

    partial void OnSelectedDetailTaskChanged(TodoItemViewModel? oldValue, TodoItemViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSelectedDetailTaskPropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedDetailTaskPropertyChanged;
        }
    }

    private async void OnSelectedDetailTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (SelectedDetailTask != null && (e.PropertyName == nameof(TodoItemViewModel.Description) || e.PropertyName == nameof(TodoItemViewModel.Title)))
        {
            await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            RequestDebouncedAutoSync();
        }
    }

    [ObservableProperty]
    private bool _isDetailDrawerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotePreviewMode))]
    private bool _isNoteEditMode = true;

    public bool IsNotePreviewMode
    {
        get => !IsNoteEditMode;
        set => IsNoteEditMode = !value;
    }

    [RelayCommand]
    private void OpenDetailDrawer(TodoItemViewModel? item)
    {
        if (item == null) return;
        IsNoteEditMode = true;
        SelectedDetailTask = item;
        IsDetailDrawerOpen = true;
    }

    [RelayCommand]
    private async Task CloseDetailDrawerAsync()
    {
        if (SelectedDetailTask != null)
        {
            await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            RequestDebouncedAutoSync();
        }
        IsDetailDrawerOpen = false;
        SelectedDetailTask = null;
    }

    private void CloseDetailDrawer() => _ = CloseDetailDrawerAsync();

    [RelayCommand]
    private async Task SaveDetailTaskAsync()
    {
        if (SelectedDetailTask == null) return;
        await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
        RequestDebouncedAutoSync();
        UpdateSubCollections();
    }

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newTaskDescription = string.Empty;

    [ObservableProperty]
    private string _newTaskCategoryInput = string.Empty;

    public ObservableCollection<string> NewTaskCategories { get; } = new();

    public bool HasNewTaskCategories => NewTaskCategories.Count > 0;

    [RelayCommand]
    private void AddNewTaskCategory(string? category)
    {
        var tag = string.IsNullOrWhiteSpace(category) ? NewTaskCategoryInput : category;
        if (string.IsNullOrWhiteSpace(tag)) return;

        tag = tag.Trim();
        if (!NewTaskCategories.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            NewTaskCategories.Add(tag);
            NewTaskCategoryInput = string.Empty;
            OnPropertyChanged(nameof(HasNewTaskCategories));
        }
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void RemoveNewTaskCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        var existing = NewTaskCategories.FirstOrDefault(x => x.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            NewTaskCategories.Remove(existing);
            OnPropertyChanged(nameof(HasNewTaskCategories));
        }
    }

    [RelayCommand]
    private void ClearNewTaskCategories()
    {
        NewTaskCategories.Clear();
        NewTaskCategoryInput = string.Empty;
        OnPropertyChanged(nameof(HasNewTaskCategories));
    }

    private bool _isUpdatingCategoryFilters;

    public ObservableCollection<CategoryFilterOption> CategoryFilterOptions { get; } = new();

    public ObservableCollection<string> ExistingCategories { get; } = new();
    public bool HasExistingCategories => ExistingCategories.Count > 0;

    public string CategoryFilterButtonText
    {
        get
        {
            var selected = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
            if (selected.Count == 0 || selected.Contains("All Categories", StringComparer.OrdinalIgnoreCase))
            {
                return "Tags";
            }

            if (selected.Count == 1)
            {
                return selected[0];
            }

            var joined = string.Join(", ", selected);
            return joined.Length > 18 ? $"{selected.Count} Tags" : joined;
        }
    }

    public ObservableCollection<TagItemViewModel> AllTags { get; } = new();

    private readonly List<string> _customEmptyTags = new();
    private readonly Dictionary<string, DateTime> _tagCreatedTimes = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _newGlobalTagInput = string.Empty;

    public void UpdateAvailableCategories()
    {
        var activeCategories = TodoItems
            .SelectMany(x => x.Categories)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var combinedActiveAndCustom = activeCategories
            .Concat(_customEmptyTags.Where(c => !activeCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        DateTime GetLatestTime(string cat)
        {
            var taskLatest = TodoItems
                .Where(x => x.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase))
                .Select(x => x.CreatedAt)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            var tagLatest = _tagCreatedTimes.TryGetValue(cat, out var dt) ? dt : DateTime.MinValue;
            return taskLatest > tagLatest ? taskLatest : tagLatest;
        }

        var sortedCategories = combinedActiveAndCustom
            .OrderByDescending(cat => GetLatestTime(cat))
            .ThenBy(cat => cat)
            .ToList();

        ExistingCategories.Clear();
        foreach (var cat in sortedCategories)
        {
            ExistingCategories.Add(cat);
        }
        OnPropertyChanged(nameof(HasExistingCategories));

        AllTags.Clear();
        foreach (var cat in sortedCategories)
        {
            int count = TodoItems.Count(x => x.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase));
            AllTags.Add(new TagItemViewModel(cat, count, RenameGlobalTagAsync, DeleteGlobalTagAsync));
        }

        var defaults = new List<string> { "All Categories", "Uncategorized" };
        var combinedNames = defaults
            .Concat(sortedCategories.Where(c => !defaults.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        _isUpdatingCategoryFilters = true;
        try
        {
            var existingMap = CategoryFilterOptions.ToDictionary(x => x.Name, x => x.IsSelected, StringComparer.OrdinalIgnoreCase);

            CategoryFilterOptions.Clear();
            foreach (var name in combinedNames)
            {
                bool isSel = existingMap.TryGetValue(name, out var wasSel) ? wasSel : name.Equals("All Categories", StringComparison.OrdinalIgnoreCase);
                var opt = new CategoryFilterOption(name, isSel)
                {
                    OnSelectionChanged = OnCategoryOptionSelectionChanged
                };
                CategoryFilterOptions.Add(opt);
            }

            if (!CategoryFilterOptions.Any(o => o.IsSelected))
            {
                var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                if (allOpt != null) allOpt.IsSelected = true;
            }
        }
        finally
        {
            _isUpdatingCategoryFilters = false;
        }

        OnPropertyChanged(nameof(CategoryFilterButtonText));
    }

    private void OnCategoryOptionSelectionChanged(CategoryFilterOption changedOption)
    {
        if (_isUpdatingCategoryFilters) return;

        _isUpdatingCategoryFilters = true;
        try
        {
            if (changedOption.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase))
            {
                if (changedOption.IsSelected)
                {
                    foreach (var opt in CategoryFilterOptions.Where(o => !o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase)))
                    {
                        opt.IsSelected = false;
                    }
                }
                else
                {
                    if (!CategoryFilterOptions.Any(o => o.IsSelected))
                    {
                        changedOption.IsSelected = true;
                    }
                }
            }
            else
            {
                if (changedOption.IsSelected)
                {
                    var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                    if (allOpt != null) allOpt.IsSelected = false;
                }
                else
                {
                    if (!CategoryFilterOptions.Any(o => o.IsSelected))
                    {
                        var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                        if (allOpt != null) allOpt.IsSelected = true;
                    }
                }
            }
        }
        finally
        {
            _isUpdatingCategoryFilters = false;
        }

        OnPropertyChanged(nameof(CategoryFilterButtonText));
        UpdateSubCollections();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskDueDate))]
    [NotifyPropertyChangedFor(nameof(NewTaskDueDateFormatted))]
    [NotifyPropertyChangedFor(nameof(DueDateHeaderYear))]
    [NotifyPropertyChangedFor(nameof(DueDateHeaderMainText))]
    private DateTime? _newTaskDueDate;

    public DateTime MinDueDate => DateTime.Today;

    public TimeSpan GetSuggestedLaterTodayTime()
    {
        var now = DateTime.Now;
        if (now.Hour < 17)
        {
            return new TimeSpan(17, 0, 0);
        }
        else if (now.Hour < 20)
        {
            return new TimeSpan(20, 0, 0);
        }
        else if (now.Hour < 22)
        {
            return new TimeSpan(22, 0, 0);
        }
        else
        {
            return new TimeSpan(23, 59, 0);
        }
    }

    public string ReminderLaterTodayText
    {
        get
        {
            var time = GetSuggestedLaterTodayTime();
            return $"Later Today ({time:hh\\:mm})";
        }
    }

    public bool IsReminderLaterTodayEnabled
    {
        get
        {
            var time = GetSuggestedLaterTodayTime();
            var preset = DateTime.Today.Add(time);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    public bool IsReminderTomorrowMorningEnabled
    {
        get
        {
            var preset = DateTime.Today.AddDays(1).AddHours(9);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    public bool IsReminderNextWeekEnabled
    {
        get
        {
            var preset = DateTime.Today.AddDays(7).AddHours(9);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    partial void OnNewTaskDueDateChanged(DateTime? value)
    {
        if (value.HasValue && value.Value.Date < DateTime.Today)
        {
            NewTaskDueDate = DateTime.Today;
            StatusMessage = "Due date cannot be set in the past.";
            return;
        }
        ValidateAndResetReminderIfExceedsDueDate();
        OnPropertyChanged(nameof(ReminderLaterTodayText));
        OnPropertyChanged(nameof(IsReminderLaterTodayEnabled));
        OnPropertyChanged(nameof(IsReminderTomorrowMorningEnabled));
        OnPropertyChanged(nameof(IsReminderNextWeekEnabled));
    }

    public bool HasNewTaskDueDate => NewTaskDueDate.HasValue;

    public string NewTaskDueDateFormatted
    {
        get
        {
            if (!NewTaskDueDate.HasValue) return string.Empty;
            var date = NewTaskDueDate.Value.Date;
            var today = DateTime.Today;
            if (date == today) return "Today";
            if (date == today.AddDays(1)) return "Tomorrow";
            return NewTaskDueDate.Value.ToString("MMM d, yyyy");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskReminder))]
    [NotifyPropertyChangedFor(nameof(NewTaskReminderFormatted))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderYear))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private DateTime? _newTaskReminderDate;

    partial void OnNewTaskReminderDateChanged(DateTime? value)
    {
        if (value.HasValue && !NewTaskReminderTime.HasValue)
        {
            NewTaskReminderTime = TimeSpan.Zero;
        }
        ValidateAndResetReminderIfExceedsDueDate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskReminder))]
    [NotifyPropertyChangedFor(nameof(NewTaskReminderFormatted))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private TimeSpan? _newTaskReminderTime;

    partial void OnNewTaskReminderTimeChanged(TimeSpan? value)
    {
        ValidateAndResetReminderIfExceedsDueDate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReminderDateTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsReminderTimeTabSelected))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private int _reminderSelectedTab = 0;

    public bool IsReminderDateTabSelected => ReminderSelectedTab == 0;
    public bool IsReminderTimeTabSelected => ReminderSelectedTab == 1;

    [RelayCommand]
    private void SelectReminderDateTab() => ReminderSelectedTab = 0;

    [RelayCommand]
    private void SelectReminderTimeTab() => ReminderSelectedTab = 1;

    public string ReminderHeaderYear => (NewTaskReminderDate ?? DateTime.Today).ToString("yyyy");

    public string ReminderHeaderMainText
    {
        get
        {
            var dateStr = (NewTaskReminderDate ?? DateTime.Today).ToString("ddd, MMM d");
            if (IsReminderTimeTabSelected && NewTaskReminderTime.HasValue)
            {
                var timeStr = DateTime.Today.Add(NewTaskReminderTime.Value).ToString("HH:mm");
                return $"{dateStr}, {timeStr}";
            }
            return dateStr;
        }
    }

    public string DueDateHeaderYear => (NewTaskDueDate ?? DateTime.Today).ToString("yyyy");
    public string DueDateHeaderMainText => (NewTaskDueDate ?? DateTime.Today).ToString("ddd, MMM d");

    [RelayCommand]
    private void SetReminderPresetMorning()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(9, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetAfternoon()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(13, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetEvening()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(17, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetNight()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(20, 0, 0);
    }

    private bool _isValidatingReminder;

    private void ValidateAndResetReminderIfExceedsDueDate()
    {
        if (_isValidatingReminder) return;
        if (!NewTaskDueDate.HasValue) return;

        var reminderDt = GetCombinedNewTaskReminder();
        if (!reminderDt.HasValue) return;

        DateTime maxReminderAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
            ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
            : NewTaskDueDate.Value;

        if (reminderDt.Value > maxReminderAllowed)
        {
            _isValidatingReminder = true;
            try
            {
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                StatusMessage = "Reminder reset because it exceeded the due date.";
            }
            finally
            {
                _isValidatingReminder = false;
            }
        }
    }

    public bool HasNewTaskReminder => NewTaskReminderDate.HasValue || NewTaskReminderTime.HasValue;

    public string NewTaskReminderFormatted
    {
        get
        {
            var dt = GetCombinedNewTaskReminder();
            if (!dt.HasValue) return string.Empty;
            var date = dt.Value.Date;
            var today = DateTime.Today;
            var timeStr = dt.Value.ToString("HH:mm");
            if (date == today) return $"Today at {timeStr}";
            if (date == today.AddDays(1)) return $"Tomorrow at {timeStr}";
            return $"{dt.Value:MMM d, yyyy} at {timeStr}";
        }
    }

    private DateTime? GetCombinedNewTaskReminder()
    {
        if (!NewTaskReminderDate.HasValue && !NewTaskReminderTime.HasValue)
            return null;

        var baseDate = NewTaskReminderDate?.Date ?? DateTime.Today;
        var time = NewTaskReminderTime ?? TimeSpan.Zero; // Default is 00:00
        return baseDate.Add(time);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCustomRecurrenceVisible))]
    [NotifyPropertyChangedFor(nameof(IsWeeklyDaysPickerVisible))]
    [NotifyPropertyChangedFor(nameof(HasNewTaskRecurrence))]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private string _selectedRecurrenceType = "None";

    public bool IsRepeatEnabled => !string.IsNullOrWhiteSpace(SelectedRecurrenceType) && !SelectedRecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase);

    public List<string> RecurrenceOptions { get; } = new() { "None", "Daily", "Weekdays", "Weekly", "Monthly", "Yearly", "Custom" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private int _customInterval = 1;

    partial void OnCustomIntervalChanged(int value)
    {
        if (value < 1) CustomInterval = 1;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeeklyDaysPickerVisible))]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private string _selectedCustomUnit = "Days";

    public List<string> CustomUnitOptions { get; } = new() { "Days", "Weeks", "Months", "Years" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isMondaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isTuesdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isWednesdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isThursdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isFridaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isSaturdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isSundaySelected;

    public bool IsCustomRecurrenceVisible => IsRepeatEnabled && SelectedRecurrenceType == "Custom";

    public bool IsWeeklyDaysPickerVisible => IsCustomRecurrenceVisible && SelectedCustomUnit == "Weeks";

    public bool HasNewTaskRecurrence => IsRepeatEnabled;

    public string NewTaskRecurrenceFormatted
    {
        get
        {
            if (!IsRepeatEnabled) return string.Empty;
            return RecurrenceHelper.FormatRecurrenceText(
                IsRepeatEnabled,
                SelectedRecurrenceType,
                CustomInterval,
                SelectedCustomUnit,
                GetSelectedWeeklyDaysString());
        }
    }

    private string GetSelectedWeeklyDaysString()
    {
        var days = new List<string>();
        if (IsMondaySelected) days.Add("Monday");
        if (IsTuesdaySelected) days.Add("Tuesday");
        if (IsWednesdaySelected) days.Add("Wednesday");
        if (IsThursdaySelected) days.Add("Thursday");
        if (IsFridaySelected) days.Add("Friday");
        if (IsSaturdaySelected) days.Add("Saturday");
        if (IsSundaySelected) days.Add("Sunday");
        return string.Join(",", days);
    }

    [RelayCommand]
    private void SetRecurrenceType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        SelectedRecurrenceType = type;
        if (type != "Custom")
        {
            CloseMobileRepeatSheet();
        }
    }

    [RelayCommand]
    private void ClearRecurrence()
    {
        SelectedRecurrenceType = "None";
        CustomInterval = 1;
        SelectedCustomUnit = "Days";
        IsMondaySelected = false;
        IsTuesdaySelected = false;
        IsWednesdaySelected = false;
        IsThursdaySelected = false;
        IsFridaySelected = false;
        IsSaturdaySelected = false;
        IsSundaySelected = false;
    }

    [RelayCommand]
    private void OpenMobileTaskComposer()
    {
        IsMobileTaskComposerOpen = true;
    }

    [RelayCommand]
    private void CloseMobileTaskComposer()
    {
        IsMobileTaskComposerOpen = false;
        IsMobileDueDateSheetOpen = false;
        IsMobileReminderSheetOpen = false;
        IsMobileRepeatSheetOpen = false;
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileDueDateSheet()
    {
        IsMobileDueDateSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileDueDateSheet()
    {
        IsMobileDueDateSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileReminderSheet()
    {
        IsMobileReminderSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileReminderSheet()
    {
        IsMobileReminderSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileRepeatSheet()
    {
        IsMobileRepeatSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileRepeatSheet()
    {
        IsMobileRepeatSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileCategorySheet()
    {
        IsMobileCategorySheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileCategorySheet()
    {
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileTagFilterSheet()
    {
        IsMobileTagFilterSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileTagFilterSheet()
    {
        IsMobileTagFilterSheetOpen = false;
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
        foreach (var opt in CategoryFilterOptions)
        {
            opt.IsSelected = (opt == allOpt);
        }
    }

    private bool _isApplyingPreset;

    [ObservableProperty]
    private string _completedDateFilterPreset = "Today";

    [ObservableProperty]
    private DateTime? _completedDateFilterStartDate;

    [ObservableProperty]
    private DateTime? _completedDateFilterEndDate;

    [ObservableProperty]
    private bool _isMobileCompletedDateFilterSheetOpen;

    partial void OnCompletedDateFilterPresetChanged(string value)
    {
        ApplyCompletedDatePreset(value);
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    partial void OnCompletedDateFilterStartDateChanged(DateTime? value)
    {
        if (_isApplyingPreset) return;

        if (value.HasValue && CompletedDateFilterEndDate.HasValue && value.Value.Date > CompletedDateFilterEndDate.Value.Date)
        {
            CompletedDateFilterEndDate = value.Value.Date;
        }

        if (CompletedDateFilterPreset != "Custom" && value.HasValue)
        {
            _completedDateFilterPreset = "Custom";
            OnPropertyChanged(nameof(CompletedDateFilterPreset));
        }
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    partial void OnCompletedDateFilterEndDateChanged(DateTime? value)
    {
        if (_isApplyingPreset) return;

        if (value.HasValue && CompletedDateFilterStartDate.HasValue && value.Value.Date < CompletedDateFilterStartDate.Value.Date)
        {
            CompletedDateFilterStartDate = value.Value.Date;
        }

        if (CompletedDateFilterPreset != "Custom" && value.HasValue)
        {
            _completedDateFilterPreset = "Custom";
            OnPropertyChanged(nameof(CompletedDateFilterPreset));
        }
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    [RelayCommand]
    private void SetCompletedStartDate(string? option)
    {
        var today = DateTime.Today;
        switch (option)
        {
            case "Today":
                CompletedDateFilterStartDate = today;
                break;
            case "7DaysAgo":
                CompletedDateFilterStartDate = today.AddDays(-6);
                break;
            case "30DaysAgo":
                CompletedDateFilterStartDate = today.AddDays(-29);
                break;
            case "FirstOfMonth":
                CompletedDateFilterStartDate = new DateTime(today.Year, today.Month, 1);
                break;
        }
    }

    [RelayCommand]
    private void SetCompletedEndDate(string? option)
    {
        var today = DateTime.Today;
        switch (option)
        {
            case "Today":
                CompletedDateFilterEndDate = today;
                break;
            case "Yesterday":
                CompletedDateFilterEndDate = today.AddDays(-1);
                break;
        }
    }

    [ObservableProperty]
    private string _selectedCompletedDateTab = "Start";

    public bool IsCompletedDateStartTabActive => SelectedCompletedDateTab == "Start";

    [RelayCommand]
    private void SelectCompletedDateTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        SelectedCompletedDateTab = tab;
        OnPropertyChanged(nameof(IsCompletedDateStartTabActive));
    }

    public string CompletedDateHeaderYear => (CompletedDateFilterStartDate ?? DateTime.Today).ToString("yyyy");
    public string CompletedDateHeaderMainText => CompletedDateFilterButtonText;

    public string CompletedDateFilterStartDateFormatted => CompletedDateFilterStartDate.HasValue ? CompletedDateFilterStartDate.Value.ToString("ddd, MMM d") : "Any Date";
    public string CompletedDateFilterEndDateFormatted => CompletedDateFilterEndDate.HasValue ? CompletedDateFilterEndDate.Value.ToString("ddd, MMM d") : "Any Date";

    private void NotifyCompletedDateFilterProperties()
    {
        OnPropertyChanged(nameof(CompletedDateFilterButtonText));
        OnPropertyChanged(nameof(CompletedDateHeaderYear));
        OnPropertyChanged(nameof(CompletedDateHeaderMainText));
        OnPropertyChanged(nameof(CompletedDateFilterStartDateFormatted));
        OnPropertyChanged(nameof(CompletedDateFilterEndDateFormatted));
        OnPropertyChanged(nameof(IsCompletedDateFilterActive));
        OnPropertyChanged(nameof(IsAnyCompletedFilterActive));
        OnPropertyChanged(nameof(CompletedDateFilterStartDateOffset));
        OnPropertyChanged(nameof(CompletedDateFilterEndDateOffset));
    }

    public DateTimeOffset? CompletedDateFilterStartDateOffset
    {
        get => CompletedDateFilterStartDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(CompletedDateFilterStartDate.Value, DateTimeKind.Local)) : null;
        set
        {
            if (value.HasValue)
            {
                CompletedDateFilterStartDate = value.Value.LocalDateTime.Date;
            }
            else
            {
                CompletedDateFilterStartDate = null;
            }
        }
    }

    public DateTimeOffset? CompletedDateFilterEndDateOffset
    {
        get => CompletedDateFilterEndDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(CompletedDateFilterEndDate.Value, DateTimeKind.Local)) : null;
        set
        {
            if (value.HasValue)
            {
                CompletedDateFilterEndDate = value.Value.LocalDateTime.Date;
            }
            else
            {
                CompletedDateFilterEndDate = null;
            }
        }
    }

    private void ApplyCompletedDatePreset(string preset)
    {
        _isApplyingPreset = true;
        try
        {
            var today = DateTime.Today;
            switch (preset)
            {
                case "Today":
                    CompletedDateFilterStartDate = today;
                    CompletedDateFilterEndDate = today;
                    break;
                case "Yesterday":
                    CompletedDateFilterStartDate = today.AddDays(-1);
                    CompletedDateFilterEndDate = today.AddDays(-1);
                    break;
                case "7Days":
                    CompletedDateFilterStartDate = today.AddDays(-6);
                    CompletedDateFilterEndDate = today;
                    break;
                case "30Days":
                    CompletedDateFilterStartDate = today.AddDays(-29);
                    CompletedDateFilterEndDate = today;
                    break;
                case "ThisMonth":
                    CompletedDateFilterStartDate = new DateTime(today.Year, today.Month, 1);
                    CompletedDateFilterEndDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
                    break;
                case "Custom":
                    break;
                case "All":
                default:
                    CompletedDateFilterStartDate = null;
                    CompletedDateFilterEndDate = null;
                    break;
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    public bool IsCompletedDateFilterActive => CompletedDateFilterPreset != "All" || CompletedDateFilterStartDate.HasValue || CompletedDateFilterEndDate.HasValue;

    public bool IsAnyCompletedFilterActive => IsCompletedDateFilterActive || (CategoryFilterOptions != null && CategoryFilterOptions.Any(o => o.IsSelected && !o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase)));

    public string CompletedDateFilterButtonText
    {
        get
        {
            return CompletedDateFilterPreset switch
            {
                "Today" => "Today",
                "Yesterday" => "Yesterday",
                "7Days" => "Last 7 Days",
                "30Days" => "Last 30 Days",
                "ThisMonth" => "This Month",
                "Custom" => GetCustomDateFilterText(),
                _ => "All Time"
            };
        }
    }

    private string GetCustomDateFilterText()
    {
        if (CompletedDateFilterStartDate.HasValue && CompletedDateFilterEndDate.HasValue)
        {
            if (CompletedDateFilterStartDate.Value.Date == CompletedDateFilterEndDate.Value.Date)
            {
                return $"{CompletedDateFilterStartDate.Value:MMM d, yyyy}";
            }
            return $"{CompletedDateFilterStartDate.Value:MMM d} - {CompletedDateFilterEndDate.Value:MMM d}";
        }
        if (CompletedDateFilterStartDate.HasValue)
        {
            return $"From {CompletedDateFilterStartDate.Value:MMM d}";
        }
        if (CompletedDateFilterEndDate.HasValue)
        {
            return $"Until {CompletedDateFilterEndDate.Value:MMM d}";
        }
        return "Custom Range";
    }

    [RelayCommand]
    private void SetCompletedDateFilterPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return;
        CompletedDateFilterPreset = preset;
    }

    [RelayCommand]
    private void ClearCompletedDateFilter()
    {
        CompletedDateFilterPreset = "All";
    }

    [RelayCommand]
    private void ClearAllCompletedFilters()
    {
        ClearCompletedDateFilter();
        ClearTagFilter();
    }

    [RelayCommand]
    private void OpenMobileCompletedDateFilterSheet()
    {
        IsMobileCompletedDateFilterSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileCompletedDateFilterSheet()
    {
        IsMobileCompletedDateFilterSheetOpen = false;
    }

    private bool PassesCompletedDateFilter(TodoItemViewModel item)
    {
        if (CompletedDateFilterPreset == "All" && !CompletedDateFilterStartDate.HasValue && !CompletedDateFilterEndDate.HasValue)
        {
            return true;
        }

        if (!item.CompletedAt.HasValue)
        {
            return false;
        }

        var localCompletedDate = item.CompletedAt.Value.ToLocalTime().Date;

        if (CompletedDateFilterStartDate.HasValue && localCompletedDate < CompletedDateFilterStartDate.Value.Date)
        {
            return false;
        }

        if (CompletedDateFilterEndDate.HasValue && localCompletedDate > CompletedDateFilterEndDate.Value.Date)
        {
            return false;
        }

        return true;
    }

    [ObservableProperty]
    private bool _isCompact = OperatingSystem.IsAndroid();

    partial void OnIsCompactChanged(bool value)
    {
        if (GoalsVM != null)
        {
            GoalsVM.IsCompact = value;
        }
    }

    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    private bool _isMobileTaskComposerOpen;

    [ObservableProperty]
    private bool _isMobileDueDateSheetOpen;

    [ObservableProperty]
    private bool _isMobileReminderSheetOpen;

    [ObservableProperty]
    private bool _isMobileRepeatSheetOpen;

    [ObservableProperty]
    private bool _isMobileCategorySheetOpen;

    [ObservableProperty]
    private bool _isMobileTagFilterSheetOpen;

    [ObservableProperty]
    private bool _isMobileMoreSheetOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    [NotifyPropertyChangedFor(nameof(ScrimOpacity))]
    private bool _isNavExpanded = false;

    public double SidebarWidth => IsNavExpanded ? 240 : 64;
    public double ScrimOpacity => IsNavExpanded ? 1.0 : 0.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageStatusText))]
    [NotifyPropertyChangedFor(nameof(IsSyncingOrSigningIn))]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncEndpointUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageStatusText))]
    private bool _isGoogleSignedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageStatusText))]
    [NotifyPropertyChangedFor(nameof(IsSyncingOrSigningIn))]
    private bool _isGoogleSigningIn;

    public bool IsSyncingOrSigningIn => IsSyncing || IsGoogleSigningIn;

    public string StorageStatusText
    {
        get
        {
            if (IsGoogleSigningIn) return LocalizationManager.Instance["Main_SigningInGoogle"] ?? "Signing in with Google Account...";
            if (IsSyncing) return "Syncing with cloud…";
            if (IsGoogleSignedIn) return "Synced with Google Drive";
            return "Saved on device";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedFormatted))]
    private DateTime? _lastUpdatedAt;

    public string LastUpdatedFormatted
    {
        get
        {
            if (!LastUpdatedAt.HasValue) return "Saved on device";
            var now = DateTime.Now;
            var diff = now - LastUpdatedAt.Value;
            if (diff.TotalSeconds < 60) return "Updated just now";
            if (diff.TotalMinutes < 60) return $"Updated {Math.Max(1, (int)diff.TotalMinutes)}m ago";
            if (LastUpdatedAt.Value.Date == now.Date) return $"Updated at {LastUpdatedAt.Value:h:mm tt}";
            return $"Updated {LastUpdatedAt.Value:MMM d, h:mm tt}";
        }
    }

    [ObservableProperty]
    private string _googleUserEmail = string.Empty;

    [ObservableProperty]
    private string _googleUserName = string.Empty;

    [ObservableProperty]
    private string _googleClientId = string.Empty;

    partial void OnGoogleClientIdChanged(string value)
    {
        _syncService.GoogleClientId = value;
    }

    [ObservableProperty]
    private string _firebaseApiKey = string.Empty;

    partial void OnFirebaseApiKeyChanged(string value)
    {
        _syncService.FirebaseApiKey = value;
    }

    [ObservableProperty]
    private string _firebaseProjectId = string.Empty;

    partial void OnFirebaseProjectIdChanged(string value)
    {
        _syncService.FirebaseProjectId = value;
    }

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    [ObservableProperty]
    private bool _hasLoadedCompletedView;

    [ObservableProperty]
    private bool _hasLoadedRecurringView;

    [ObservableProperty]
    private bool _hasLoadedCalendarView;

    [ObservableProperty]
    private bool _hasLoadedSearchView;

    [ObservableProperty]
    private bool _hasLoadedTagsView;

    [ObservableProperty]
    private bool _hasLoadedSettingsView;

    [ObservableProperty]
    private bool _hasLoadedGoalsView;

    public bool IsTasksView => SelectedNavIndex == 0;
    public bool IsCompletedView => SelectedNavIndex == 1;
    public bool IsRecurringView => SelectedNavIndex == 2;
    public bool IsCalendarView => SelectedNavIndex == 3;
    public bool IsSearchView => SelectedNavIndex == 4;
    public bool IsTagsView => SelectedNavIndex == 5;
    public bool IsGoalsView => SelectedNavIndex == 6;
    public bool IsSettingsView => SelectedNavIndex == 7;

    public bool IsMoreActive => IsSearchView || IsTagsView || IsGoalsView || IsSettingsView;

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (value == 1 && !HasLoadedCompletedView) HasLoadedCompletedView = true;
        if (value == 2 && !HasLoadedRecurringView) HasLoadedRecurringView = true;
        if (value == 3 && !HasLoadedCalendarView) HasLoadedCalendarView = true;
        if (value == 4 && !HasLoadedSearchView) HasLoadedSearchView = true;
        if (value == 5 && !HasLoadedTagsView) HasLoadedTagsView = true;
        if (value == 6 && !HasLoadedGoalsView) HasLoadedGoalsView = true;
        if (value == 7 && !HasLoadedSettingsView) HasLoadedSettingsView = true;

        OnPropertyChanged(nameof(IsTasksView));
        OnPropertyChanged(nameof(IsSearchView));
        OnPropertyChanged(nameof(IsCompletedView));
        OnPropertyChanged(nameof(IsRecurringView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsTagsView));
        OnPropertyChanged(nameof(IsGoalsView));
        OnPropertyChanged(nameof(IsSettingsView));
        OnPropertyChanged(nameof(IsMoreActive));
    }

    [ObservableProperty]
    private bool _isReviewPromptVisible;

    [ObservableProperty]
    private bool _isTaskWizardVisible;

    public ObservableCollection<TodoItemViewModel> TodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> UpcomingTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> TodayTodoItems => StandardTodoItems;

    public ObservableCollection<TodoItemViewModel> StandardTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> CompletedTodayTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> CompletedHistoryTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> AllRecurringTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> SearchResultsTodoItems { get; } = new();

    public bool HasSearchResults => SearchResultsTodoItems.Count > 0;

    [ObservableProperty]
    private string _searchStatusFilter = "All";

    public bool IsSearchFilterAll
    {
        get => SearchStatusFilter == "All";
        set { if (value) SearchStatusFilter = "All"; }
    }

    public bool IsSearchFilterActive
    {
        get => SearchStatusFilter == "Active";
        set { if (value) SearchStatusFilter = "Active"; }
    }

    public bool IsSearchFilterCompleted
    {
        get => SearchStatusFilter == "Completed";
        set { if (value) SearchStatusFilter = "Completed"; }
    }

    public bool IsSearchFilterWithNotes
    {
        get => SearchStatusFilter == "WithNotes";
        set { if (value) SearchStatusFilter = "WithNotes"; }
    }

    partial void OnSearchStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearchFilterAll));
        OnPropertyChanged(nameof(IsSearchFilterActive));
        OnPropertyChanged(nameof(IsSearchFilterCompleted));
        OnPropertyChanged(nameof(IsSearchFilterWithNotes));
        UpdateSearchResults();
    }

    [RelayCommand]
    private void ClearSearchQuery()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void ExecuteSearch()
    {
        UpdateSearchResults();
    }

    public bool HasUpcomingTodoItems => UpcomingTodoItems.Count > 0;

    public bool HasTodayTodoItems => StandardTodoItems.Count > 0;

    public bool HasStandardTodoItems => StandardTodoItems.Count > 0;

    public bool HasCompletedTodayTodoItems => CompletedTodayTodoItems.Count > 0;

    public bool HasCompletedHistoryTodoItems => CompletedHistoryTodoItems.Count > 0;

    public bool HasAllRecurringTodoItems => AllRecurringTodoItems.Count > 0;

    [ObservableProperty]
    private bool _isUpcomingTasksExpanded = true;

    [ObservableProperty]
    private bool _isTodayTasksExpanded = true;

    [ObservableProperty]
    private bool _isCompletedTodayExpanded = true;

    [RelayCommand]
    private void ToggleUpcomingTasksExpanded() => IsUpcomingTasksExpanded = !IsUpcomingTasksExpanded;

    [RelayCommand]
    private void ToggleTodayTasksExpanded() => IsTodayTasksExpanded = !IsTodayTasksExpanded;

    [RelayCommand]
    private void ToggleCompletedTodayExpanded() => IsCompletedTodayExpanded = !IsCompletedTodayExpanded;

    [RelayCommand]
    private void NavigateToRecurringView() => SelectedNavIndex = 2;

    [RelayCommand]
    private void NavigateToCalendarView() => SelectedNavIndex = 3;

    private bool PassesSearchAndCategoryFilter(TodoItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            bool matchesTitle = item.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool matchesDesc = item.HasDescription && item.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool matchesTag = item.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (!matchesTitle && !matchesDesc && !matchesTag) return false;
        }

        return PassesCategoryFilter(item);
    }

    private bool PassesCategoryFilter(TodoItemViewModel item)
    {
        var selectedOptions = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        if (selectedOptions.Count == 0 || selectedOptions.Contains("All Categories", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        bool allowsUncategorized = selectedOptions.Contains("Uncategorized", StringComparer.OrdinalIgnoreCase);
        var namedCategories = selectedOptions.Where(s => !s.Equals("All Categories", StringComparison.OrdinalIgnoreCase) && !s.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)).ToList();

        if (allowsUncategorized && !item.HasCategories)
        {
            return true;
        }

        if (item.Categories.Any(c => namedCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private void UpdateSubCollections()
    {
        var today = DateTime.Today;
        var filteredItems = TodoItems.Where(PassesSearchAndCategoryFilter).ToList();
        var activeItems = filteredItems.Where(x => !x.IsCompleted).ToList();
        var completedItems = filteredItems.Where(x => x.IsCompleted).ToList();

        // Today tasks: active tasks with DueDate <= today OR ReminderAt <= today OR without any dates
        // Prioritize tasks that have a due date first (earliest due date first), then reminder, then undated
        var freshStandard = activeItems.Where(x =>
            (x.DueDate.HasValue && x.DueDate.Value.Date <= today) ||
            (x.ReminderAt.HasValue && x.ReminderAt.Value.Date <= today) ||
            (!x.DueDate.HasValue && !x.ReminderAt.HasValue))
            .OrderBy(x => !x.DueDate.HasValue)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => !x.ReminderAt.HasValue)
            .ThenBy(x => x.ReminderAt)
            .ToList();

        // Upcoming tasks: active tasks (both one-off and recurring) due or reminded strictly in the future, filtered by range preset and sorted by nearest due date
        var allUpcoming = activeItems
            .Where(x => !freshStandard.Contains(x))
            .Where(x => PassesUpcomingRangeFilter(x, today, UpcomingTasksRange))
            .OrderBy(x => x.DueDate ?? x.ReminderAt ?? DateTime.MaxValue)
            .ToList();

        // Cap upcoming display to 10 items for All, or up to 50 for specific timeframes
        var freshUpcoming = (UpcomingTasksRange == "All" ? allUpcoming.Take(10) : allUpcoming.Take(50))
            .ToList();

        var freshCompletedToday = completedItems
            .Where(x => !x.CompletedAt.HasValue || x.CompletedAt.Value.ToLocalTime().Date == today)
            .OrderByDescending(x => x.CompletedAt ?? DateTime.MinValue)
            .ToList();
        var freshCompletedHistory = completedItems
            .Where(PassesCompletedDateFilter)
            .OrderByDescending(x => x.CompletedAt ?? DateTime.MinValue)
            .ToList();

        // All recurring tasks
        var freshAllRecurring = filteredItems.Where(x => x.IsRecurring).ToList();

        SyncCollection(UpcomingTodoItems, freshUpcoming);
        SyncCollection(StandardTodoItems, freshStandard);
        SyncCollection(CompletedTodayTodoItems, freshCompletedToday);
        SyncCollection(CompletedHistoryTodoItems, freshCompletedHistory);
        SyncCollection(AllRecurringTodoItems, freshAllRecurring);

        OnPropertyChanged(nameof(HasUpcomingTodoItems));
        OnPropertyChanged(nameof(HasTodayTodoItems));
        OnPropertyChanged(nameof(HasStandardTodoItems));
        OnPropertyChanged(nameof(HasCompletedTodayTodoItems));
        OnPropertyChanged(nameof(HasCompletedHistoryTodoItems));
        OnPropertyChanged(nameof(HasAllRecurringTodoItems));
        OnPropertyChanged(nameof(IsCompletedTodaySectionVisible));
        OnPropertyChanged(nameof(IsUpcomingSectionVisible));

        GenerateCalendarGrid();
        UpdateSearchResults();
    }

    private void UpdateSearchResults()
    {
        var q = SearchQuery?.Trim() ?? string.Empty;
        var filtered = TodoItems.Where(PassesCategoryFilter).ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(x =>
                x.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (x.HasDescription && x.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                x.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (SearchStatusFilter == "Active")
        {
            filtered = filtered.Where(x => !x.IsCompleted).ToList();
        }
        else if (SearchStatusFilter == "Completed")
        {
            filtered = filtered.Where(x => x.IsCompleted).ToList();
        }
        else if (SearchStatusFilter == "WithNotes")
        {
            filtered = filtered.Where(x => x.HasDescription).ToList();
        }

        SyncCollection(SearchResultsTodoItems, filtered);
        OnPropertyChanged(nameof(HasSearchResults));
    }

    #region Calendar View Logic

    private bool _isUpdatingCalendarPickers;

    [ObservableProperty]
    private bool _isSingleColumnCalendar;

    [RelayCommand]
    private void SetCalendarLayoutMode(string? mode)
    {
        IsSingleColumnCalendar = string.Equals(mode, "Schedule", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mode, "SingleColumn", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mode, "Portrait", StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private DateTime _currentCalendarDate = DateTime.Today;

    [ObservableProperty]
    private string _currentMonthYearText = DateTime.Today.ToString("MMMM yyyy");

    [ObservableProperty]
    private int _selectedMonthIndex = DateTime.Today.Month - 1;

    partial void OnSelectedMonthIndexChanged(int value)
    {
        if (_isUpdatingCalendarPickers) return;
        if (value < 0 || value > 11) return;
        int targetMonth = value + 1;
        int targetYear = SelectedYear > 0 ? SelectedYear : CurrentCalendarDate.Year;
        int maxDays = DateTime.DaysInMonth(targetYear, targetMonth);
        int targetDay = Math.Min(CurrentCalendarDate.Day, maxDays);

        CurrentCalendarDate = new DateTime(targetYear, targetMonth, targetDay);
        GenerateCalendarGrid();
    }

    [ObservableProperty]
    private int _selectedYear = DateTime.Today.Year;

    partial void OnSelectedYearChanged(int value)
    {
        if (_isUpdatingCalendarPickers) return;
        if (value < 2000 || value > 2100) return;
        int targetMonth = SelectedMonthIndex >= 0 ? SelectedMonthIndex + 1 : CurrentCalendarDate.Month;
        int maxDays = DateTime.DaysInMonth(value, targetMonth);
        int targetDay = Math.Min(CurrentCalendarDate.Day, maxDays);

        CurrentCalendarDate = new DateTime(value, targetMonth, targetDay);
        GenerateCalendarGrid();
    }

    public List<string> MonthOptions { get; } = new()
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public List<int> YearOptions { get; } = Enumerable.Range(DateTime.Today.Year - 10, 21).ToList();

    #region Recurrence & Task Filters for Calendar View

    [ObservableProperty]
    private bool _isStandardFilterEnabled = true;

    partial void OnIsStandardFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isDailyFilterEnabled = true;

    partial void OnIsDailyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isWeekdaysFilterEnabled = true;

    partial void OnIsWeekdaysFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isWeeklyFilterEnabled = true;

    partial void OnIsWeeklyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isMonthlyFilterEnabled = true;

    partial void OnIsMonthlyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isYearlyFilterEnabled = true;

    partial void OnIsYearlyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isCompletedFilterEnabled = true;

    partial void OnIsCompletedFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    private IEnumerable<TodoItem> GetFilteredTodoModels()
    {
        var allModels = TodoItems.Where(PassesSearchAndCategoryFilter).Select(x => x.Model).ToList();
        var recurringTitleToTypeMap = allModels
            .Where(t => t.IsRecurring && !string.IsNullOrWhiteSpace(t.RecurrenceType) && !t.RecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.Title.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().RecurrenceType);

        return allModels.Where(item =>
        {
            if (item.IsCompleted && !IsCompletedFilterEnabled)
            {
                return false;
            }

            string recType = item.RecurrenceType ?? "None";
            if (recType.Equals("None", StringComparison.OrdinalIgnoreCase) && item.IsCompleted)
            {
                var titleKey = item.Title.Trim().ToLowerInvariant();
                if (recurringTitleToTypeMap.TryGetValue(titleKey, out var parentRecType))
                {
                    recType = parentRecType;
                }
            }

            string recurrenceType = recType.ToLowerInvariant();
            if (recurrenceType.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return IsStandardFilterEnabled;
            }

            return recurrenceType switch
            {
                "daily" => IsDailyFilterEnabled,
                "weekdays" => IsWeekdaysFilterEnabled,
                "weekly" => IsWeeklyFilterEnabled,
                "monthly" => IsMonthlyFilterEnabled,
                "yearly" => IsYearlyFilterEnabled,
                "custom" => IsDailyFilterEnabled || IsWeeklyFilterEnabled || IsMonthlyFilterEnabled,
                _ => IsStandardFilterEnabled
            };
        });
    }

    #endregion

    private readonly Dictionary<string, string> _dateNotes = new();
    private bool _isSelectingDay;
    private DispatcherTimer? _dateNoteSaveTimer;

    [ObservableProperty]
    private string _selectedDayNoteText = string.Empty;

    partial void OnSelectedDayNoteTextChanged(string value)
    {
        if (_selectedDay == null || _isSelectingDay) return;

        // 1. Instantly update in-memory SelectedDay NoteText & badge
        var dateKey = _selectedDay.Date.ToString("yyyy-MM-dd");
        var trimmedText = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            _dateNotes.Remove(dateKey);
            _selectedDay.NoteText = string.Empty;
        }
        else
        {
            _dateNotes[dateKey] = trimmedText;
            _selectedDay.NoteText = trimmedText;
        }
        _selectedDay.RefreshComputedProperties();

        // 2. Debounce async SQLite persistence (500ms delay after last keystroke)
        if (_dateNoteSaveTimer == null)
        {
            _dateNoteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _dateNoteSaveTimer.Tick += async (_, _) =>
            {
                _dateNoteSaveTimer?.Stop();
                if (_selectedDay != null)
                {
                    var noteToSave = SelectedDayNoteText?.Trim() ?? string.Empty;
                    await _todoService.SaveDateNoteAsync(_selectedDay.Date, noteToSave);
                    RequestDebouncedAutoSync();
                }
            };
        }

        _dateNoteSaveTimer.Stop();
        _dateNoteSaveTimer.Start();
    }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = new();

    public ObservableCollection<TodoItemViewModel> SelectedDateTasks { get; } = new();

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private CalendarDayViewModel? _selectedDay;

    [ObservableProperty]
    private string _selectedDateTitle = "Tasks for Selected Date";

    public bool HasSelectedDateTasks => SelectedDateTasks.Count > 0;

    [RelayCommand]
    private void NextMonth()
    {
        CurrentCalendarDate = CurrentCalendarDate.AddMonths(1);
        GenerateCalendarGrid();
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        CurrentCalendarDate = CurrentCalendarDate.AddMonths(-1);
        GenerateCalendarGrid();
    }

    [RelayCommand]
    private void JumpToToday()
    {
        CurrentCalendarDate = DateTime.Today;
        GenerateCalendarGrid();
        var todayVm = CalendarDays.FirstOrDefault(x => x.Date.Date == DateTime.Today);
        if (todayVm != null)
        {
            SelectDay(todayVm);
        }
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? day)
    {
        if (day == null) return;

        foreach (var d in CalendarDays)
        {
            d.IsSelected = false;
        }

        _isSelectingDay = true;
        day.IsSelected = true;
        SelectedDay = day;
        SelectedDayNoteText = day.NoteText;
        _isSelectingDay = false;

        var tasksForDate = RecurrenceEvaluator.GetTasksForDate(day.Date, GetFilteredTodoModels()).ToList();
        var freshSelectedTasks = TodoItems
            .Where(x => PassesCategoryFilter(x) && tasksForDate.Any(t => t.Id == x.Id))
            .Select(x => new TodoItemViewModel(x.Model) { ContextDate = day.Date })
            .ToList();

        SyncCollection(SelectedDateTasks, freshSelectedTasks);
        SelectedDateTitle = $"Tasks for {day.Date:MMM d, yyyy}";
        IsSidebarOpen = true;

        OnPropertyChanged(nameof(HasSelectedDateTasks));
    }

    [RelayCommand]
    private async Task SaveSelectedDayNoteAsync()
    {
        if (SelectedDay == null) return;

        var dateKey = SelectedDay.Date.ToString("yyyy-MM-dd");
        var text = SelectedDayNoteText?.Trim() ?? string.Empty;

        await _todoService.SaveDateNoteAsync(SelectedDay.Date, text);

        if (string.IsNullOrWhiteSpace(text))
        {
            _dateNotes.Remove(dateKey);
            SelectedDay.NoteText = string.Empty;
        }
        else
        {
            _dateNotes[dateKey] = text;
            SelectedDay.NoteText = text;
        }

        SelectedDay.RefreshComputedProperties();
        GenerateCalendarGrid();
        StatusMessage = $"Note saved for {SelectedDay.Date:MMM d}";
        RequestDebouncedAutoSync();
    }

    [RelayCommand]
    private async Task DeleteSelectedDayNoteAsync()
    {
        if (SelectedDay == null) return;

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Note_Title"];
        var msg = lm["Dialog_Delete_Note_Msg"];
        var itemName = SelectedDay.Date.ToString("D");
        var itemDetails = !string.IsNullOrWhiteSpace(SelectedDayNoteText) ? SelectedDayNoteText : string.Empty;

        var confirmed = await RequestDeleteConfirmationAsync(title, msg, itemName, itemDetails);
        if (!confirmed) return;

        var dateKey = SelectedDay.Date.ToString("yyyy-MM-dd");
        await _todoService.DeleteDateNoteAsync(SelectedDay.Date);

        _dateNotes.Remove(dateKey);
        SelectedDayNoteText = string.Empty;
        SelectedDay.NoteText = string.Empty;
        SelectedDay.RefreshComputedProperties();
        GenerateCalendarGrid();
        StatusMessage = $"Note deleted for {SelectedDay.Date:MMM d}";
        RequestDebouncedAutoSync();
    }

    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

    private int _calendarGridVersion = 0;

    public void GenerateCalendarGrid()
    {
        CurrentMonthYearText = CurrentCalendarDate.ToString("MMMM yyyy");

        _isUpdatingCalendarPickers = true;
        try
        {
            SelectedMonthIndex = CurrentCalendarDate.Month - 1;
            SelectedYear = CurrentCalendarDate.Year;
        }
        finally
        {
            _isUpdatingCalendarPickers = false;
        }

        var currentVersion = ++_calendarGridVersion;
        var currentCalendarDate = CurrentCalendarDate;
        var filteredModels = GetFilteredTodoModels().ToList();
        var vmDict = TodoItems.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var notesSnapshot = new Dictionary<string, string>(_dateNotes);
        var selectedDate = SelectedDay?.Date;

        Task.Run(() =>
        {
            var firstDayOfMonth = new DateTime(currentCalendarDate.Year, currentCalendarDate.Month, 1);
            int offsetDays = (int)firstDayOfMonth.DayOfWeek;
            var gridStartDate = firstDayOfMonth.AddDays(-offsetDays);
            var today = DateTime.Today;

            var freshDays = new List<CalendarDayViewModel>(42);
            for (int i = 0; i < 42; i++)
            {
                var dayDate = gridStartDate.AddDays(i);
                bool isCurrentMonth = dayDate.Month == currentCalendarDate.Month;
                bool isToday = dayDate.Date == today;

                var dayVm = new CalendarDayViewModel(dayDate, isCurrentMonth, isToday);
                if (notesSnapshot.TryGetValue(dayDate.ToString("yyyy-MM-dd"), out var noteText))
                {
                    dayVm.NoteText = noteText;
                }

                var matchingModels = RecurrenceEvaluator.GetTasksForDate(dayDate, filteredModels);
                foreach (var m in matchingModels)
                {
                    if (vmDict.TryGetValue(m.Id, out var vm))
                    {
                        var contextualVm = new TodoItemViewModel(m)
                        {
                            ContextDate = dayDate
                        };
                        dayVm.DayTasks.Add(contextualVm);
                    }
                }
                dayVm.RefreshComputedProperties();

                if (selectedDate.HasValue && dayVm.Date.Date == selectedDate.Value.Date)
                {
                    dayVm.IsSelected = true;
                }

                freshDays.Add(dayVm);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (currentVersion != _calendarGridVersion) return;

                if (CalendarDays.Count == 42)
                {
                    for (int i = 0; i < 42; i++)
                    {
                        var target = CalendarDays[i];
                        var source = freshDays[i];

                        target.Date = source.Date;
                        target.IsCurrentMonth = source.IsCurrentMonth;
                        target.IsToday = source.IsToday;
                        target.IsSelected = source.IsSelected;
                        target.NoteText = source.NoteText;
                        target.DayTasks.Clear();
                        foreach (var task in source.DayTasks)
                        {
                            target.DayTasks.Add(task);
                        }
                        target.RefreshComputedProperties();
                    }
                }
                else
                {
                    CalendarDays.Clear();
                    foreach (var d in freshDays)
                    {
                        CalendarDays.Add(d);
                    }
                }

                if (SelectedDay != null)
                {
                    var updatedSelectedDay = CalendarDays.FirstOrDefault(x => x.Date.Date == SelectedDay.Date.Date);
                    if (updatedSelectedDay != null)
                    {
                        var tasksForDate = RecurrenceEvaluator.GetTasksForDate(updatedSelectedDay.Date, filteredModels);
                        var freshSelectedTasks = tasksForDate
                            .Select(m => new TodoItemViewModel(m) { ContextDate = updatedSelectedDay.Date })
                            .ToList();
                        SyncCollection(SelectedDateTasks, freshSelectedTasks);
                        OnPropertyChanged(nameof(HasSelectedDateTasks));
                    }
                }
            });
        });
    }

    #endregion

    private static void SyncCollection(ObservableCollection<TodoItemViewModel> collection, List<TodoItemViewModel> freshItems)
    {
        var freshSet = new HashSet<Guid>(freshItems.Select(x => x.Id));
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!freshSet.Contains(collection[i].Id))
            {
                collection.RemoveAt(i);
            }
        }
        for (int i = 0; i < freshItems.Count; i++)
        {
            var item = freshItems[i];
            int existingIndex = -1;
            for (int j = 0; j < collection.Count; j++)
            {
                if (collection[j].Id == item.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                collection.Insert(i, item);
            }
            else
            {
                collection[existingIndex].ContextDate = item.ContextDate;
                if (existingIndex != i)
                {
                    collection.Move(existingIndex, i);
                }
            }
        }
    }

    public TaskConflictViewModel TaskConflictVm { get; }

    public int UnresolvedConflictCount => _syncService.UnresolvedConflictCount;
    public bool HasUnresolvedConflicts => UnresolvedConflictCount > 0;
    public string UnresolvedConflictNotificationText => $"{UnresolvedConflictCount} task update(s) require your review.";

    public MainViewModel() : this(
        App.Services?.GetService<ITodoService>() ?? new SQLiteTodoService(),
        App.Services?.GetService<IThemeService>() ?? new ThemeService(),
        App.Services?.GetService<ISyncService>() ?? new GoogleDriveSyncService(App.Services?.GetService<ITodoService>() ?? new SQLiteTodoService()),
        App.Services?.GetService<IExportService>() ?? new ExcelExportService(),
        App.Services?.GetService<IGoalService>() ?? new SQLiteGoalService(),
        App.Services?.GetService<IStartupService>() ?? new WindowsStartupService(),
        App.Services?.GetService<IAiGoalService>() ?? new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
        App.Services?.GetService<INotificationService>() ?? new WindowsNotificationService(),
        App.Services?.GetService<IAudioService>() ?? new Wadd.Services.AudioService())
    {
    }

    public MainViewModel(ITodoService todoService, IThemeService themeService, ISyncService syncService, IExportService exportService, IGoalService goalService, IStartupService startupService, IAiGoalService aiGoalService, INotificationService notificationService, IAudioService? audioService = null)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _goalService = goalService ?? throw new ArgumentNullException(nameof(goalService));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _aiGoalService = aiGoalService ?? throw new ArgumentNullException(nameof(aiGoalService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _audioService = audioService ?? App.Services?.GetService<IAudioService>() ?? new Wadd.Services.AudioService();
        _goalsVM = new GoalsViewModel(_goalService, _aiGoalService, _audioService);
        _goalsVM.IsCompact = IsCompact;
        _goalsVM.StatusNotificationRequested = (msg, type) => ShowStatusBubble(msg, type);
        _goalsVM.DataMutated += () => RequestDebouncedAutoSync();
        _goalsVM.ConfirmDeleteRequested = (title, msg, itemName, details) => RequestDeleteConfirmationAsync(title, msg, itemName, details);
        _goalsVM.GetAvailableTagsFunc = () => ExistingCategories;
        WindowsNotificationService.NotificationTriggered += (title, message) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShowStatusBubble(title, NotificationBubbleType.Info);
            });
        };
        _selectedTasksLayoutOption = TasksLayoutOptions[0];
        _selectedLanguageOption = LanguageOptions[0];
        LocalizationManager.Instance.CultureChanged += () =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateThemeLabel();
                CheckAndUpdateCurrentDate();
                RefreshLocalizedOptionLists();
            });
        };
        LoadUserSettings();
        RefreshLocalizedOptionLists();
        UpdateThemeLabel();
        StartReminderChecker();
        ApplyCompletedDatePreset("Today");

        IConflictRepository conflictRepo;
        if (_todoService is SQLiteTodoService sqliteSvc)
        {
            conflictRepo = new SQLiteConflictRepository(sqliteSvc.DatabaseConnection);
        }
        else
        {
            conflictRepo = new SQLiteConflictRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
        }

        TaskConflictVm = new TaskConflictViewModel(conflictRepo, _todoService, _syncService);

        _syncService.ConflictCountChanged += (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(UnresolvedConflictCount));
                OnPropertyChanged(nameof(HasUnresolvedConflicts));
                OnPropertyChanged(nameof(UnresolvedConflictNotificationText));
            });
        };

        _syncService.AuthStateChanged += (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateGoogleAuthState();
            });
        };

        _currentThemeMode = _themeService.CurrentTheme;
        UpdateThemeLabel();

        if (_syncService is GoogleDriveSyncService driveSync)
        {
            SyncEndpointUrl = driveSync.WebAppUrl;
        }

        _themeService.ThemeChanged += (_, mode) =>
        {
            CurrentThemeMode = mode;
            UpdateThemeLabel();
        };

        UpdateGoogleAuthState();
        CheckAndUpdateCurrentDate();
        _ = LoadTodoItemsAsync();

        WaddDatabaseNotifier.DataChanged += async (_, _) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                CheckAndUpdateCurrentDate();
                await LoadTodoItemsAsync();
                RequestDebouncedAutoSync();
            });
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            CheckAndUpdateCurrentDate();
            OnPropertyChanged(nameof(LastUpdatedFormatted));
            OnPropertyChanged(nameof(ReminderLaterTodayText));
            OnPropertyChanged(nameof(IsReminderLaterTodayEnabled));
            OnPropertyChanged(nameof(IsReminderTomorrowMorningEnabled));
            OnPropertyChanged(nameof(IsReminderNextWeekEnabled));

            _periodicSyncTicks++;
            if (_periodicSyncTicks >= 20) // Every 5 minutes (20 * 15s)
            {
                _periodicSyncTicks = 0;
                if (IsGoogleSignedIn && !IsSyncing)
                {
                    _ = TriggerAutoSyncAsync();
                }
            }
        };
        timer.Start();

        if (IsGoogleSignedIn)
        {
            ScheduleStartupAutoSync();
        }
    }

    public void ScheduleStartupAutoSync(int delayMs = 1800)
    {
        if (!IsGoogleSignedIn) return;
        _ = Task.Run(async () =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
            if (IsGoogleSignedIn && !IsSyncing)
            {
                AppLogger.LogInfo("Sync", "Startup auto-sync triggered on app launch.");
                await TriggerAutoSyncAsync();
            }
        });
    }

    private DateTime _lastRecordedDate = DateTime.Today;

    public void CheckAndUpdateCurrentDate()
    {
        var today = DateTime.Today;
        var culture = LocalizationManager.Instance.CurrentCulture;
        CurrentDateFormatted = DateTime.Now.ToString("dddd, MMMM d", culture).ToUpper(culture);
        CurrentDateFull = DateTime.Now.ToString("dddd, MMMM d, yyyy", culture);
        OnPropertyChanged(nameof(MinDueDate));
        OnPropertyChanged(nameof(DueDateHeaderYear));
        OnPropertyChanged(nameof(DueDateHeaderMainText));

        if (today != _lastRecordedDate)
        {
            _lastRecordedDate = today;
            if (CompletedDateFilterPreset != "Custom" && CompletedDateFilterPreset != "All")
            {
                ApplyCompletedDatePreset(CompletedDateFilterPreset);
            }
            UpdateSubCollections();
            UpdateAvailableCategories();
            GenerateCalendarGrid();
        }
    }

    [RelayCommand]
    private async Task ReviewNowAsync()
    {
        IsReviewPromptVisible = false;
        await ShowTaskReviewWizardAsync();
    }

    [RelayCommand]
    private void ReviewLater()
    {
        IsReviewPromptVisible = false;
        StatusMessage = "Task updates saved for later review.";
    }

    [RelayCommand]
    public async Task ShowTaskReviewWizardAsync()
    {
        await TaskConflictVm.LoadConflictsAsync();
        if (TaskConflictVm.Conflicts.Count == 0) return;

        var tcs = new TaskCompletionSource<bool>();

        EventHandler onCompletedHandler = null!;
        onCompletedHandler = (_, _) =>
        {
            TaskConflictVm.OnCompleted -= onCompletedHandler;
            tcs.TrySetResult(true);
        };
        TaskConflictVm.OnCompleted += onCompletedHandler;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var window = new TaskConflictDialog
            {
                DataContext = TaskConflictVm
            };

            EventHandler desktopCloseHandler = null!;
            desktopCloseHandler = (_, _) =>
            {
                TaskConflictVm.OnCompleted -= desktopCloseHandler;
                window.Close();
            };
            TaskConflictVm.OnCompleted += desktopCloseHandler;

            await window.ShowDialog(desktop.MainWindow);
            tcs.TrySetResult(true);
        }
        else
        {
            IsTaskWizardVisible = true;
            await tcs.Task;
            IsTaskWizardVisible = false;
        }

        await LoadTodoItemsAsync();
        StatusMessage = "Syncing reviewed changes to Google Drive...";
        try
        {
            await _syncService.SyncAsync();
            StatusMessage = "All task updates have been reviewed successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Task updates saved. Sync update pending: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenConflictCenter()
    {
        _ = ShowTaskReviewWizardAsync();
    }

    private void UpdateGoogleAuthState()
    {
        IsGoogleSignedIn = _syncService.IsSignedIn;
        GoogleUserEmail = _syncService.UserEmail ?? string.Empty;
        GoogleUserName = _syncService.UserName ?? "Google Account User";
        GoogleClientId = _syncService.GoogleClientId ?? string.Empty;
        FirebaseApiKey = _syncService.FirebaseApiKey ?? string.Empty;
        FirebaseProjectId = _syncService.FirebaseProjectId ?? string.Empty;
    }

    [RelayCommand]
    private async Task LoadTodoItemsAsync()
    {
        try
        {
            var itemsList = (await _todoService.GetTodosAsync()).ToList();
            var notesDict = await _todoService.GetAllDateNotesAsync();
            var customTagsDict = await _todoService.GetCustomTagsAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _dateNotes.Clear();
                foreach (var kvp in notesDict)
                {
                    _dateNotes[kvp.Key] = kvp.Value;
                }

                _customEmptyTags.Clear();
                foreach (var kvp in customTagsDict)
                {
                    if (!_customEmptyTags.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        _customEmptyTags.Add(kvp.Key);
                    }
                    _tagCreatedTimes[kvp.Key] = kvp.Value;
                }

                var existingDict = TodoItems.GroupBy(vm => vm.Id).ToDictionary(g => g.Key, g => g.First());
                var freshIds = new HashSet<Guid>(itemsList.Select(x => x.Id));

                // 1. Remove items that no longer exist
                for (int i = TodoItems.Count - 1; i >= 0; i--)
                {
                    if (!freshIds.Contains(TodoItems[i].Id))
                    {
                        TodoItems.RemoveAt(i);
                    }
                }

                // 2. Update existing items in place or add new ones in order
                for (int i = 0; i < itemsList.Count; i++)
                {
                    var item = itemsList[i];
                    if (existingDict.TryGetValue(item.Id, out var existingVm))
                    {
                        existingVm.UpdateFromModel(item);
                        var currentIndex = TodoItems.IndexOf(existingVm);
                        if (currentIndex != i && currentIndex >= 0)
                        {
                            TodoItems.Move(currentIndex, i);
                        }
                    }
                    else
                    {
                        TodoItems.Insert(i, new TodoItemViewModel(item));
                    }
                }

                UpdateSubCollections();
                UpdateAvailableCategories();
                GenerateCalendarGrid();
                LastUpdatedAt = DateTime.Now;
                StatusMessage = $"Loaded {TodoItems.Count} {(TodoItems.Count == 1 ? "task" : "tasks")} from device.";

                // When tasks are first loaded from disk/DB on startup, seed notification state
                // to prevent pop-up alerts for existing tasks on app launch.
                if (!_isInitialNotificationSeeded)
                {
                    _isInitialNotificationSeeded = true;
                    SeedInitialNotificationState();
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error loading tasks: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private void SetDueDateLaterToday()
    {
        NewTaskDueDate = DateTime.Today;
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void SetDueDateTomorrow()
    {
        NewTaskDueDate = DateTime.Today.AddDays(1);
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void SetDueDateNextWeek()
    {
        NewTaskDueDate = DateTime.Today.AddDays(7);
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void ClearDueDate()
    {
        NewTaskDueDate = null;
    }

    [RelayCommand]
    private void SetReminderLaterToday()
    {
        var time = GetSuggestedLaterTodayTime();
        var preset = DateTime.Today.Add(time);
        if (preset <= DateTime.Now)
        {
            StatusMessage = "Cannot set reminder in the past.";
            return;
        }
        NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = time;
        CloseMobileReminderSheet();
    }

    [RelayCommand]
    private void SetReminderTomorrowMorning()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(1);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
        CloseMobileReminderSheet();
    }

    [RelayCommand]
    private void SetReminderNextWeek()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(7);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
        CloseMobileReminderSheet();
    }

    [RelayCommand]
    private void ClearReminder()
    {
        NewTaskReminderDate = null;
        NewTaskReminderTime = null;
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        var reminderAt = GetCombinedNewTaskReminder();
        if (NewTaskDueDate.HasValue && reminderAt.HasValue)
        {
            DateTime maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;

            if (reminderAt.Value > maxAllowed)
            {
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                StatusMessage = "Reminder reset because it exceeded the due date.";
                return;
            }
        }

        try
        {
            var initialDueDate = NewTaskDueDate?.Date ?? DateTime.Today;
            var cat = NewTaskCategories.Count > 0
                ? string.Join(", ", NewTaskCategories)
                : (string.IsNullOrWhiteSpace(NewTaskCategoryInput) ? null : NewTaskCategoryInput.Trim());

            var newItem = new TodoItem
            {
                Title = NewTaskTitle.Trim(),
                Description = NewTaskDescription.Trim(),
                Category = cat,
                IsCompleted = false,
                Priority = TodoPriority.Medium,
                CreatedAt = DateTime.UtcNow,
                ReminderAt = GetCombinedNewTaskReminder(),
                IsRecurring = IsRepeatEnabled,
                RecurrenceType = IsRepeatEnabled ? SelectedRecurrenceType : "None",
                CustomRecurrenceInterval = IsRepeatEnabled && SelectedRecurrenceType == "Custom" ? CustomInterval : null,
                CustomRecurrenceUnit = IsRepeatEnabled && SelectedRecurrenceType == "Custom" ? SelectedCustomUnit : null,
                CustomWeeklyDays = IsRepeatEnabled && SelectedRecurrenceType == "Custom" && SelectedCustomUnit == "Weeks" ? GetSelectedWeeklyDaysString() : null
            };

            newItem.DueDate = IsRepeatEnabled
                ? RecurrenceHelper.GetFirstValidOccurrenceDate(newItem, initialDueDate)
                : NewTaskDueDate;

            await _todoService.AddTodoAsync(newItem);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
                NewTaskCategoryInput = string.Empty;
                NewTaskCategories.Clear();
                OnPropertyChanged(nameof(HasNewTaskCategories));
                NewTaskDueDate = null;
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                ClearRecurrence();
                IsMobileTaskComposerOpen = false;
                IsMobileDueDateSheetOpen = false;
                IsMobileReminderSheetOpen = false;
                IsMobileRepeatSheetOpen = false;
            });

            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error adding task: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task ToggleTodoAsync(TodoItemViewModel? itemVm)
    {
        if (itemVm == null) return;

        // Trigger completed sound instantly the moment the user checks the task
        if (!itemVm.IsCompleted)
        {
            _audioService.PlayCompletedSound();
        }

        lock (_togglingTaskIds)
        {
            if (!_togglingTaskIds.Add(itemVm.Id))
            {
                return;
            }
        }

        try
        {
            DateTime? targetDate = itemVm.ContextDate ?? (IsCalendarView && SelectedDay != null ? SelectedDay.Date : itemVm.DueDate);
            await _todoService.ToggleCompleteAsync(itemVm.Id, targetDate);
            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error toggling task: {ex.Message}";
            });
        }
        finally
        {
            lock (_togglingTaskIds)
            {
                _togglingTaskIds.Remove(itemVm.Id);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteTodoAsync(TodoItemViewModel? itemVm)
    {
        if (itemVm == null) return;

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Task_Title"];
        var msg = lm["Dialog_Delete_Task_Msg"];
        var itemName = itemVm.Title;
        var itemDetails = !string.IsNullOrWhiteSpace(itemVm.DueDateFormatted)
            ? itemVm.DueDateFormatted
            : (itemVm.HasDescription ? itemVm.DescriptionPreview : string.Empty);

        var confirmed = await RequestDeleteConfirmationAsync(title, msg, itemName, itemDetails);
        if (!confirmed) return;

        try
        {
            await _todoService.DeleteTodoAsync(itemVm.Id);

            if (SelectedDetailTask?.Id == itemVm.Id)
            {
                SelectedDetailTask = null;
                IsDetailDrawerOpen = false;
            }

            CloseAllOverlays();

            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error deleting task: {ex.Message}";
            });
        }
    }

    public Task<bool> RequestDeleteConfirmationAsync(string title, string message, string itemName, string itemDetails = "")
    {
        _deleteConfirmationTcs?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DeleteConfirmationTitle = title;
        DeleteConfirmationMessage = message;
        DeleteConfirmationItemName = itemName;
        DeleteConfirmationItemDetails = itemDetails;
        _deleteConfirmationTcs = tcs;
        IsDeleteConfirmationOpen = true;
        return tcs.Task;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsDeleteConfirmationOpen = false;
        _deleteConfirmationTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmationOpen = false;
        _deleteConfirmationTcs?.TrySetResult(false);
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        try
        {
            StatusMessage = "Exporting tasks to Excel...";
            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localFolder))
            {
                localFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }
            var exportDir = Path.Combine(localFolder, "Wadd", "Exports");
            Directory.CreateDirectory(exportDir);

            var fileName = $"todo_export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(exportDir, fileName);

            var models = TodoItems.Select(vm => vm.Model).ToList();
            await _exportService.ExportToExcelAsync(models, filePath);

            StatusMessage = $"Exported {models.Count} {(models.Count == 1 ? "task" : "tasks")} successfully to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Export", $"Export failed: {ex.Message}", ex);
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private CancellationTokenSource? _googleSignInCts;

    [RelayCommand]
    private async Task SignInWithGoogleAsync()
    {
        if (IsGoogleSigningIn) return;

        _googleSignInCts?.Cancel();
        _googleSignInCts?.Dispose();
        _googleSignInCts = new CancellationTokenSource();
        var ct = _googleSignInCts.Token;

        try
        {
            IsGoogleSigningIn = true;
            StatusMessage = LocalizationManager.Instance["Main_SigningInGoogle"] ?? "Signing in with Google Account...";
            var success = await _syncService.SignInAsync(ct);
            UpdateGoogleAuthState();
            if (success)
            {
                var template = LocalizationManager.Instance["Main_SignedInSuccess"] ?? "Signed in as {0}. Connected to Google Drive.";
                StatusMessage = string.Format(template, GoogleUserEmail);
                AppLogger.LogInfo("GoogleAuth", "Triggering post-sign-in auto-sync.");
                await TriggerAutoSyncAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = LocalizationManager.Instance["Main_SignInCancelled"] ?? "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            AppLogger.LogError("GoogleAuth", $"Sign-in failed: {ex.Message}", ex);
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            IsGoogleSigningIn = false;
        }
    }

    [RelayCommand]
    private void CancelGoogleSignIn()
    {
        _googleSignInCts?.Cancel();
    }

    [RelayCommand]
    private async Task SignOutGoogleAsync()
    {
        try
        {
            await _syncService.SignOutAsync();
            UpdateGoogleAuthState();
            StatusMessage = "Signed out of Google. Cloud sync disabled.";
        }
        catch (Exception ex)
        {
            AppLogger.LogError("GoogleAuth", $"Sign-out failed: {ex.Message}", ex);
            StatusMessage = $"Sign-out failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing) return;
        try
        {
            _autoSyncDebounceTimer?.Stop();

            if (SelectedDetailTask != null)
            {
                await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            }

            IsSyncing = true;
            StatusMessage = "Syncing with Google Drive...";
            var success = await _syncService.SyncAsync();
            if (success)
            {
                LastUpdatedAt = DateTime.Now;
                if (_syncService.HasChangesApplied)
                {
                    await LoadTodoItemsAsync();
                    await GoalsVM.LoadAllGoalsAsync();
                }

                if (HasUnresolvedConflicts)
                {
                    StatusMessage = $"{UnresolvedConflictCount} task update(s) require your review.";
                    IsReviewPromptVisible = true;
                }
                else
                {
                    StatusMessage = _syncService.HasChangesApplied
                        ? "Sync completed successfully."
                        : "Everything is up to date.";
                }
            }
            else
            {
                StatusMessage = "Sync finished.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Sync", $"Manual sync failed: {ex.Message}", ex);
            UpdateGoogleAuthState();
            StatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private Task SyncAsync() => SyncNowAsync();

    private void RequestDebouncedAutoSync()
    {
        if (!IsGoogleSignedIn) return;

        if (_autoSyncDebounceTimer == null)
        {
            _autoSyncDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoSyncDebounceTimer.Tick += async (_, _) =>
            {
                _autoSyncDebounceTimer?.Stop();
                if (IsGoogleSignedIn && !IsSyncing)
                {
                    await TriggerAutoSyncAsync();
                }
            };
        }
        _autoSyncDebounceTimer.Stop();
        _autoSyncDebounceTimer.Start();
    }

    internal async Task TriggerAutoSyncAsync()
    {
        if (IsSyncing || !IsGoogleSignedIn) return;
        _autoSyncDebounceTimer?.Stop();
        AppLogger.LogInfo("Sync", "Triggering auto-sync (Foreground)...");
        try
        {
            if (SelectedDetailTask != null)
            {
                await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            }

            IsSyncing = true;
            var success = await _syncService.SyncAsync();
            if (success)
            {
                LastUpdatedAt = DateTime.Now;
                if (_syncService.HasChangesApplied)
                {
                    await LoadTodoItemsAsync();
                    await GoalsVM.LoadAllGoalsAsync();
                }

                if (HasUnresolvedConflicts)
                {
                    StatusMessage = $"{UnresolvedConflictCount} task update(s) require your review.";
                    IsReviewPromptVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("Sync", $"Auto sync background failed: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void CloseAllOverlays()
    {
        CancelDelete();
        IsMobileMoreSheetOpen = false;
        IsMobileTagFilterSheetOpen = false;
        IsMobileCategorySheetOpen = false;
        IsMobileDueDateSheetOpen = false;
        IsMobileReminderSheetOpen = false;
        IsMobileRepeatSheetOpen = false;
        IsMobileCompletedDateFilterSheetOpen = false;
        IsMobileTaskComposerOpen = false;
        IsDetailDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenTasksView()
    {
        SelectedNavIndex = 0;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenCompletedView()
    {
        SelectedNavIndex = 1;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenRecurringView()
    {
        SelectedNavIndex = 2;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenCalendarView()
    {
        SelectedNavIndex = 3;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenSearchView(object? parameter = null)
    {
        SelectedNavIndex = 4;
        CloseAllOverlays();

        if (string.IsNullOrWhiteSpace(SearchStatusFilter))
        {
            SearchStatusFilter = "All";
        }
        OnPropertyChanged(nameof(IsSearchFilterAll));
        OnPropertyChanged(nameof(IsSearchFilterActive));
        OnPropertyChanged(nameof(IsSearchFilterCompleted));
        OnPropertyChanged(nameof(IsSearchFilterWithNotes));

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenTagsView(object? parameter = null)
    {
        SelectedNavIndex = 5;
        CloseAllOverlays();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenGoalsView(object? parameter = null)
    {
        SelectedNavIndex = 6;
        CloseAllOverlays();
        _ = GoalsVM.InitializeAsync();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenSettings(object? parameter = null)
    {
        SelectedNavIndex = 7;
        CloseAllOverlays();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void ToggleMobileMoreSheet() => IsMobileMoreSheetOpen = !IsMobileMoreSheetOpen;

    [RelayCommand]
    private void CloseMobileMoreSheet() => IsMobileMoreSheetOpen = false;

    [RelayCommand]
    private async Task CreateNewGlobalTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGlobalTagInput)) return;
        var tag = NewGlobalTagInput.Trim();
        if (!_customEmptyTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            var created = DateTime.UtcNow;
            _customEmptyTags.Insert(0, tag);
            _tagCreatedTimes[tag] = created;
            NewGlobalTagInput = string.Empty;
            await _todoService.SaveCustomTagAsync(tag, created);
            UpdateAvailableCategories();
        }
    }

    private async Task RenameGlobalTagAsync(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        var oldTag = oldName.Trim();
        var newTag = newName.Trim();

        var prevTime = _tagCreatedTimes.TryGetValue(oldTag, out var t) ? t : DateTime.UtcNow;
        _tagCreatedTimes.Remove(oldTag);
        _tagCreatedTimes[newTag] = prevTime;

        _customEmptyTags.RemoveAll(x => x.Equals(oldTag, StringComparison.OrdinalIgnoreCase));
        if (!_customEmptyTags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
        {
            _customEmptyTags.Insert(0, newTag);
        }

        await _todoService.RenameCustomTagAsync(oldTag, newTag);
        await _todoService.RenameCategoryAsync(oldTag, newTag);
        await LoadTodoItemsAsync();
    }

    private async Task DeleteGlobalTagAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        var tag = tagName.Trim();

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Tag_Title"];
        var msg = lm["Dialog_Delete_Tag_Msg"];

        var confirmed = await RequestDeleteConfirmationAsync(title, msg, $"#{tag}");
        if (!confirmed) return;

        _tagCreatedTimes.Remove(tag);
        _customEmptyTags.RemoveAll(x => x.Equals(tag, StringComparison.OrdinalIgnoreCase));
        await _todoService.DeleteCustomTagAsync(tag);
        await _todoService.DeleteCategoryAsync(tag);
        await LoadTodoItemsAsync();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_syncService is GoogleDriveSyncService driveSync)
        {
            driveSync.WebAppUrl = SyncEndpointUrl.Trim();
        }
        StatusMessage = "Settings saved successfully.";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
    }

    [RelayCommand]
    private void SetThemeMode(string modeName)
    {
        if (Enum.TryParse<ThemeMode>(modeName, true, out var mode))
        {
            _themeService.SetTheme(mode);
        }
    }

    [RelayCommand]
    private void ToggleSideMenu()
    {
        IsSideMenuOpen = !IsSideMenuOpen;
    }

    [RelayCommand]
    private void ToggleNavExpanded()
    {
        IsNavExpanded = !IsNavExpanded;
    }

    [RelayCommand]
    private void CloseSideMenu()
    {
        IsSideMenuOpen = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadTodoItemsAsync();
    }

    public bool IsDarkMode
    {
        get
        {
            if (CurrentThemeMode == ThemeMode.Dark) return true;
            if (CurrentThemeMode == ThemeMode.Light) return false;
            return Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        }
    }

    private void UpdateThemeLabel()
    {
        var lm = LocalizationManager.Instance;
        var lightText = lm["Settings_Theme_Light"];
        var darkText = lm["Settings_Theme_Dark"];
        var systemText = lm["Settings_Theme_System"];
        var resolved = IsDarkMode ? darkText : lightText;
        CurrentThemeLabel = CurrentThemeMode switch
        {
            ThemeMode.Light => lightText,
            ThemeMode.Dark => darkText,
            _ => $"{systemText} — {resolved}"
        };
        OnPropertyChanged(nameof(IsDarkMode));
    }
}

public enum NotificationBubbleType
{
    Info,
    Success,
    Warning,
    Error
}

public partial class NotificationBubbleItem : ObservableObject
{
    public string Message { get; }
    public string Timestamp { get; }
    public NotificationBubbleType Type { get; }

    public bool IsInfo => Type == NotificationBubbleType.Info;
    public bool IsSuccess => Type == NotificationBubbleType.Success;
    public bool IsWarning => Type == NotificationBubbleType.Warning;
    public bool IsError => Type == NotificationBubbleType.Error;

    [ObservableProperty]
    private double _opacity = 1.0;

    public NotificationBubbleItem(string message, NotificationBubbleType type = NotificationBubbleType.Info)
    {
        Message = message;
        Type = type;
        Timestamp = DateTime.Now.ToString("HH:mm");
    }
}

public partial class CategoryFilterOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public Action<CategoryFilterOption>? OnSelectionChanged { get; set; }

    public CategoryFilterOption(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnSelectionChanged?.Invoke(this);
    }
}

public partial class MainViewModel
{
    private void LoadUserSettings()
    {
        var settings = AppSettingsHelper.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.TasksViewLayout))
        {
            TasksViewLayout = settings.TasksViewLayout;
            SelectedTasksLayoutOption = TasksLayoutOptions.FirstOrDefault(x => x.Id == settings.TasksViewLayout) ?? TasksLayoutOptions[0];
        }
        if (!string.IsNullOrWhiteSpace(settings.UpcomingTasksRange))
        {
            UpcomingTasksRange = settings.UpcomingTasksRange;
            SelectedUpcomingTasksRangeOption = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == settings.UpcomingTasksRange) ?? UpcomingTasksRangeOptions[^1];
        }
        else
        {
            SelectedUpcomingTasksRangeOption = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == UpcomingTasksRange) ?? UpcomingTasksRangeOptions[^1];
        }
        ShowNotePreviewsInList = settings.ShowNotePreviewsInList;
        _themeService.SetTheme(settings.ThemeMode);
        Language = string.IsNullOrWhiteSpace(settings.Language) ? "system" : settings.Language;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(x => x.Id.Equals(Language, StringComparison.OrdinalIgnoreCase)) ?? LanguageOptions[0];
        LocalizationManager.Instance.SetLanguage(Language);

        AutoStartOnBoot = _startupService.IsSupported ? _startupService.IsAutoStartEnabled() : settings.AutoStartOnBoot;
        StartMinimized = settings.StartMinimized;
        MinimizeToTray = settings.MinimizeToTray;
        CloseToTray = settings.CloseToTray;
        EnableTrayIcon = settings.EnableTrayIcon;
        AiProvider = string.IsNullOrWhiteSpace(settings.AiProvider) ? "Gemini" : settings.AiProvider;
        SelectedAiProviderOption = AiProviderOptions.FirstOrDefault(x => x.Id.Equals(AiProvider, StringComparison.OrdinalIgnoreCase)) ?? AiProviderOptions[0];
        AiApiKey = !string.IsNullOrWhiteSpace(settings.AiApiKey) ? settings.AiApiKey : (settings.GeminiApiKey ?? string.Empty);
        AiCustomBaseUrl = settings.AiCustomBaseUrl ?? string.Empty;
        AiCustomModel = settings.AiCustomModel ?? string.Empty;

        _aiGoalService.Provider = AiProvider;
        _aiGoalService.ApiKey = AiApiKey;
        _aiGoalService.CustomBaseUrl = AiCustomBaseUrl;
        _aiGoalService.CustomModel = AiCustomModel;

        EnableNotifications = settings.EnableNotifications;
        NotifyOnTaskReminder = settings.NotifyOnTaskReminder;
        NotifyOnOverdueTasks = settings.NotifyOnOverdueTasks;
        NotifyOnTaskDueDate = settings.NotifyOnTaskDueDate;
        NotificationLeadTimeMinutes = settings.NotificationLeadTimeMinutes;
        SelectedNotificationLeadTimeOption = NotificationLeadTimeOptions.FirstOrDefault(x => x.Minutes == settings.NotificationLeadTimeMinutes) ?? NotificationLeadTimeOptions[0];
        NotificationRepeatIntervalMinutes = settings.NotificationRepeatIntervalMinutes;
        SelectedNotificationRepeatIntervalOption = NotificationRepeatIntervalOptions.FirstOrDefault(x => x.Minutes == settings.NotificationRepeatIntervalMinutes) ?? NotificationRepeatIntervalOptions[0];
        PlayNotificationSound = settings.PlayNotificationSound;
        PlayTaskCompletedSound = settings.PlayTaskCompletedSound;
        WindowsToastNotifications = true; // Always on
        AndroidVibration = settings.AndroidVibration;
        AndroidHighPriorityChannel = settings.AndroidHighPriorityChannel;
        AndroidStickyReminders = settings.AndroidStickyReminders;

        _ = LoadAvailableModelsAsync(forceLive: false);
    }

    public void SaveUserSettings()
    {
        var settings = AppSettingsHelper.LoadSettings();
        settings.TasksViewLayout = TasksViewLayout;
        settings.UpcomingTasksRange = UpcomingTasksRange;
        settings.ShowNotePreviewsInList = ShowNotePreviewsInList;
        settings.ThemeMode = _themeService.CurrentTheme;
        settings.Language = Language;
        settings.AutoStartOnBoot = AutoStartOnBoot;
        settings.StartMinimized = StartMinimized;
        settings.MinimizeToTray = MinimizeToTray;
        settings.CloseToTray = CloseToTray;
        settings.EnableTrayIcon = EnableTrayIcon;
        settings.EnableNotifications = EnableNotifications;
        settings.NotifyOnTaskReminder = NotifyOnTaskReminder;
        settings.NotifyOnOverdueTasks = NotifyOnOverdueTasks;
        settings.NotifyOnTaskDueDate = NotifyOnTaskDueDate;
        settings.NotificationLeadTimeMinutes = NotificationLeadTimeMinutes;
        settings.NotificationRepeatIntervalMinutes = NotificationRepeatIntervalMinutes;
        settings.PlayNotificationSound = PlayNotificationSound;
        settings.PlayTaskCompletedSound = PlayTaskCompletedSound;
        settings.WindowsToastNotifications = true; // Always on
        settings.AndroidVibration = AndroidVibration;
        settings.AndroidHighPriorityChannel = AndroidHighPriorityChannel;
        settings.AndroidStickyReminders = AndroidStickyReminders;
        settings.AiProvider = AiProvider;
        settings.AiApiKey = AiApiKey;
        settings.AiCustomBaseUrl = AiCustomBaseUrl;
        settings.AiCustomModel = AiCustomModel;
        settings.GeminiApiKey = AiApiKey;
        AppSettingsHelper.SaveSettings(settings);
    }

    public static bool PassesUpcomingRangeFilter(TodoItemViewModel item, DateTime today, string range)
    {
        var targetDate = (item.DueDate ?? item.ReminderAt)?.Date;
        if (!targetDate.HasValue) return false;
        if (targetDate.Value <= today) return false;

        return range switch
        {
            "Tomorrow" => targetDate.Value == today.AddDays(1),
            "Next3Days" => targetDate.Value <= today.AddDays(3),
            "ThisWeek" => targetDate.Value <= GetEndOfWeek(today),
            "Next7Days" => targetDate.Value <= today.AddDays(7),
            "ThisMonth" => targetDate.Value <= new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            "Next30Days" => targetDate.Value <= today.AddDays(30),
            _ => true // "All"
        };
    }

    private static DateTime GetEndOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(6 - diff).Date;
    }

    public string CurrentLogFilePath => AppDataHelper.GetCurrentLogFilePath();
    public string LogsDirectoryPath => AppDataHelper.GetLogsDirectory();

    [RelayCommand]
    private async Task CopyDiagnosticsLogsAsync()
    {
        try
        {
            var logsText = AppLogger.GetRecentLogsText();
            if (string.IsNullOrWhiteSpace(logsText))
            {
                logsText = "No log entries recorded yet.";
            }

            var topLevel = TopLevel.GetTopLevel(
                (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView ??
                (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);

            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(logsText);
                ShowStatusBubble("Diagnostics logs copied to clipboard.", NotificationBubbleType.Success);
            }
            else
            {
                ShowStatusBubble("Clipboard is not available on this platform.", NotificationBubbleType.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("MainViewModel", "Failed to copy diagnostics logs", ex);
            ShowStatusBubble($"Failed to copy logs: {ex.Message}", NotificationBubbleType.Error);
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            var folder = AppDataHelper.GetLogsDirectory();
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", folder);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("MainViewModel", "Failed to open logs folder", ex);
            ShowStatusBubble($"Failed to open logs folder: {ex.Message}", NotificationBubbleType.Error);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        try
        {
            AppLogger.ClearRecentLogs();
            ShowStatusBubble("Recent logs buffer cleared.", NotificationBubbleType.Info);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("MainViewModel", "Failed to clear recent logs", ex);
        }
    }

    public void RefreshLocalizedOptionLists()
    {
        var lm = LocalizationManager.Instance;

        var optStandard = TasksLayoutOptions.FirstOrDefault(x => x.Id == "Standard");
        if (optStandard != null)
        {
            optStandard.Name = lm["Tasks_Layout_StandardMode"];
            optStandard.Description = lm["Tasks_Layout_Standard_Desc"];
        }
        var optFocus = TasksLayoutOptions.FirstOrDefault(x => x.Id == "Focus");
        if (optFocus != null)
        {
            optFocus.Name = lm["Tasks_Layout_Focus"];
            optFocus.Description = lm["Tasks_Layout_Focus_Desc"];
        }
        var optCompletedFirst = TasksLayoutOptions.FirstOrDefault(x => x.Id == "CompletedFirst");
        if (optCompletedFirst != null)
        {
            optCompletedFirst.Name = lm["Tasks_Layout_CompletedFirst"];
            optCompletedFirst.Description = lm["Tasks_Layout_CompletedFirst_Desc"];
        }
        var optTodayCompleted = TasksLayoutOptions.FirstOrDefault(x => x.Id == "TodayCompletedOnly");
        if (optTodayCompleted != null)
        {
            optTodayCompleted.Name = lm["Tasks_Layout_TodayCompletedOnly"];
            optTodayCompleted.Description = lm["Tasks_Layout_TodayCompletedOnly_Desc"];
        }
        var optTodayUpcoming = TasksLayoutOptions.FirstOrDefault(x => x.Id == "TodayUpcomingOnly");
        if (optTodayUpcoming != null)
        {
            optTodayUpcoming.Name = lm["Tasks_Layout_TodayUpcomingOnly"];
            optTodayUpcoming.Description = lm["Tasks_Layout_TodayUpcomingOnly_Desc"];
        }

        var optTomorrow = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "Tomorrow");
        if (optTomorrow != null)
        {
            optTomorrow.Name = lm["Tasks_Upcoming_Tomorrow"];
            optTomorrow.Description = lm["Tasks_Upcoming_Tomorrow_Desc"];
        }
        var optNext3Days = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "Next3Days");
        if (optNext3Days != null)
        {
            optNext3Days.Name = lm["Tasks_Upcoming_3Days"];
            optNext3Days.Description = lm["Tasks_Upcoming_3Days_Desc"];
        }
        var optThisWeek = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "ThisWeek");
        if (optThisWeek != null)
        {
            optThisWeek.Name = lm["Tasks_Upcoming_7Days"];
            optThisWeek.Description = lm["Tasks_Upcoming_7Days_Desc"];
        }
        var optNext7Days = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "Next7Days");
        if (optNext7Days != null)
        {
            optNext7Days.Name = lm["Tasks_Upcoming_7Days"];
            optNext7Days.Description = lm["Tasks_Upcoming_7Days_Desc"];
        }
        var optThisMonth = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "ThisMonth");
        if (optThisMonth != null)
        {
            optThisMonth.Name = lm["Tasks_Upcoming_Month"];
            optThisMonth.Description = lm["Tasks_Upcoming_Month_Desc"];
        }
        var optNext30Days = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "Next30Days");
        if (optNext30Days != null)
        {
            optNext30Days.Name = lm["Tasks_Upcoming_14Days"];
            optNext30Days.Description = lm["Tasks_Upcoming_14Days_Desc"];
        }
        var optAll = UpcomingTasksRangeOptions.FirstOrDefault(x => x.Id == "All");
        if (optAll != null)
        {
            optAll.Name = lm["Tasks_Upcoming_All"];
            optAll.Description = lm["Tasks_Upcoming_All_Desc"];
        }

        var optSystemLang = LanguageOptions.FirstOrDefault(x => x.Id == "system");
        if (optSystemLang != null)
        {
            optSystemLang.Name = $"{lm["Settings_Language_System"]} / Default Sistem";
            optSystemLang.Description = lm["Settings_Language_Desc"];
        }

        OnPropertyChanged(nameof(UpcomingTasksRangeLabel));
        OnPropertyChanged(nameof(SelectedLanguageOption));
        OnPropertyChanged(nameof(SelectedTasksLayoutOption));
        OnPropertyChanged(nameof(SelectedUpcomingTasksRangeOption));
    }
}

public partial class TasksLayoutOption : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
}

public partial class UpcomingTasksRangeOption : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
}

public class AiProviderOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string KeyWatermark { get; set; } = string.Empty;
    public string KeyHelpText { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
}

public class NotificationLeadTimeOption
{
    public int Minutes { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class NotificationRepeatIntervalOption
{
    public int Minutes { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public partial class LanguageOption : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
}


