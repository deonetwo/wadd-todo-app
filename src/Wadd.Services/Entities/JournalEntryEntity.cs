using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("JournalEntry")]
public class JournalEntryEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string? GoalId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime EntryDate { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [Indexed]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public JournalEntry ToDomain()
    {
        return new JournalEntry
        {
            Id = Id,
            GoalId = GoalId,
            Title = Title,
            Content = Content,
            EntryDate = EntryDate,
            UpdatedAt = UpdatedAt,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt
        };
    }

    public static JournalEntryEntity FromDomain(JournalEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new JournalEntryEntity
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
            GoalId = item.GoalId,
            Title = item.Title,
            Content = item.Content,
            EntryDate = item.EntryDate == default ? DateTime.UtcNow : item.EntryDate,
            UpdatedAt = item.UpdatedAt,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt
        };
    }
}
