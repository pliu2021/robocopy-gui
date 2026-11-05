using System.IO;
using System.Diagnostics;
using MF4CopyStudio.Models;

namespace MF4CopyStudio.Services;

/// <summary>
/// Executes robocopy operations with progress tracking
/// </summary>
public class RobocopyService : IDisposable
{
    private Process? _currentProcess;
    private CancellationTokenSource? _processCts;
    
    /// <summary>
    /// Copies files from source to target using robocopy
    /// </summary>
    public async Task<CopyTaskResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        string extension,
        int threadCount,
        int retryCount,
        int retryWait,
        IProgress<CopyProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CopyTaskResult
        {
            FolderName = Path.GetFileName(sourcePath)
        };
        
        try
        {
            _processCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // Build argument list safely
            var arguments = new List<string>
            {
                sourcePath,
                destinationPath,
                GetFilePattern(extension),
                "/E",           // Include subdirectories
                "/Z",           // Restartable mode
                $"/R:{retryCount}",  // Retry count
                $"/W:{retryWait}",   // Wait between retries
                $"/MT:{threadCount}",// Multi-threading
                "/BYTES",       // Report sizes in bytes
                "/ETA",         // Show estimated time
                "/XJ",          // Exclude junction points
                "/FP",          // Full path in output
                "/NDL",         // No directory listing (files only)
                "/NJH",         // No job header
                "/NJS"          // No job summary
            };
            
            var psi = new ProcessStartInfo
            {
                FileName = "robocopy.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(437),
                StandardErrorEncoding = System.Text.Encoding.GetEncoding(437)
            };
            
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
            
            _currentProcess = new Process { StartInfo = psi };
            
            var outputLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var completedBytes = 0L;
            var totalBytes = 0L;
            var startTime = DateTime.UtcNow;
            
            _currentProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Enqueue(e.Data);
                    
                    // Parse for progress - look for file lines with size
                    // Format: "source\file.ext    size_in_bytes"
                    var parts = e.Data.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[^1], out var size))
                    {
                        completedBytes += size;
                        totalBytes += size;
                        
                        var elapsed = DateTime.UtcNow - startTime;
                        var speed = elapsed.TotalSeconds > 0 ? completedBytes / elapsed.TotalSeconds : 0;
                        
                        progress?.Report(new CopyProgressInfo
                        {
                            CurrentFileName = Path.GetFileName(parts[0]),
                            CompletedBytes = completedBytes,
                            TotalBytes = totalBytes,
                            SpeedBytesPerSecond = speed,
                            ElapsedTime = elapsed
                        });
                    }
                }
            };
            
            _currentProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Enqueue($"ERROR: {e.Data}");
                }
            };
            
            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();
            
            await _currentProcess.WaitForExitAsync(_processCts.Token);
            
            var exitCode = _currentProcess.ExitCode;
            
            // Robocopy exit codes: 0-7 = success/non-fatal, 8+ = failure
            result.Success = exitCode < 8;
            result.ExitCode = exitCode;
            
            if (!result.Success)
            {
                result.ErrorMessage = GetErrorMessage(exitCode);
            }
            
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.Success = false;
            result.ErrorMessage = "Copy operation was cancelled";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Copy failed: {ex.Message}";
            return result;
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
            _processCts?.Dispose();
            _processCts = null;
        }
    }
    
    private static string GetFilePattern(string extension)
    {
        var ext = extension.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return "*" + ext.ToUpperInvariant();
    }
    
    private static string GetErrorMessage(int exitCode)
    {
        return exitCode switch
        {
            >= 8 and < 16 => "Some files could not be copied (access denied or not found)",
            >= 16 => "Serious error - source or target directory problem",
            _ => "Unknown error"
        };
    }
    
    /// <summary>
    /// Cancels the current copy operation
    /// </summary>
    public void Cancel()
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try
            {
                _currentProcess.Kill(entireProcessTree: true);
                _currentProcess.WaitForExit(5000);
            }
            catch
            {
                // Process may already be exited
            }
        }
        
        _processCts?.Cancel();
    }
    
    public void Dispose()
    {
        Cancel();
        _currentProcess?.Dispose();
        _processCts?.Dispose();
    }
}
