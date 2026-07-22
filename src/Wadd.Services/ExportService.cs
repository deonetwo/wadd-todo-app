using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class ExportService : IExportService
{
    public Task<byte[]> ExportToExcelAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        // Placeholder for Excel generation logic
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task ExportToExcelFileAsync(string filePath, IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        // Placeholder for file writing logic
        return Task.CompletedTask;
    }
}
