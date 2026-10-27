namespace MF4CopyStudio.Models;

/// <summary>
/// Result of scanning a single folder for MF4 files
/// </summary>
public class FolderScanResult
{
    public string FolderName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public bool IsAccessible { get; init; } = true;
    public string? WarningMessage { get; init; }
    public bool IsSelected { get; set; } = true;
    
    public string SizeFormatted => FormatSize(TotalBytes);
    
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
