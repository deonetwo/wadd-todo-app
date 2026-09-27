using SQLite;

namespace Wadd.Services.Entities;

public class CustomTagEntity
{
    [PrimaryKey]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
