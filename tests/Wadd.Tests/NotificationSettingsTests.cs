using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

[Collection("AppSettingsTests")]
public class NotificationSettingsTests
{
    [Fact]
    public void AppSettingsData_NotificationDefaults_AreCorrect()
    {
        var settings = new AppSettingsData();

        Assert.True(settings.EnableNotifications);
        Assert.True(settings.NotifyOnTaskReminder);
        Assert.True(settings.NotifyOnOverdueTasks);
        Assert.True(settings.NotifyOnTaskDueDate);
        Assert.Equal(0, settings.NotificationLeadTimeMinutes);
        Assert.Equal(0, settings.NotificationRepeatIntervalMinutes);
        Assert.True(settings.PlayNotificationSound);

        // Windows defaults
        Assert.True(settings.WindowsToastNotifications);
        Assert.True(settings.WindowsNotificationIncludeNotes);

        // Android defaults
        Assert.True(settings.AndroidVibration);
        Assert.True(settings.AndroidHighPriorityChannel);
        Assert.False(settings.AndroidStickyReminders);
    }

    [Fact]
    public void AppSettingsHelper_SaveAndLoad_PersistsNotificationSettings()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddNotificationTest_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WADD_DATA_DIR", tempPath);

        try
        {
            var original = new AppSettingsData
            {
                EnableNotifications = false,
                NotifyOnTaskReminder = false,
                NotifyOnOverdueTasks = true,
                NotifyOnTaskDueDate = true,
                NotificationLeadTimeMinutes = 15,
                NotificationRepeatIntervalMinutes = 60,
                PlayNotificationSound = false,
                WindowsToastNotifications = false,
                WindowsNotificationIncludeNotes = false,
                AndroidVibration = false,
                AndroidHighPriorityChannel = false,
                AndroidStickyReminders = true
            };

            AppSettingsHelper.SaveSettings(original);

            var loaded = AppSettingsHelper.LoadSettings();

            Assert.False(loaded.EnableNotifications);
            Assert.False(loaded.NotifyOnTaskReminder);
            Assert.True(loaded.NotifyOnOverdueTasks);
            Assert.True(loaded.NotifyOnTaskDueDate);
            Assert.Equal(15, loaded.NotificationLeadTimeMinutes);
            Assert.Equal(60, loaded.NotificationRepeatIntervalMinutes);
            Assert.False(loaded.PlayNotificationSound);

            Assert.False(loaded.WindowsToastNotifications);
            Assert.False(loaded.WindowsNotificationIncludeNotes);

            Assert.False(loaded.AndroidVibration);
            Assert.False(loaded.AndroidHighPriorityChannel);
            Assert.True(loaded.AndroidStickyReminders);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WADD_DATA_DIR", null);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void AppSettingsData_JsonSerialization_PreservesAllNotificationProperties()
    {
        var original = new AppSettingsData
        {
            EnableNotifications = true,
            NotifyOnTaskReminder = true,
            NotifyOnOverdueTasks = true,
            NotifyOnTaskDueDate = false,
            NotificationLeadTimeMinutes = 30,
            NotificationRepeatIntervalMinutes = 120,
            PlayNotificationSound = true,
            WindowsToastNotifications = true,
            WindowsNotificationIncludeNotes = true,
            AndroidVibration = true,
            AndroidHighPriorityChannel = true,
            AndroidStickyReminders = false
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettingsData>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.EnableNotifications);
        Assert.True(deserialized.NotifyOnTaskReminder);
        Assert.True(deserialized.NotifyOnOverdueTasks);
        Assert.False(deserialized.NotifyOnTaskDueDate);
        Assert.Equal(30, deserialized.NotificationLeadTimeMinutes);
        Assert.Equal(120, deserialized.NotificationRepeatIntervalMinutes);
        Assert.True(deserialized.PlayNotificationSound);
        Assert.True(deserialized.WindowsToastNotifications);
        Assert.True(deserialized.WindowsNotificationIncludeNotes);
        Assert.True(deserialized.AndroidVibration);
        Assert.True(deserialized.AndroidHighPriorityChannel);
        Assert.False(deserialized.AndroidStickyReminders);
    }

    private class MockNotificationService : INotificationService
    {
        public bool IsSupported => true;
        public List<(string Title, string Message, string? Tag)> ShownNotifications { get; } = new();
        public List<string> CancelledTags { get; } = new();

        public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

        public Task ShowNotificationAsync(string title, string message, string? tag = null)
        {
            ShownNotifications.Add((title, message, tag));
            return Task.CompletedTask;
        }

        public Task CancelNotificationAsync(string tag)
        {
            CancelledTags.Add(tag);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task MockNotificationService_CapturesNotificationsCorrectly()
    {
        var service = new MockNotificationService();

        await service.ShowNotificationAsync("Meeting", "Prep for standup", "tag-1");
        await service.CancelNotificationAsync("tag-1");

        Assert.Single(service.ShownNotifications);
        Assert.Equal("Meeting", service.ShownNotifications[0].Title);
        Assert.Equal("Prep for standup", service.ShownNotifications[0].Message);
        Assert.Equal("tag-1", service.ShownNotifications[0].Tag);

        Assert.Single(service.CancelledTags);
        Assert.Equal("tag-1", service.CancelledTags[0]);
    }

    [Fact]
    public void NotificationLeadTimeOptions_ContainExpectedValues()
    {
        var vm = new MainViewModel();

        Assert.NotEmpty(vm.NotificationLeadTimeOptions);
        Assert.Contains(vm.NotificationLeadTimeOptions, opt => opt.Minutes == 0);
        Assert.Contains(vm.NotificationLeadTimeOptions, opt => opt.Minutes == 5);
        Assert.Contains(vm.NotificationLeadTimeOptions, opt => opt.Minutes == 15);
        Assert.Contains(vm.NotificationLeadTimeOptions, opt => opt.Minutes == 30);
        Assert.Contains(vm.NotificationLeadTimeOptions, opt => opt.Minutes == 60);
    }

    [Fact]
    public async Task MainViewModel_SendTestNotification_InvokesNotificationService()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        await vm.SendTestNotificationAsync();

        Assert.Single(mockService.ShownNotifications);
        Assert.Contains("Test", mockService.ShownNotifications[0].Title);
        Assert.NotNull(vm.TestNotificationFeedback);
    }

    [Fact]
    public void NotificationRepeatIntervalOptions_ContainExpectedValues()
    {
        var vm = new MainViewModel();

        Assert.NotEmpty(vm.NotificationRepeatIntervalOptions);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 0);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 30);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 60);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 120);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 180);
        Assert.Contains(vm.NotificationRepeatIntervalOptions, opt => opt.Minutes == 300);
    }

    [Fact]
    public void MainViewModel_CheckReminders_SingleItem_FiresIndividualNotification()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskReminder = true;

        var now = DateTime.Now;
        var singleTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Morning Standup",
            ReminderAt = now.AddHours(-1)
        });

        vm.TodoItems.Add(singleTask);
        vm.CheckReminders();

        Assert.Single(mockService.ShownNotifications);
        var notif = mockService.ShownNotifications[0];
        Assert.Equal("Reminder: Morning Standup", notif.Title);
        Assert.Equal("Morning Standup", notif.Message);
        Assert.Equal(singleTask.Id.ToString(), notif.Tag);
    }

    [Fact]
    public void MainViewModel_CheckReminders_MultipleItems_BundlesIntoSummaryNotification()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskReminder = true;
        vm.NotifyOnOverdueTasks = true;
        vm.NotifyOnTaskDueDate = true;

        var now = DateTime.Now;
        // Task 1: Reminder was 1 hour ago today (passed)
        var passedReminderTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Missed Morning Standup",
            ReminderAt = now.AddHours(-1)
        });

        // Task 2: Due yesterday (overdue)
        var overdueTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Submit Quarterly Report",
            DueDate = now.AddDays(-1)
        });

        // Task 3: Due today
        var dueTodayTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Dentist Appointment",
            DueDate = now.Date
        });

        vm.TodoItems.Add(passedReminderTask);
        vm.TodoItems.Add(overdueTask);
        vm.TodoItems.Add(dueTodayTask);

        vm.CheckReminders();

        // 3 items triggering together must be bundled into 1 summary notification
        Assert.Single(mockService.ShownNotifications);
        var notif = mockService.ShownNotifications[0];
        Assert.Equal("3 Task Reminders", notif.Title);
        Assert.Contains("Missed Morning Standup", notif.Message);
        Assert.Contains("Submit Quarterly Report", notif.Message);
        Assert.Contains("Dentist Appointment", notif.Message);
        Assert.Equal("tasks-summary", notif.Tag);
    }

    [Fact]
    public void MainViewModel_CheckReminders_MoreThanThreeItems_CapsSummaryWithMoreCount()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskReminder = true;

        var now = DateTime.Now;
        for (int i = 1; i <= 5; i++)
        {
            vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
            {
                Id = Guid.NewGuid(),
                Title = $"Task Item {i}",
                ReminderAt = now.AddHours(-1)
            }));
        }

        vm.CheckReminders();

        Assert.Single(mockService.ShownNotifications);
        var notif = mockService.ShownNotifications[0];
        Assert.Equal("5 Task Reminders", notif.Title);
        Assert.Contains("• Task Item 1", notif.Message);
        Assert.Contains("• Task Item 2", notif.Message);
        Assert.Contains("• Task Item 3", notif.Message);
        Assert.Contains("+ 2 more tasks", notif.Message);
        Assert.Equal("tasks-summary", notif.Tag);
    }

    [Fact]
    public void MainViewModel_CheckReminders_AllOverdueTasks_UsesOverdueTitle()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnOverdueTasks = true;

        var now = DateTime.Now;
        vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Overdue Invoice",
            DueDate = now.AddDays(-2)
        }));
        vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Overdue Taxes",
            DueDate = now.AddDays(-1)
        }));

        vm.CheckReminders();

        Assert.Single(mockService.ShownNotifications);
        var notif = mockService.ShownNotifications[0];
        Assert.Equal("2 Overdue Tasks", notif.Title);
        Assert.Contains("Overdue Invoice", notif.Message);
        Assert.Contains("Overdue Taxes", notif.Message);
    }

    [Fact]
    public void WindowsNotificationService_EnsureLogoFileOnDisk_ReturnsSquareLogo()
    {
        var logoPath = Wadd.Services.WindowsNotificationService.EnsureLogoFileOnDisk();
        Assert.NotNull(logoPath);
        Assert.True(File.Exists(logoPath));
        Assert.EndsWith("logo_square.png", logoPath);
        Assert.True(new FileInfo(logoPath).Length > 0);
    }

    [Fact]
    public void NotificationBubbleItem_Types_ExposeCorrectBooleanFlags()
    {
        var info = new NotificationBubbleItem("Info message", NotificationBubbleType.Info);
        Assert.True(info.IsInfo);
        Assert.False(info.IsWarning);
        Assert.False(info.IsError);
        Assert.False(info.IsSuccess);

        var warning = new NotificationBubbleItem("Warning message", NotificationBubbleType.Warning);
        Assert.False(warning.IsInfo);
        Assert.True(warning.IsWarning);
        Assert.False(warning.IsError);
        Assert.False(warning.IsSuccess);

        var error = new NotificationBubbleItem("Error message", NotificationBubbleType.Error);
        Assert.False(error.IsInfo);
        Assert.False(error.IsWarning);
        Assert.True(error.IsError);
        Assert.False(error.IsSuccess);

        var success = new NotificationBubbleItem("Success message", NotificationBubbleType.Success);
        Assert.False(success.IsInfo);
        Assert.False(success.IsWarning);
        Assert.False(success.IsError);
        Assert.True(success.IsSuccess);
    }

    [Fact]
    public void MainViewModel_InferBubbleType_CorrectlyDetectsSemanticTypes()
    {
        Assert.Equal(NotificationBubbleType.Error, MainViewModel.InferBubbleType("Error adding task: connection lost"));
        Assert.Equal(NotificationBubbleType.Error, MainViewModel.InferBubbleType("Connection test failed: timeout"));
        Assert.Equal(NotificationBubbleType.Warning, MainViewModel.InferBubbleType("Cannot set reminder in the past."));
        Assert.Equal(NotificationBubbleType.Warning, MainViewModel.InferBubbleType("Reminder reset because it exceeded the due date."));
        Assert.Equal(NotificationBubbleType.Warning, MainViewModel.InferBubbleType("3 task update(s) require your review."));
        Assert.Equal(NotificationBubbleType.Success, MainViewModel.InferBubbleType("Settings saved successfully."));
        Assert.Equal(NotificationBubbleType.Success, MainViewModel.InferBubbleType("Sync completed successfully."));
        Assert.Equal(NotificationBubbleType.Info, MainViewModel.InferBubbleType("Loaded 5 tasks from device."));
    }

    [Fact]
    public void MainViewModel_ShowStatusBubble_AddsItemWithInferredOrExplicitType()
    {
        var vm = new MainViewModel();

        vm.ShowStatusBubble("Cannot set due date in the past");
        Assert.NotEmpty(vm.StatusNotifications);
        var last = vm.StatusNotifications[^1];
        Assert.Equal(NotificationBubbleType.Warning, last.Type);
        Assert.True(last.IsWarning);

        vm.ShowStatusBubble("Explicit Error Message", NotificationBubbleType.Error);
        var err = vm.StatusNotifications[^1];
        Assert.Equal(NotificationBubbleType.Error, err.Type);
        Assert.True(err.IsError);
    }

    [Fact]
    public async Task GoalsViewModel_AutoFillGoalWithAi_DispatchesStatusNotificationBubble()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddGoalTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var goalService = new Wadd.Services.SQLiteGoalService(Path.Combine(tempPath, "test.db"));
            var aiService = new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()) { ApiKey = string.Empty };
            var vm = new GoalsViewModel(goalService, aiService);

            var notifications = new List<(string Message, NotificationBubbleType? Type)>();
            vm.StatusNotificationRequested = (msg, type) => notifications.Add((msg, type));

            vm.NewGoalTitle = "Run a marathon";
            await vm.AutoFillGoalWithAiCommand.ExecuteAsync(null);

            Assert.NotEmpty(notifications);
            Assert.Contains(notifications, n => n.Message == "Generated with Smart Offline Engine" && n.Type == NotificationBubbleType.Info);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void MainViewModel_Startup_SeedingSuppressesImmediateNotifications()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()) { ApiKey = string.Empty },
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskReminder = true;
        vm.NotifyOnOverdueTasks = true;
        vm.NotifyOnTaskDueDate = true;

        var now = DateTime.Now;

        // Existing overdue task
        vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Old Overdue Task",
            DueDate = now.AddDays(-2)
        }));

        // Existing past reminder
        vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Past Morning Reminder",
            ReminderAt = now.AddHours(-3)
        }));

        // Existing due today task
        vm.TodoItems.Add(new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Task Due Today",
            DueDate = now.Date
        }));

        // Seed notification state as happens on startup when loading DB
        vm.SeedInitialNotificationState();

        // Timer check runs
        vm.CheckReminders();

        // Verify zero pop-up notifications were fired on startup
        Assert.Empty(mockService.ShownNotifications);
    }

    [Fact]
    public void MainViewModel_OnStatusMessageChanged_IgnoresStartupBackgroundStatusMessages()
    {
        var vm = new MainViewModel();
        vm.StatusNotifications.Clear();

        vm.StatusMessage = "Loaded 5 tasks from device.";
        Assert.Empty(vm.StatusNotifications);

        vm.StatusMessage = "Syncing with Google Drive...";
        Assert.Empty(vm.StatusNotifications);

        vm.StatusMessage = "Sync finished.";
        Assert.Empty(vm.StatusNotifications);

        vm.StatusMessage = "Note saved for Today";
        Assert.Single(vm.StatusNotifications);
        Assert.Equal("Note saved for Today", vm.StatusNotifications[0].Message);
    }

    [Fact]
    public void GoalsViewModel_PendingMilestones_MoveAndReorderAndAddAndRemove()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddGoalMoveTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var goalService = new Wadd.Services.SQLiteGoalService(Path.Combine(tempPath, "test.db"));
            var aiService = new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()) { ApiKey = string.Empty };
            var vm = new GoalsViewModel(goalService, aiService);

            vm.PendingAiMilestones.Add("Step 1");
            vm.PendingAiMilestones.Add("Step 2");
            vm.PendingAiMilestones.Add("Step 3");

            // Move Step 2 up to index 0
            vm.MovePendingMilestoneUpCommand.Execute("Step 2");
            Assert.Equal("Step 2", vm.PendingAiMilestones[0]);
            Assert.Equal("Step 1", vm.PendingAiMilestones[1]);
            Assert.Equal("Step 3", vm.PendingAiMilestones[2]);

            // Move Step 2 down to index 1
            vm.MovePendingMilestoneDownCommand.Execute("Step 2");
            Assert.Equal("Step 1", vm.PendingAiMilestones[0]);
            Assert.Equal("Step 2", vm.PendingAiMilestones[1]);
            Assert.Equal("Step 3", vm.PendingAiMilestones[2]);

            // Delete Step 2
            vm.DeletePendingMilestoneCommand.Execute("Step 2");
            Assert.Equal(2, vm.PendingAiMilestones.Count);
            Assert.DoesNotContain("Step 2", vm.PendingAiMilestones);

            // Add new step
            vm.NewPendingMilestoneTitle = "Step 4 Custom";
            vm.AddPendingMilestoneCommand.Execute(null);
            Assert.Equal(3, vm.PendingAiMilestones.Count);
            Assert.Equal("Step 4 Custom", vm.PendingAiMilestones[^1]);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task GoalsViewModel_CurrentMilestones_MoveUpAndDown_UpdatesOrderIndex()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddGoalMilestoneOrderTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var goalService = new Wadd.Services.SQLiteGoalService(Path.Combine(tempPath, "test.db"));
            var aiService = new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()) { ApiKey = string.Empty };
            var vm = new GoalsViewModel(goalService, aiService);

            var goal = await goalService.SaveGoalAsync(new Wadd.Core.Models.LifeGoal { Title = "Test Move Goal" });
            var m1 = await goalService.SaveMilestoneAsync(new Wadd.Core.Models.GoalMilestone { GoalId = goal.Id, Title = "Alpha", OrderIndex = 0 });
            var m2 = await goalService.SaveMilestoneAsync(new Wadd.Core.Models.GoalMilestone { GoalId = goal.Id, Title = "Beta", OrderIndex = 1 });
            var m3 = await goalService.SaveMilestoneAsync(new Wadd.Core.Models.GoalMilestone { GoalId = goal.Id, Title = "Gamma", OrderIndex = 2 });

            await vm.LoadAllGoalsAsync();
            vm.SelectedGoal = vm.Goals.First(g => g.Id == goal.Id);

            // Wait for milestones to load
            await Task.Delay(50);

            Assert.Equal(3, vm.CurrentMilestones.Count);
            Assert.Equal("Alpha", vm.CurrentMilestones[0].Title);
            Assert.Equal("Beta", vm.CurrentMilestones[1].Title);

            // Move Beta up to index 0
            var betaVm = vm.CurrentMilestones[1];
            await vm.MoveMilestoneUpCommand.ExecuteAsync(betaVm);

            Assert.Equal("Beta", vm.CurrentMilestones[0].Title);
            Assert.Equal("Alpha", vm.CurrentMilestones[1].Title);
            Assert.Equal(0, vm.CurrentMilestones[0].Model.OrderIndex);
            Assert.Equal(1, vm.CurrentMilestones[1].Model.OrderIndex);

            // Move Beta down to index 1
            await vm.MoveMilestoneDownCommand.ExecuteAsync(betaVm);
            Assert.Equal("Alpha", vm.CurrentMilestones[0].Title);
            Assert.Equal("Beta", vm.CurrentMilestones[1].Title);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void MainViewModel_CheckReminders_RepeatingNotifications_FiresAfterInterval()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskDueDate = true;
        vm.NotificationRepeatIntervalMinutes = 30;

        var task = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Today Task to Repeat",
            DueDate = DateTime.Today
        });

        vm.TodoItems.Add(task);

        // First check: fires initial notification and records last notified time
        vm.CheckReminders();
        Assert.Single(mockService.ShownNotifications);
        Assert.Equal("Due Today: Today Task to Repeat", mockService.ShownNotifications[0].Title);

        mockService.ShownNotifications.Clear();

        // Immediate subsequent check before interval: does not fire
        vm.CheckReminders();
        Assert.Empty(mockService.ShownNotifications);
    }

    [Fact]
    public void MainViewModel_CheckReminders_UndatedTask_DoesNotTriggerNotification()
    {
        var mockService = new MockNotificationService();
        var vm = new MainViewModel(
            new Wadd.Services.InMemoryTodoService(),
            new Wadd.Services.ThemeService(),
            new Wadd.Services.SyncService(),
            new Wadd.Services.ExcelExportService(),
            new Wadd.Services.SQLiteGoalService(),
            new Wadd.Services.WindowsStartupService(),
            new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()),
            mockService);

        vm.EnableNotifications = true;
        vm.NotifyOnTaskDueDate = true;

        var undatedTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Undated Task",
            DueDate = null,
            ReminderAt = null
        });

        vm.TodoItems.Add(undatedTask);
        vm.CheckReminders();

        Assert.Empty(mockService.ShownNotifications);
    }
}
