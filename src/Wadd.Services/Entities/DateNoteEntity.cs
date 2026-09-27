using System;
using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("DateNotes")]
public class DateNoteEntity
{
    [PrimaryKey]
    public string DateKey { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public CalendarDateNote ToDomain()
    {
        return new CalendarDateNote
        {
            DateKey = DateKey,
            NoteText = NoteText,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt
        };
    }

    public static DateNoteEntity FromDomain(CalendarDateNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new DateNoteEntity
        {
            DateKey = note.DateKey,
            NoteText = note.NoteText,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt,
            IsDeleted = note.IsDeleted,
            DeletedAt = note.DeletedAt
        };
    }
}
