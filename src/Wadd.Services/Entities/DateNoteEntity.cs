using System;
using SQLite;

namespace Wadd.Services.Entities;

[Table("DateNotes")]
public class DateNoteEntity
{
    [PrimaryKey]
    public string DateKey { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
