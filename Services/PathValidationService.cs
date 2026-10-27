using System.IO;
using System.Text.RegularExpressions;
using MF4CopyStudio.Models;

namespace MF4CopyStudio.Services;

/// <summary>
/// Validates and compares file paths safely
/// </summary>
public class PathValidationService
{
    /// <summary>
    /// Validates that source and target paths are not the same or nested
    /// </summary>
    public static ValidationResult ValidatePaths(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new ValidationResult(false, "Source directory cannot be empty");
        
        if (string.IsNullOrWhiteSpace(destinationPath))
            return new ValidationResult(false, "Target directory cannot be empty");
        
        // Normalize paths
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destinationPath);
        
        // Check if identical
        if (normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase))
            return new ValidationResult(false, "Source and target directories cannot be the same");
        
        // Check nesting - ensure we compare full directory names, not prefixes
        if (IsSubdirectoryOf(normalizedDest, normalizedSource))
            return new ValidationResult(false, "Target directory cannot be inside source directory");
        
        if (IsSubdirectoryOf(normalizedSource, normalizedDest))
            return new ValidationResult(false, "Source directory cannot be inside target directory");
        
        return new ValidationResult(true, null);
    }
    
    /// <summary>
    /// Normalizes a path for comparison
    /// </summary>
    private static string NormalizePath(string path)
    {
        // Remove trailing slashes
        var normalized = path.TrimEnd('\\', '/');
        
        // Handle UNC paths
        if (normalized.StartsWith(@"\\"))
        {
            // Convert forward slashes to backslashes for UNC
            normalized = normalized.Replace('/', '\\');
        }
        else if (Path.IsPathRooted(normalized))
        {
            // Local path - get full path if possible
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // If can't get full path, use as-is
            }
        }
        
        return normalized;
    }
    
    /// <summary>
    /// Checks if child is a subdirectory of parent
    /// </summary>
    private static bool IsSubdirectoryOf(string child, string parent)
    {
        if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent))
            return false;
        
        // Ensure both end without separator for consistent comparison
        var childNorm = child.TrimEnd('\\', '/');
        var parentNorm = parent.TrimEnd('\\', '/');
        
        // Add separator to parent for proper prefix matching
        var parentWithSep = parentNorm + "\\";
        
        return childNorm.StartsWith(parentWithSep, StringComparison.OrdinalIgnoreCase) ||
               childNorm.StartsWith(parentWithSep.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Validates folder keyword input
    /// </summary>
    public static bool IsValidKeyword(string keyword)
    {
        return !string.IsNullOrWhiteSpace(keyword?.Trim());
    }
    
    /// <summary>
    /// Validates and normalizes file extension input
    /// </summary>
    public static string? NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;
        
        var ext = extension.Trim();
        
        // Reject path separators or command characters
        if (ext.Contains('\\') || ext.Contains('/') || ext.Contains(' ') || ext.Contains('&'))
            return null;
        
        // Auto-add dot if missing
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        
        return ext.ToUpperInvariant();
    }
}

public record ValidationResult(bool IsValid, string? ErrorMessage);
