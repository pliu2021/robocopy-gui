namespace MF4CopyStudio.Models;

/// <summary>
/// Summary of completed copy operation
/// </summary>
public class CopySummary
{
    public int TotalFolders { get; set; }
    public int SuccessfulFolders { get; set; }
    public int FailedFolders { get; set; }
    public int SkippedFolders { get; set; }
    public int TotalFilesCopied { get; set; }
    public long TotalBytesCopied { get; set; }
    public TimeSpan TotalElapsedTime { get; set; }
    public bool IsCompleteSuccess => FailedFolders == 0 && SkippedFolders == 0;
    public bool HasFailures => FailedFolders > 0;
    
    // Aliases for backward compatibility with MainViewModel
    public int CopiedFiles => TotalFilesCopied;
    public int SkippedFiles => SkippedFolders;
    public int FailedFiles => FailedFolders;
    
    public string StatusMessage
    {
        get
        {
            if (IsCompleteSuccess) return "All files copied successfully";
            if (HasFailures) return "Some files could not be copied";
            return "Copy completed with skipped items";
        }
    }
}
