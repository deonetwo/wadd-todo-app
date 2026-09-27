using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wadd.Core.Models;
using Wadd.Services;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

public class TaskAndGoalEditModeTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SQLiteTodoService _todoService;
    private readonly SQLiteGoalService _goalService;

    public TaskAndGoalEditModeTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_edit_test_{Guid.NewGuid():N}.db");
        _todoService = new SQLiteTodoService(_testDbPath);
        _goalService = new SQLiteGoalService(_todoService.DatabaseConnection);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch { }
    }

    private MainViewModel CreateMainViewModel()
    {
        return new MainViewModel(
            _todoService,
            new ThemeService(),
            new MockSyncService(),
            new ExcelExportService(),
            _goalService,
            new WindowsStartupService(),
            new AiGoalService(new System.Net.Http.HttpClient()),
            new WindowsNotificationService(),
            new AudioService());
    }

    [Fact]
    public async Task TaskEditMode_StartEdit_PopulatesDraftsAndEntersEditMode()
    {
        var mainVm = CreateMainViewModel();
        var item = new TodoItem
        {
            Title = "Original Task",
            Description = "Original Description"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        Assert.False(mainVm.IsEditingDetailTask);
        Assert.True(mainVm.IsDetailDrawerOpen);

        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.True(mainVm.IsEditingDetailTask);
        Assert.Equal("Original Task", mainVm.EditDetailTitle);
        Assert.Equal("Original Description", mainVm.EditDetailDescription);
    }

    [Fact]
    public async Task TaskEditMode_CancelEdit_RevertsDraftsWithoutModifyingTask()
    {
        var mainVm = CreateMainViewModel();
        var item = new TodoItem
        {
            Title = "Keep Me",
            Description = "Original Desc"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);

        // Edit drafts
        mainVm.EditDetailTitle = "Changed Draft Title";
        mainVm.EditDetailDescription = "Changed Draft Desc";

        mainVm.CancelDetailTaskEditCommand.Execute(null);

        Assert.False(mainVm.IsEditingDetailTask);
        Assert.Equal("Keep Me", itemVm.Title);
        Assert.Equal("Original Desc", itemVm.Description);
    }

    [Fact]
    public async Task TaskEditMode_SaveEdit_PersistsToSqliteAndUpdatesModel()
    {
        var mainVm = CreateMainViewModel();
        var item = new TodoItem
        {
            Title = "Before Save",
            Description = "Before Desc"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);

        mainVm.EditDetailTitle = "Updated Title";
        mainVm.EditDetailDescription = "Updated Desc";

        await mainVm.SaveDetailTaskCommand.ExecuteAsync(null);

        Assert.False(mainVm.IsEditingDetailTask);
        Assert.Equal("Updated Title", itemVm.Title);
        Assert.Equal("Updated Desc", itemVm.Description);

        // Verify SQLite persistence
        var fromDb = await _todoService.GetByIdAsync(item.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Updated Title", fromDb.Title);
        Assert.Equal("Updated Desc", fromDb.Description);
    }

    [Fact]
    public async Task TaskEditMode_BackNavigation_CancelsEditModeFirst()
    {
        var mainVm = CreateMainViewModel();
        var item = new TodoItem { Title = "Task for Back Test" };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.True(mainVm.IsEditingDetailTask);
        Assert.True(mainVm.IsDetailDrawerOpen);

        // First back navigation should cancel edit mode while leaving drawer open
        bool handled1 = mainVm.HandleBackNavigation();
        Assert.True(handled1);
        Assert.False(mainVm.IsEditingDetailTask);
        Assert.True(mainVm.IsDetailDrawerOpen);

        // Second back navigation should close the drawer
        bool handled2 = mainVm.HandleBackNavigation();
        Assert.True(handled2);
        Assert.False(mainVm.IsDetailDrawerOpen);
    }

    [Fact]
    public async Task TaskEditMode_StartEdit_PopulatesAllMetadataDrafts()
    {
        var mainVm = CreateMainViewModel();
        var dueDate = DateTime.Today.AddDays(2);
        var reminder = DateTime.Today.AddDays(2).AddHours(14).AddMinutes(30);
        var item = new TodoItem
        {
            Title = "Complex Recurring Task",
            Description = "Testing metadata population",
            DueDate = dueDate,
            ReminderAt = reminder,
            Category = "Work, Urgent",
            IsRecurring = true,
            RecurrenceType = "Weekly",
            CustomRecurrenceInterval = 2,
            CustomWeeklyDays = "Mon,Wed"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.SelectedNavIndex = 2; // In Recurring Tasks view
        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);

        Assert.True(mainVm.IsEditingDetailTask);
        Assert.Equal(dueDate.Date, mainVm.EditDetailDueDate?.Date);
        Assert.Equal(reminder.Date, mainVm.EditDetailReminderDate?.Date);
        Assert.Equal(new TimeSpan(14, 30, 0), mainVm.EditDetailReminderTime);
        Assert.Equal(2, mainVm.EditDetailCategories.Count);
        Assert.Contains("Work", mainVm.EditDetailCategories);
        Assert.Contains("Urgent", mainVm.EditDetailCategories);
        Assert.Equal("Weekly", mainVm.EditDetailRecurrenceType);
        Assert.True(mainVm.IsEditDetailMondaySelected);
        Assert.True(mainVm.IsEditDetailWednesdaySelected);
        Assert.False(mainVm.IsEditDetailFridaySelected);
    }

    [Fact]
    public async Task TaskEditMode_SaveNonRecurringTask_PersistsMetadata()
    {
        var mainVm = CreateMainViewModel();
        var item = new TodoItem
        {
            Title = "Simple Non-Recurring",
            Description = "Simple desc"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);

        var newDue = DateTime.Today.AddDays(5);
        var newReminderDate = DateTime.Today.AddDays(5);
        var newReminderTime = new TimeSpan(9, 15, 0);

        mainVm.EditDetailDueDate = newDue;
        mainVm.EditDetailReminderDate = newReminderDate;
        mainVm.EditDetailReminderTime = newReminderTime;
        mainVm.EditDetailCategoryInput = "Groceries";
        mainVm.AddEditDetailCategoryCommand.Execute(null);
        mainVm.EditDetailRecurrenceType = "Daily";

        await mainVm.SaveDetailTaskCommand.ExecuteAsync(null);

        Assert.False(mainVm.IsEditingDetailTask);
        Assert.False(mainVm.IsRecurringEditPromptVisible);

        var fromDb = await _todoService.GetByIdAsync(item.Id);
        Assert.NotNull(fromDb);
        Assert.Equal(newDue.Date, fromDb.DueDate?.Date);
        Assert.Equal(newReminderDate.Date + newReminderTime, fromDb.ReminderAt);
        Assert.Equal("Groceries", fromDb.Category);
        Assert.True(fromDb.IsRecurring);
        Assert.Equal("Daily", fromDb.RecurrenceType);
    }

    [Fact]
    public async Task TaskEditMode_RecurringTask_InTasksView_CannotEdit_ShowsManageShortcut()
    {
        var mainVm = CreateMainViewModel();
        var today = DateTime.Today;
        var item = new TodoItem
        {
            Title = "Daily Habit",
            DueDate = today,
            IsRecurring = true,
            RecurrenceType = "Daily"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.SelectedNavIndex = 0; // In Tasks View (My Day/Tasks)
        mainVm.OpenDetailDrawerCommand.Execute(itemVm);

        // Edit button should be hidden; shortcut to Recurring view should be visible
        Assert.False(mainVm.CanEditSelectedDetailTask);
        Assert.True(mainVm.ShowRecurringManageShortcut);

        // Attempting to start edit mode directly must be ignored
        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.False(mainVm.IsEditingDetailTask);
    }

    [Fact]
    public async Task TaskEditMode_CompletedRecurringTask_InCompletedView_CannotEdit_ShowsManageShortcut()
    {
        var mainVm = CreateMainViewModel();
        var today = DateTime.Today;
        var parent = new TodoItem
        {
            Title = "Daily Habit",
            DueDate = today,
            IsRecurring = true,
            RecurrenceType = "Daily"
        };
        await _todoService.AddTodoAsync(parent);

        // Complete the occurrence for today
        await _todoService.ToggleCompleteAsync(parent.Id, today);

        var allTodos = await _todoService.GetTodosAsync();
        var completedInstance = allTodos.FirstOrDefault(t => t.IsCompleted && t.RecurrenceType == "Daily");
        Assert.NotNull(completedInstance);
        Assert.False(completedInstance.IsRecurring);
        Assert.True(completedInstance.SeriesId.HasValue);

        var completedVm = new TodoItemViewModel(completedInstance);
        Assert.True(completedVm.IsRecurringSeriesItem);
        Assert.True(completedVm.HasRecurrence);
        Assert.Equal("Daily", completedVm.RecurrenceFormatted);

        // In Completed View (Nav index 1)
        mainVm.SelectedNavIndex = 1;
        mainVm.OpenDetailDrawerCommand.Execute(completedVm);

        // Edit button must be hidden; manage shortcut must be visible
        Assert.False(mainVm.CanEditSelectedDetailTask);
        Assert.True(mainVm.ShowRecurringManageShortcut);

        // Attempting to edit must be strictly blocked
        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.False(mainVm.IsEditingDetailTask);

        // Click "Manage" shortcut: should jump to Repeating Tasks and focus the active parent template
        await mainVm.NavigateToRecurringTaskCommand.ExecuteAsync(null);
        Assert.Equal(2, mainVm.SelectedNavIndex);
        Assert.True(mainVm.IsRecurringView);
        Assert.NotNull(mainVm.SelectedDetailTask);
        Assert.Equal(parent.Id, mainVm.SelectedDetailTask.Id);
        Assert.True(mainVm.CanEditSelectedDetailTask);
    }

    [Fact]
    public void TaskEditMode_CompletedRecurringTask_EvenIfInRecurringView_CannotBeEdited()
    {
        var mainVm = CreateMainViewModel();
        var completedInstance = new TodoItem
        {
            Id = Guid.NewGuid(),
            SeriesId = Guid.NewGuid(),
            Title = "Completed Occurrence",
            IsCompleted = true,
            IsRecurring = false,
            RecurrenceType = "Weekly"
        };
        var vm = new TodoItemViewModel(completedInstance);

        // Even if navigation is set to Recurring view (Nav index 2)
        mainVm.SelectedNavIndex = 2;
        mainVm.OpenDetailDrawerCommand.Execute(vm);

        // Completed recurring instances must NEVER be editable
        Assert.False(mainVm.CanEditSelectedDetailTask);
        Assert.True(mainVm.ShowRecurringManageShortcut);

        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.False(mainVm.IsEditingDetailTask);
    }

    [Fact]
    public async Task TaskEditMode_RecurringTask_NavigateToRecurringCommand_EnablesEditing()
    {
        var mainVm = CreateMainViewModel();
        var today = DateTime.Today;
        var item = new TodoItem
        {
            Title = "Weekly Report",
            DueDate = today,
            IsRecurring = true,
            RecurrenceType = "Weekly"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.SelectedNavIndex = 0; // Start in Tasks view
        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        Assert.False(mainVm.CanEditSelectedDetailTask);
        Assert.True(mainVm.ShowRecurringManageShortcut);

        // Click "Manage in Repeating Tasks"
        await mainVm.NavigateToRecurringTaskCommand.ExecuteAsync(null);

        // Should switch to Recurring view (Nav index 2) and enable editing
        Assert.Equal(2, mainVm.SelectedNavIndex);
        Assert.True(mainVm.IsRecurringView);
        Assert.True(mainVm.CanEditSelectedDetailTask);
        Assert.False(mainVm.ShowRecurringManageShortcut);

        // Now starting edit mode succeeds
        mainVm.StartEditDetailTaskCommand.Execute(null);
        Assert.True(mainVm.IsEditingDetailTask);
    }

    [Fact]
    public async Task TaskEditMode_RecurringTask_SaveInRecurringView_UpdatesSeriesDirectlyWithoutPrompt()
    {
        var mainVm = CreateMainViewModel();
        var today = DateTime.Today;
        var item = new TodoItem
        {
            Title = "Monthly Budget Review",
            DueDate = today,
            IsRecurring = true,
            RecurrenceType = "Monthly"
        };
        await _todoService.AddTodoAsync(item);
        var itemVm = new TodoItemViewModel(item);

        mainVm.SelectedNavIndex = 2; // In Recurring Tasks view
        mainVm.OpenDetailDrawerCommand.Execute(itemVm);
        mainVm.StartEditDetailTaskCommand.Execute(null);

        Assert.True(mainVm.IsEditingDetailTask);

        // Change title and date in recurring view
        var newDate = today.AddDays(5);
        mainVm.EditDetailTitle = "Updated Budget Review";
        mainVm.EditDetailDueDate = newDate;

        await mainVm.SaveDetailTaskCommand.ExecuteAsync(null);

        // Saves directly without prompt modal
        Assert.False(mainVm.IsRecurringEditPromptVisible);
        Assert.False(mainVm.IsEditingDetailTask);

        var fromDb = await _todoService.GetByIdAsync(item.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Updated Budget Review", fromDb.Title);
        Assert.Equal(newDate.Date, fromDb.DueDate?.Date);
        Assert.True(fromDb.IsRecurring);
        Assert.Equal("Monthly", fromDb.RecurrenceType);
    }

    [Fact]
    public async Task GoalEditMode_StartEdit_PopulatesDraftsAndEntersEditMode()
    {
        var goalsVm = new GoalsViewModel(_goalService);
        var goal = new LifeGoal
        {
            Title = "Learn Avalonia",
            Category = "Education",
            Description = "Master MVVM in Avalonia",
            TargetDate = new DateTime(2026, 12, 31)
        };
        var saved = await _goalService.SaveGoalAsync(goal);
        await goalsVm.LoadAllGoalsAsync();
        goalsVm.SelectedGoal = goalsVm.Goals.First(g => g.Id == saved.Id);

        Assert.False(goalsVm.IsEditingGoal);

        goalsVm.StartEditGoalCommand.Execute(null);
        Assert.True(goalsVm.IsEditingGoal);
        Assert.Equal("Learn Avalonia", goalsVm.EditGoalTitle);
        Assert.Equal("Education", goalsVm.EditGoalCategory);
        Assert.Equal("Master MVVM in Avalonia", goalsVm.EditGoalDescription);
        Assert.Equal(new DateTime(2026, 12, 31), goalsVm.EditGoalTargetDate);
    }

    [Fact]
    public async Task GoalEditMode_CancelEdit_RevertsDraftsWithoutModifyingGoal()
    {
        var goalsVm = new GoalsViewModel(_goalService);
        var goal = new LifeGoal
        {
            Title = "Original Goal",
            Category = "Personal"
        };
        var saved = await _goalService.SaveGoalAsync(goal);
        await goalsVm.LoadAllGoalsAsync();
        var goalVm = goalsVm.Goals.First(g => g.Id == saved.Id);
        goalsVm.SelectedGoal = goalVm;

        goalsVm.StartEditGoalCommand.Execute(null);
        goalsVm.EditGoalTitle = "Unsaved Draft Title";
        goalsVm.EditGoalCategory = "Unsaved Category";

        goalsVm.CancelEditGoalCommand.Execute(null);

        Assert.False(goalsVm.IsEditingGoal);
        Assert.Equal("Original Goal", goalVm.Title);
        Assert.Equal("Personal", goalVm.Category);
    }

    [Fact]
    public async Task GoalEditMode_SaveEdit_PersistsToSqliteAndUpdatesModel()
    {
        var goalsVm = new GoalsViewModel(_goalService);
        var goal = new LifeGoal
        {
            Title = "Before Update",
            Category = "Work",
            Description = "Before Desc"
        };
        var saved = await _goalService.SaveGoalAsync(goal);
        await goalsVm.LoadAllGoalsAsync();
        var goalVm = goalsVm.Goals.First(g => g.Id == saved.Id);
        goalsVm.SelectedGoal = goalVm;

        goalsVm.StartEditGoalCommand.Execute(null);
        goalsVm.EditGoalTitle = "Updated Goal Title";
        goalsVm.EditGoalCategory = "Health";
        goalsVm.EditGoalDescription = "Updated Goal Desc";
        var newDate = new DateTime(2027, 1, 1);
        goalsVm.EditGoalTargetDate = newDate;

        await goalsVm.SaveGoalEditCommand.ExecuteAsync(null);

        Assert.False(goalsVm.IsEditingGoal);
        Assert.Equal("Updated Goal Title", goalVm.Title);
        Assert.Equal("Health", goalVm.Category);
        Assert.Equal("Updated Goal Desc", goalVm.Description);
        Assert.Equal(newDate, goalVm.TargetDate);

        // Verify SQLite
        var fromDb = await _goalService.GetGoalByIdAsync(saved.Id);
        Assert.NotNull(fromDb);
        Assert.Equal("Updated Goal Title", fromDb.Title);
        Assert.Equal("Health", fromDb.Category);
        Assert.Equal("Updated Goal Desc", fromDb.Description);
        Assert.Equal(newDate, fromDb.TargetDate);
    }

    [Fact]
    public async Task GoalEditMode_BackNavigation_CancelsEditModeFirst()
    {
        var goalsVm = new GoalsViewModel(_goalService);
        var goal = new LifeGoal { Title = "Goal Back Test" };
        var saved = await _goalService.SaveGoalAsync(goal);
        await goalsVm.LoadAllGoalsAsync();
        goalsVm.SelectedGoal = goalsVm.Goals.First(g => g.Id == saved.Id);
        goalsVm.IsMobileDetailViewOpen = true;

        goalsVm.StartEditGoalCommand.Execute(null);
        Assert.True(goalsVm.IsEditingGoal);

        // First back press cancels editing
        bool handled1 = goalsVm.HandleBackNavigation();
        Assert.True(handled1);
        Assert.False(goalsVm.IsEditingGoal);
        Assert.True(goalsVm.IsMobileDetailViewOpen);

        // Second back press closes mobile detail view
        bool handled2 = goalsVm.HandleBackNavigation();
        Assert.True(handled2);
        Assert.False(goalsVm.IsMobileDetailViewOpen);
    }

    [Fact]
    public async Task GoalEditMode_SelectionChange_ResetsEditMode()
    {
        var goalsVm = new GoalsViewModel(_goalService);
        var g1 = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Goal 1" });
        var g2 = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Goal 2" });
        await goalsVm.LoadAllGoalsAsync();

        goalsVm.SelectedGoal = goalsVm.Goals.First(g => g.Id == g1.Id);
        goalsVm.StartEditGoalCommand.Execute(null);
        Assert.True(goalsVm.IsEditingGoal);

        // Switching selection resets edit mode
        goalsVm.SelectedGoal = goalsVm.Goals.First(g => g.Id == g2.Id);
        Assert.False(goalsVm.IsEditingGoal);
    }
}
