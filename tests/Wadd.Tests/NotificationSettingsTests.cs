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

    [Fact]
    public void NotificationTagHelper_GetNotificationId_IsDeterministicAcrossPostAndCancel()
    {
        var taskId = Guid.NewGuid();
        var tag = taskId.ToString();

        var idAtPost = NotificationTagHelper.GetNotificationId(tag);
        var idAtCancel = NotificationTagHelper.GetNotificationId(tag);

        Assert.Equal(taskId.GetHashCode(), idAtPost);
        Assert.Equal(idAtPost, idAtCancel);

        var summaryId1 = NotificationTagHelper.GetNotificationId("tasks-summary");
        var summaryId2 = NotificationTagHelper.GetNotificationId("TASKS-SUMMARY");
        Assert.Equal(NotificationTagHelper.TasksSummaryNotificationId, summaryId1);
        Assert.Equal(NotificationTagHelper.TasksSummaryNotificationId, summaryId2);
        Assert.Equal(90001, summaryId1);
    }

    [Theory]
    [InlineData(true, false, false, false)] // Android 33+, not granted, request denied -> false
    [InlineData(true, false, true, true)]   // Android 33+, not granted, request accepted -> true
    [InlineData(true, true, false, true)]   // Android 33+, already granted -> true (no request needed)
    [InlineData(false, false, false, true)] // Pre-Android 33 -> true (no request needed)
    public async Task AndroidNotificationPermissionGate_EvaluatesCorrectly(
        bool isAndroid33OrHigher,
        bool isPermissionGranted,
        bool requestResult,
        bool expectedShouldProceed)
    {
        bool requestInvoked = false;
        Func<Task<bool>> requestFunc = () =>
        {
            requestInvoked = true;
            return Task.FromResult(requestResult);
        };

        var actual = await Wadd.Services.AndroidNotificationPermissionGate.ShouldProceedWithNotificationAsync(
            isAndroid33OrHigher,
            isPermissionGranted,
            requestFunc);

        Assert.Equal(expectedShouldProceed, actual);

        if (isAndroid33OrHigher && !isPermissionGranted)
        {
            Assert.True(requestInvoked);
        }
        else
        {
            Assert.False(requestInvoked);
        }
    }

    [Fact]
    public void AndroidNotificationChannelHelper_BuildChannelId_ProducesUniqueIdFor4Combinations_IgnoringSoundSetting()
    {
        var booleans = new[] { false, true };
        var generatedIds = new HashSet<string>();

        foreach (var highPriority in booleans)
        {
            foreach (var vibration in booleans)
            {
                // Channel ID should be identical regardless of PlayNotificationSound setting
                var settingsSoundOn = new AppSettingsData
                {
                    PlayNotificationSound = true,
                    AndroidHighPriorityChannel = highPriority,
                    AndroidVibration = vibration
                };

                var settingsSoundOff = new AppSettingsData
                {
                    PlayNotificationSound = false,
                    AndroidHighPriorityChannel = highPriority,
                    AndroidVibration = vibration
                };

                var idSoundOn = AndroidNotificationChannelHelper.BuildChannelId(settingsSoundOn);
                var idSoundOff = AndroidNotificationChannelHelper.BuildChannelId(settingsSoundOff);

                // Sound toggle must NOT change the channel ID
                Assert.Equal(idSoundOn, idSoundOff);
                Assert.StartsWith("wadd_task_reminders_v3_", idSoundOn);

                generatedIds.Add(idSoundOn);
            }
        }

        // Exactly 4 combinations for (highPriority, vibration)
        Assert.Equal(4, generatedIds.Count);
    }

    private class MockNotificationChannelManager : IAndroidNotificationChannelManager
    {
        public List<string> ExistingChannelIds { get; } = new();
        public List<string> DeletedChannelIds { get; } = new();
        public List<string> CreatedChannelIds { get; } = new();

        public IEnumerable<string> GetNotificationChannelIds() => ExistingChannelIds;

        public void DeleteNotificationChannel(string channelId)
        {
            DeletedChannelIds.Add(channelId);
            ExistingChannelIds.Remove(channelId);
        }

        public void CreateNotificationChannel(string channelId, string channelName, string channelDesc, bool highPriority, bool vibration)
        {
            CreatedChannelIds.Add(channelId);
            if (!ExistingChannelIds.Contains(channelId))
            {
                ExistingChannelIds.Add(channelId);
            }
        }
    }

    [Fact]
    public void AndroidNotificationChannelHelper_SyncChannels_DeletesMismatchedStaleChannelAndDoesNotCreateSilentChannel()
    {
        var mockManager = new MockNotificationChannelManager();

        // Combination 1: highPriority=true, vibration=true -> "wadd_task_reminders_v3_h1_v1"
        var combo1Settings = new AppSettingsData
        {
            PlayNotificationSound = true,
            AndroidHighPriorityChannel = true,
            AndroidVibration = true
        };
        var channelId1 = AndroidNotificationChannelHelper.BuildChannelId(combo1Settings);

        // Combination 2: highPriority=false, vibration=false -> "wadd_task_reminders_v3_h0_v0"
        var combo2Settings = new AppSettingsData
        {
            PlayNotificationSound = false,
            AndroidHighPriorityChannel = false,
            AndroidVibration = false
        };
        var channelId2 = AndroidNotificationChannelHelper.BuildChannelId(combo2Settings);

        // Pre-populate mock manager with channel IDs from 2 different combinations + legacy channels
        mockManager.ExistingChannelIds.Add(channelId1);
        mockManager.ExistingChannelIds.Add(channelId2);
        mockManager.ExistingChannelIds.Add("wadd_task_reminders");
        mockManager.ExistingChannelIds.Add("wadd_task_reminders_v2");
        mockManager.ExistingChannelIds.Add("wadd_task_reminders_v2_silent");

        // Execute channel sync with combo1 settings active
        AndroidNotificationChannelHelper.SyncChannels(mockManager, combo1Settings);

        // channelId2 (mismatched combination) and legacy channels must be deleted
        Assert.Contains(channelId2, mockManager.DeletedChannelIds);
        Assert.Contains("wadd_task_reminders", mockManager.DeletedChannelIds);
        Assert.Contains("wadd_task_reminders_v2", mockManager.DeletedChannelIds);
        Assert.Contains("wadd_task_reminders_v2_silent", mockManager.DeletedChannelIds);

        // channelId1 (matching current settings) must NOT be deleted
        Assert.DoesNotContain(channelId1, mockManager.DeletedChannelIds);

        // Target channelId1 was created or ensured, and no silent channel was created
        Assert.Contains(channelId1, mockManager.CreatedChannelIds);
        Assert.DoesNotContain("wadd_task_reminders_v2_silent", mockManager.CreatedChannelIds);
        Assert.DoesNotContain("wadd_task_reminders_v3_silent", mockManager.CreatedChannelIds);
    }

    [Fact]
    public void AudioService_NotificationSoundCalls_DoNotThrow()
    {
        IAudioService service = new Wadd.Services.AudioService();
        var ex1 = Record.Exception(() => service.PlaySound("notification.mp3"));
        var ex2 = Record.Exception(() => service.PlayNotificationSound());
        Assert.Null(ex1);
        Assert.Null(ex2);
    }

#if WINDOWS || NET10_0_WINDOWS10_0_17763_0_OR_GREATER
    [Fact]
    public void WindowsNotificationService_WhenToastDispatchThrows_CallsAlertSoundFallback()
    {
        var originalDispatcher = global::Wadd.Windows.WindowsNotificationService.ToastDispatcher;
        var originalAlertSound = global::Wadd.Windows.WindowsNotificationService.AlertSoundPlayer;

        bool alertSoundCalled = false;

        try
        {
            global::Wadd.Windows.WindowsNotificationService.ToastDispatcher = (title, msg, sound, tag) =>
            {
                throw new InvalidOperationException("Simulated WinRT failure");
            };
            global::Wadd.Windows.WindowsNotificationService.AlertSoundPlayer = () =>
            {
                alertSoundCalled = true;
            };

            global::Wadd.Windows.WindowsNotificationService.DispatchNativeWindowsToast("Test Title", "Test Message", false, "tag-1");

            Assert.True(alertSoundCalled);
        }
        finally
        {
            global::Wadd.Windows.WindowsNotificationService.ToastDispatcher = originalDispatcher;
            global::Wadd.Windows.WindowsNotificationService.AlertSoundPlayer = originalAlertSound;
        }
    }

    [Fact]
    public void WindowsNotificationService_CreateToastNotification_SetsAllRequiredProperties()
    {
        var tag = "test-task-guid-1234";
        var toast = global::Wadd.Windows.WindowsNotificationService.CreateToastNotification(
            "Task Due Soon",
            "Remember to buy milk",
            playSound: false,
            tag: tag);

        Assert.Equal(tag, toast.Tag);
        Assert.Equal("WaddTasks", toast.Group);
        Assert.NotNull(toast.ExpirationTime);
        Assert.True(toast.ExpirationTime.Value > DateTimeOffset.Now.AddDays(1.9));
        Assert.True(toast.ExpirationTime.Value < DateTimeOffset.Now.AddDays(2.1));

        var xml = toast.Content.GetXml();
        Assert.Contains("scenario=\"reminder\"", xml);
        Assert.Contains("<audio silent=\"true\"/>", xml);
        Assert.Contains("<text>Task Due Soon</text>", xml);
        Assert.Contains("<text>Remember to buy milk</text>", xml);
    }
#endif
}

