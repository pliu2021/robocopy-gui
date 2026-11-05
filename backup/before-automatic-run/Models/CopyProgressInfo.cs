namespace MF4CopyStudio.Models;

/// <summary>
/// Real-time copy progress information
/// </summary>
public class CopyProgressInfo
{
    public int CurrentFolderIndex { get; set; }
    public int TotalFolders { get; set; }
    public string CurrentFolderName { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public long CompletedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int CompletedFileCount { get; set; }
    public int TotalFileCount { get; set; }
    public double SpeedBytesPerSecond { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedRemaining { get; set; }
    public double ProgressPercent => TotalBytes > 0 ? (double)CompletedBytes / TotalBytes * 100 : 0;
    
    public string SpeedFormatted => FormatSpeed(SpeedBytesPerSecond);
    public string EtaFormatted => EstimatedRemaining.HasValue ? FormatTime(EstimatedRemaining.Value) : "Estimating...";
    
    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F0} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s";
    }
    
    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return $"~{(int)time.TotalSeconds}s";
        if (time.TotalHours < 1) return $"~{(int)time.TotalMinutes}m";
        return $"~{(int)time.TotalHours}h {(int)time.Minutes}m";
    }
}
