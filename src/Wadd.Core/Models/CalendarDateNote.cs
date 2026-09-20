using System;

namespace Wadd.Core.Models;

public class CalendarDateNote
{
    public string DateKey { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }
}
