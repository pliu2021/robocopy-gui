using System.IO;
using MF4CopyStudio.Models;

namespace MF4CopyStudio.Services;

/// <summary>
/// Scans source directory for folders matching keywords
/// </summary>
public class FolderScanService
{
    private readonly CancellationToken _cancellationToken;
    
    public FolderScanService(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }
    
    /// <summary>
    /// Scans source directory for matching folders
    /// </summary>
    public async Task<List<FolderScanResult>> ScanAsync(
        string sourcePath, 
        string keyword, 
        string extension,
        IProgress<double>? progress = null)
    {
        var results = new List<FolderScanResult>();
        
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
        
        var keywords = keyword.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToArray();
        
        var extensions = extension.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToArray();
        
        if (keywords.Length == 0) keywords = new[] { "MT_RE" };
        if (extensions.Length == 0) extensions = new[] { ".MF4" };
        
        var subDirs = Directory.GetDirectories(sourcePath);
        var totalDirs = subDirs.Length;
        var processed = 0;
        
        foreach (var dir in subDirs)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            
            var folderName = Path.GetFileName(dir);
            var matchFound = keywords.Any(k => 
                folderName.Contains(k, StringComparison.OrdinalIgnoreCase));
            
            if (matchFound)
            {
                try
                {
                    var mf4Files = FindMf4Files(dir, extensions, _cancellationToken);
                    var totalBytes = mf4Files.Sum(f => f.Length);
                    
                    if (mf4Files.Count > 0)
                    {
                        results.Add(new FolderScanResult
                        {
                            FolderName = folderName,
                            FullPath = dir,
                            FileCount = mf4Files.Count,
                            TotalBytes = totalBytes,
                            IsAccessible = true
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    results.Add(new FolderScanResult
                    {
                        FolderName = folderName,
                        FullPath = dir,
                        FileCount = 0,
                        TotalBytes = 0,
                        IsAccessible = false,
                        WarningMessage = "Access denied"
                    });
                }
                catch (Exception)
                {
                    results.Add(new FolderScanResult
                    {
                        FolderName = folderName,
                        FullPath = dir,
                        FileCount = 0,
                        TotalBytes = 0,
                        IsAccessible = false,
                        WarningMessage = "Cannot access folder"
                    });
                }
            }
            
            processed++;
            progress?.Report((double)processed / totalDirs * 100);
        }
        
        return results;
    }
    
    private static List<FileInfo> FindMf4Files(string directory, string[] extensions, CancellationToken ct)
    {
        var files = new List<FileInfo>();
        
        try
        {
            var di = new DirectoryInfo(directory);
            foreach (var ext in extensions)
            {
                var searchPattern = "*" + ext;
                files.AddRange(di.EnumerateFiles(searchPattern, SearchOption.AllDirectories)
                    .Where(f => f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch
        {
            // Silently skip inaccessible directories
        }
        
        return files;
    }
}
