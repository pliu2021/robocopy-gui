namespace MF4CopyStudio.Models;

/// <summary>
/// Log entry for real-time display (memory only, not persisted)
/// </summary>
public class LogEntry
{
    public string Timestamp { get; init; } = string.Empty;
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = string.Empty;
    public string? OptionalDetail { get; init; }
}
