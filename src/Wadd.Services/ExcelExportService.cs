using MiniExcelLibs;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

/// <summary>
/// Service implementation for exporting todo items to Excel spreadsheet (.xlsx) format using MiniExcel.
/// Supports platform-safe file output for Windows and Android.
/// </summary>
public class ExcelExportService : IExportService
{
    /// <summary>
    /// Exports a collection of <see cref="TodoItem"/> to an Excel file (.xlsx) at the specified file path.
    /// Formats columns: ID, Title, Description, Status (Completed/Pending), Created Date.
    /// </summary>
    public async Task ExportToExcelAsync(IEnumerable<TodoItem> items, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Ensure target directory exists (platform-safe access for Windows and Android)
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exportData = FormatExportItems(items);

        // Clean overwrite if file exists
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await MiniExcel.SaveAsAsync(filePath, exportData, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Exports a collection of <see cref="TodoItem"/> to an Excel file (.xlsx) in-memory byte array.
    /// Formats columns: ID, Title, Description, Status (Completed/Pending), Created Date.
    /// </summary>
    public async Task<byte[]> ExportToExcelAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var exportData = FormatExportItems(items);

        using var memoryStream = new MemoryStream();
        await MiniExcel.SaveAsAsync(memoryStream, exportData, cancellationToken: cancellationToken);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Alias method to write export items to file path.
    /// </summary>
    public Task ExportToExcelFileAsync(string filePath, IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        return ExportToExcelAsync(items, filePath, cancellationToken);
    }

    /// <summary>
    /// Formats TodoItem objects into dictionary representation matching required Excel columns:
    /// ID, Title, Description, Status (Completed/Pending), Created Date.
    /// </summary>
    private static IEnumerable<Dictionary<string, object?>> FormatExportItems(IEnumerable<TodoItem> items)
    {
        return items.Select(item => new Dictionary<string, object?>
        {
            { "ID", item.Id.ToString() },
            { "Title", item.Title },
            { "Description", item.Description },
            { "Status", item.IsCompleted ? "Completed" : "Pending" },
            { "Due Date", item.DueDate?.ToString("yyyy-MM-dd") ?? "None" },
            { "Reminder", item.ReminderAt?.ToString("yyyy-MM-dd HH:mm") ?? "None" },
            { "Recurrence", Wadd.Core.Helpers.RecurrenceHelper.FormatRecurrenceText(item.IsRecurring, item.RecurrenceType, item.CustomRecurrenceInterval, item.CustomRecurrenceUnit, item.CustomWeeklyDays) },
            { "Category", string.IsNullOrWhiteSpace(item.Category) ? "Uncategorized" : item.Category },
            { "Created Date", item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") }
        });
    }
}
