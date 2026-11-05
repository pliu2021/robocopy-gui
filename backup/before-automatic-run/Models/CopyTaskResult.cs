namespace MF4CopyStudio.Models;

/// <summary>
/// Result of a copy task for a single folder
/// </summary>
public class CopyTaskResult
{
    public string FolderName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int FilesCopied { get; set; }
    public long BytesCopied { get; set; }
}
