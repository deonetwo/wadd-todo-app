using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for exporting todo items to formats like Excel.
/// </summary>
public interface IExportService
{
    Task<byte[]> ExportToExcelAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default);
    Task ExportToExcelFileAsync(string filePath, IEnumerable<TodoItem> items, CancellationToken cancellationToken = default);
}
