using System.Threading;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MF4CopyStudio.Models;
using MF4CopyStudio.Services;

namespace MF4CopyStudio.ViewModels;

public enum RunTrigger { Manual, Automatic }

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _copyCts;
    private CancellationTokenSource? _autoRunDelayCts;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private bool _hasCompletedManualRunThisSession;
    private bool _disposed;
    private const int MaxLogEntries = 2000;
    private const string MF4FilePattern = "*.MF4";
    private static readonly TimeSpan AutomaticRunInterval = TimeSpan.FromMinutes(10);
    
    // Fixed keyword list (14 keywords)
    private static readonly string[] AllKeywords = new[]
    {
        "CV_DRIVING_DEBUG", "IPN_IPC", "MRR40_DEBUG", "SRR50_DEBUG",
        "FAS_DLT_LOW", "IPN_DEBUG", "BN_RADETH", "IPBETH",
        "BN_CALIFR", "BN_IUKETH", "IPN_INT_COM", "MT_RE",
        "MRR40_RAW", "SRR50_RAW"
    };

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private string _destinationPath = "";
    [ObservableProperty] private ObservableCollection<KeywordOption> _keywordOptions = new();
    [ObservableProperty] private int _threadCount = 16;
    [ObservableProperty] private int _foldersFound;
    [ObservableProperty] private int _filesFound;
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private string _startButtonText = "START";
    [ObservableProperty] private bool _isCopying;
    [ObservableProperty] private bool _isStartEnabled = true;
    [ObservableProperty] private ObservableCollection<LogEntry> _activityLog = new();
    [ObservableProperty] private CopySummary _copySummary = new();
    
    // Progress tracking
    private CopyProgressInfo _progressInfo = new();
    private readonly HashSet<string> _processedFileKeys = new(StringComparer.OrdinalIgnoreCase);
    private double _lastRawProgress = 0;
    private bool _isProgressAnimating = false;
    
    [ObservableProperty] private bool _isProgressVisible = false;
    [ObservableProperty] private bool _isProgressIndeterminate = false;
    [ObservableProperty] private string _progressStatusText = "Ready";
    [ObservableProperty] private string _progressPercentageText = "0%";
    [ObservableProperty] private string _progressFileCountText = "No new files";
    [ObservableProperty] private string _progressCurrentItemText = "—";
    [ObservableProperty] private double _progressValue = 0;
    [ObservableProperty] private string _progressTriggerText = "Manual";
    [ObservableProperty] private bool _isCopyCompleted = false;
    [ObservableProperty] private bool _isCopyFailed = false;
    [ObservableProperty] private bool _isCopyCancelled = false;
    
    // Automatic Run properties
    [ObservableProperty] private bool _isAutomaticRunEnabled;
    [ObservableProperty] private string _automaticRunStatusText = "Off";
    [ObservableProperty] private string _automaticRunStatusShortText = "Off";
    [ObservableProperty] private string _automaticRunStatusToolTip = "Automatic Run is disabled.";
    [ObservableProperty] private string _nextRunText = "--";
    [ObservableProperty] private string _currentStatusText = "Ready";
    [ObservableProperty] private string _sourceStatusText = "Not configured";
    [ObservableProperty] private string _destinationStatusText = "Not configured";
    [ObservableProperty] private string _selectedKeywordsSummary = "MT_RE";
    [ObservableProperty] private bool _isAutoScrollEnabled = true;
    [ObservableProperty] private bool _isLogPanelExpanded = true;
    public IAsyncRelayCommand ShowAboutCommand { get; set; }

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = _settingsService.LoadSettings();
        SourcePath = settings.SourcePath;
        DestinationPath = settings.DestinationPath;
        ThreadCount = settings.ThreadCount is 8 or 16 or 32 ? settings.ThreadCount : 16;
        IsAutomaticRunEnabled = settings.AutomaticRunEnabled;
        
        foreach (var kw in AllKeywords)
        {
            KeywordOptions.Add(new KeywordOption { Name = kw, IsSelected = settings.SelectedKeywords?.Contains(kw) == true });
        }
        
        if (KeywordOptions.All(k => !k.IsSelected))
        {
            var mtRe = KeywordOptions.FirstOrDefault(k => k.Name == "MT_RE");
            if (mtRe != null) mtRe.IsSelected = true;
        }
        
        UpdateSelectedKeywordsDisplay();
        AddLog("INFO", "Application started.");
        
        // Subscribe to individual keyword option changes for real-time Task Overview updates
        foreach (var kw in KeywordOptions)
        {
            kw.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(KeywordOption.IsSelected))
                {
                    UpdateSelectedKeywordsDisplay();
                    StartCopyCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

    partial void OnIsAutomaticRunEnabledChanged(bool value)
    {
        var settings = _settingsService.LoadSettings();
        settings.AutomaticRunEnabled = value;
        _settingsService.SaveSettings(settings);
        
        if (!value)
        {
            _autoRunDelayCts?.Cancel();
            AutomaticRunStatusText = "Off";
            AutomaticRunStatusShortText = "Off";
            AutomaticRunStatusToolTip = "Automatic Run is disabled.";
            NextRunText = "--";
        }
        else if (_hasCompletedManualRunThisSession)
        {
            AutomaticRunStatusText = "Ready after first run";
            AutomaticRunStatusShortText = "Ready";
            AutomaticRunStatusToolTip = "Automatic Run is enabled and will start after the first completed manual run.";
        }
        else
        {
            AutomaticRunStatusText = "Ready after first run";
            AutomaticRunStatusShortText = "Ready";
            AutomaticRunStatusToolTip = "Automatic Run is enabled and will start after the first completed manual run.";
        }
    }

    partial void OnKeywordOptionsChanged(ObservableCollection<KeywordOption> value)
    {
        UpdateSelectedKeywordsDisplay();
        // Subscribe to future additions (though collection is typically populated once)
        foreach (var kw in value)
        {
            kw.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(KeywordOption.IsSelected))
                {
                    UpdateSelectedKeywordsDisplay();
                    StartCopyCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

    private void UpdateSelectedKeywordsDisplay()
    {
        var selected = KeywordOptions.Where(k => k.IsSelected).Select(k => k.Name).ToList();
        SelectedKeywordsSummary = selected.Count == 0 ? "None selected" : 
                                   selected.Count == 1 ? selected[0] : 
                                   $"{selected.Count} selected";
        StartCopyCommand.NotifyCanExecuteChanged();
    }
    
    // Constructor adds Application started log at end of ctor body

    [RelayCommand(CanExecute = nameof(IsStartEnabled))]
    private async Task StartCopy() => await RunCopyCycleAsync(RunTrigger.Manual);

    private async Task RunCopyCycleAsync(RunTrigger trigger)
    {
        // Concurrency protection: only one task at a time
        if (!await _runGate.WaitAsync(TimeSpan.FromMilliseconds(100)))
        {
            AddLog("WARNING", trigger == RunTrigger.Automatic 
                ? "Automatic run skipped: another task is active." 
                : "Another task is already running.");
            return;
        }

        _copyCts = new CancellationTokenSource();
        try
        {
            // Cancel any existing auto-run countdown
            _autoRunDelayCts?.Cancel();
            _autoRunDelayCts = null;
            
            IsCopying = true;
            StartButtonText = "RUNNING...";
            IsStartEnabled = false;
            AutomaticRunStatusText = "Waiting for current run";
            AutomaticRunStatusShortText = "Waiting";
            AutomaticRunStatusToolTip = "Automatic Run is waiting for the current copy task to finish.";

            var selectedKeywords = KeywordOptions.Where(k => k.IsSelected).Select(k => k.Name).ToList();
            
            AddLog("INFO", $"Starting {(trigger == RunTrigger.Automatic ? "automatic" : "manual")} copy cycle.");
            
            // Validate paths
            if (!Directory.Exists(SourcePath))
            {
                AddLog("WARNING", "Source directory is not available.");
                if (trigger == RunTrigger.Automatic)
                {
                    ScheduleNextRun();
                    ResetUI();
                    return;
                }
                MessageBox.Show($"Source directory does not exist:\n{SourcePath}", "Robocopy Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetUI();
                return;
            }

            if (!Directory.Exists(DestinationPath))
            {
                if (trigger == RunTrigger.Automatic)
                {
                    AddLog("WARNING", "Destination directory is not available.");
                    ScheduleNextRun();
                    ResetUI();
                    return;
                }
                
                var result = MessageBox.Show($"Destination directory does not exist:\n{DestinationPath}\n\nCreate it?", "Robocopy Tool", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    Directory.CreateDirectory(DestinationPath);
                else
                {
                    ResetUI();
                    return;
                }
            }

            // Find matching folders
            StartButtonText = "SCANNING...";
            AddLog("INFO", $"Searching for folders matching: {string.Join(", ", selectedKeywords)}");
            
            var matchingFolders = FindMatchingFolders(SourcePath, selectedKeywords);
            FoldersFound = matchingFolders.Count;

            if (matchingFolders.Count == 0)
            {
                AddLog("WARNING", "No folders matched the selected keywords.");
                if (trigger == RunTrigger.Automatic)
                {
                    ScheduleNextRun();
                    ResetUI();
                    return;
                }
                MessageBox.Show("No folders matched the selected keywords.", "Robocopy Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetUI();
                return;
            }

            AddLog("FOUND", $"Matched {FoldersFound} folder(s).");

            // Count MF4 files and check for NEW files only
            var mf4Files = new List<string>();
            foreach (var folder in matchingFolders)
            {
                var files = Directory.GetFiles(folder, MF4FilePattern, SearchOption.AllDirectories);
                // Filter: only files that don't exist in destination (new files)
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(folder, file);
                    var destFile = Path.Combine(DestinationPath, Path.GetFileName(folder), relativePath);
                    if (!File.Exists(destFile))
                    {
                        mf4Files.Add(file);
                    }
                }
                AddLog("FOUND", $"Found {files.Length} MF4 file(s) in {Path.GetFileName(folder)}.");
            }

            FilesFound = mf4Files.Count;
            TotalSize = mf4Files.Sum(f => new FileInfo(f).Length);
            
            if (mf4Files.Count == 0)
            {
                AddLog("INFO", "No new MF4 files found.");
                if (trigger == RunTrigger.Automatic)
                {
                    ScheduleNextRun();
                }
                ResetUI();
                return;
            }

            AddLog("INFO", $"Total: {FilesFound} new MF4 file(s), {FormatSize(TotalSize)}");

            // Execute robocopy for EACH matching folder (not root!)
            StartButtonText = "COPYING...";
            var threadArg = ThreadCount / 8;
            var robocopyBaseArgs = $"/Z /R:2 /W:2 /MT:{threadArg} /BYTES /ETA /XJ /FP /NDL /NJH /NJS /XC /XN /XO";
            
            foreach (var matchFolder in matchingFolders)
            {
                var folderName = Path.GetFileName(matchFolder);
                var destFolder = Path.Combine(DestinationPath, folderName);
                
                AddLog("INFO", $"Copying {folderName}...");
                
                // Validate: folder name must contain at least one selected keyword
                bool isValid = selectedKeywords.Any(k => folderName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!isValid)
                {
                    AddLog("ERROR", $"Blocked invalid folder: {folderName} (outside selected keywords)");
                    continue;
                }
                
                var processId = await ExecuteRobocopyForFolderAsync(matchFolder, destFolder, robocopyBaseArgs, _copyCts.Token);
                AddLog("SUCCESS", $"Folder completed: {folderName} (PID: {processId})");
            }

            CopySummary = new CopySummary
            {
                SuccessfulFolders = matchingFolders.Count,
                TotalFilesCopied = FilesFound,
                TotalBytesCopied = TotalSize
            };
            AddLog("SUCCESS", $"Copy cycle completed: {matchingFolders.Count} folder(s), {FilesFound} file(s).");

            // Mark that we've completed at least one manual run this session
            if (trigger == RunTrigger.Manual)
            {
                _hasCompletedManualRunThisSession = true;
            }

            // Schedule next automatic run if enabled
            if (IsAutomaticRunEnabled)
            {
                ScheduleNextRun();
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("WARNING", "Copy operation cancelled by user.");
            if (IsAutomaticRunEnabled)
            {
                AutomaticRunStatusText = "Paused after cancellation";
                AutomaticRunStatusShortText = "Paused";
                AutomaticRunStatusToolTip = "Automatic Run is paused after cancellation. Start a manual run to resume.";
                NextRunText = "--";
            }
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Copy failed: {ex.Message}");
            if (IsAutomaticRunEnabled && trigger == RunTrigger.Automatic)
            {
                ScheduleNextRun();
            }
        }
        finally
        {
            // Reset UI state - this was missing, causing START to stay disabled
            ResetUI();
            
            _copyCts?.Dispose();
            _copyCts = null;
        }
    }

    private void ScheduleNextRun()
    {
        if (!IsAutomaticRunEnabled) return;
        
        _autoRunDelayCts?.Cancel();
        _autoRunDelayCts = new CancellationTokenSource();
        
        AutomaticRunStatusText = "Countdown";
        var startTime = DateTime.Now;
        
        Task.Run(async () =>
        {
            try
            {
                while (!_autoRunDelayCts.Token.IsCancellationRequested)
                {
                    var elapsed = DateTime.Now - startTime;
                    var remaining = AutomaticRunInterval - elapsed;
                    
                    if (remaining <= TimeSpan.Zero)
                    {
                        AddLog("INFO", "Automatic run starting...");
                        await RunCopyCycleAsync(RunTrigger.Automatic);
                        return;
                    }
                    
                    NextRunText = $"Next run in {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                    await Task.Delay(1000, _autoRunDelayCts.Token);
                }
            }
            catch (OperationCanceledException) { }
        }, _autoRunDelayCts.Token);
    }

    [RelayCommand]
    private void CancelCopy()
    {
        if (_copyCts != null && !_copyCts.IsCancellationRequested)
        {
            AddLog("WARNING", "Cancelling copy operation...");
            _copyCts.Cancel();
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("robocopy");
                foreach (var p in procs) { try { p.Kill(); p.WaitForExit(3000); p.Dispose(); } catch { } }
            } catch { }
        }
    }

    private List<string> FindMatchingFolders(string sourcePath, List<string> keywords)
    {
        var result = new List<string>();
        if (!Directory.Exists(sourcePath)) return result;
        foreach (var keyword in keywords)
        {
            try { result.AddRange(Directory.GetDirectories(sourcePath, $"*{keyword}*")); }
            catch { }
        }
        return result;
    }

    private async Task<int> ExecuteRobocopyForFolderAsync(string sourceFolder, string destFolder, string baseArgs, CancellationToken ct)
    {
        using var proc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "robocopy.exe",
                Arguments = $"\"{sourceFolder}\" \"{destFolder}\" *.MF4 /E {baseArgs}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        // Filter output: only show friendly messages, not raw robocopy noise
        proc.OutputDataReceived += (s, e) => {
            if (!string.IsNullOrEmpty(e.Data) && !e.Data.StartsWith("100%"))
            {
                // Parse and show only meaningful lines
                var trimmed = e.Data.Trim();
                if (trimmed.StartsWith("New File") || trimmed.StartsWith("Copying"))
                {
                    var parts = trimmed.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var fileName = Path.GetFileName(parts[1].Trim());
                        AddLog("COPY", $"Copying: {fileName}");
                    }
                }
            }
        };
        
        proc.Start();
        proc.BeginOutputReadLine();
        await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(Timeout.Infinite, ct));
        return proc.Id;
    }

    private void ResetUI()
    {
        IsCopying = false;
        StartButtonText = "START";
        IsStartEnabled = true;
        if (IsAutomaticRunEnabled && _hasCompletedManualRunThisSession && _autoRunDelayCts == null)
        {
            AutomaticRunStatusText = "Countdown";
            AutomaticRunStatusShortText = "Countdown";
            AutomaticRunStatusToolTip = "Automatic Run is waiting for the next scheduled copy cycle.";
        }
    }

    private void AddLog(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
            
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;
        
        // Always marshal to UI thread to prevent cross-thread collection exceptions
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => AddLogCore(level, message), 
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        
        AddLogCore(level, message);
    }
    
    private void AddLogCore(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        while (ActivityLog.Count >= MaxLogEntries) ActivityLog.RemoveAt(0);
        ActivityLog.Add(new LogEntry { Timestamp = timestamp, Level = level, Message = message });
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0; double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1) { size /= 1024; i++; }
        return $"{size:F2} {suffixes[i]}";
    }

    partial void OnSourcePathChanged(string value)
    {
        SaveSettings();
        SourceStatusText = string.IsNullOrWhiteSpace(value) ? "Not configured" : "Configured";
        StartCopyCommand.NotifyCanExecuteChanged();
    }
    
    partial void OnDestinationPathChanged(string value)
    {
        SaveSettings();
        DestinationStatusText = string.IsNullOrWhiteSpace(value) ? "Not configured" : "Configured";
        StartCopyCommand.NotifyCanExecuteChanged();
    }
    partial void OnThreadCountChanged(int value) { SaveSettings(); }

    public void SaveSettings()
    {
        var settings = _settingsService.LoadSettings();
        settings.SourcePath = SourcePath;
        settings.DestinationPath = DestinationPath;
        settings.ThreadCount = ThreadCount;
        settings.SelectedKeywords = KeywordOptions.Where(k => k.IsSelected).Select(k => k.Name).ToList();
        settings.AutomaticRunEnabled = IsAutomaticRunEnabled;
        _settingsService.SaveSettings(settings);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _copyCts?.Cancel();
        _copyCts?.Dispose();
        _autoRunDelayCts?.Cancel();
        _autoRunDelayCts?.Dispose();
        _runGate.Dispose();
        _disposed = true;
    }

    
    [RelayCommand]
    private void ClearLogs()
    {
        ActivityLog.Clear();
    }
    
    /// <summary>
    /// 阶段加权进度计算
    /// Validating: 0-5%, Scanning: 5-15%, Copying: 15-98%, Finalizing: 98-100%
    /// </summary>
    private void UpdateProgress(string status, int processedFiles, int totalFiles, string? currentFile = null)
    {
        _progressInfo.Status = status;
        _progressInfo.CurrentFileName = currentFile;
        
        // Show progress during any active task state AND terminal states
        if (status.ToLower() is "validating" or "scanning" or "preparing" or "copying" or "finalizing" or "cancelling" or "completed" or "cancelled" or "failed" or "up to date")
        {
            IsProgressVisible = true;
        }
        
        double rawProgress;
        
        switch (status.ToLower())
        {
            case "validating":
                rawProgress = 2.5; // Fixed point in validating phase
                break;
                
            case "scanning":
                // Use indeterminate during scanning if we don't have total yet
                if (totalFiles <= 0)
                {
                    IsProgressIndeterminate = true;
                    rawProgress = 5;
                }
                else
                {
                    IsProgressIndeterminate = false;
                    // Map scanning to 5-15% range
                    // Estimate based on folders scanned vs total folders
                    rawProgress = 5 + (_progressInfo.CompletedFolders / Math.Max(_progressInfo.TotalFolders, 1)) * 10;
                }
                break;
                
            case "copying":
                IsProgressIndeterminate = false;
                if (totalFiles > 0)
                {
                    // Ensure processed doesn't exceed total
                    int effectiveProcessed = Math.Min(processedFiles, totalFiles);
                    double copyPhaseProgress = (double)effectiveProcessed / totalFiles;
                    // Map copying phase to 15-98% range
                    rawProgress = 15 + copyPhaseProgress * 83;
                }
                else
                {
                    // No files to copy - already done
                    rawProgress = 100;
                }
                break;
                
            case "finalizing":
                rawProgress = 98;
                break;
                
            case "completed":
                rawProgress = 100;
                break;
                
            case "cancelled":
                // Keep current progress, don't reset
                rawProgress = _lastRawProgress;
                break;
                
            case "failed":
                rawProgress = _lastRawProgress;
                break;
                
            default:
                rawProgress = 0;
                break;
        }
        
        // Monotonic protection - never go backwards
        if (rawProgress < _lastRawProgress && status != "cancelled" && status != "failed")
        {
            rawProgress = _lastRawProgress;
        }
        
        // Clamp to 0-100
        rawProgress = Math.Max(0, Math.Min(100, rawProgress));
        
        _progressInfo.RawProgressValue = rawProgress;
        _lastRawProgress = rawProgress;
        
        // Update display properties
        ProgressStatusText = status;
        ProgressPercentageText = $"{(int)Math.Round(rawProgress)}%";
        ProgressValue = rawProgress; // This will trigger smooth animation
        
        if (!string.IsNullOrEmpty(currentFile))
        {
            ProgressCurrentItemText = currentFile;
        }
    }
    
    /// <summary>
    /// 安全地增加已处理文件计数（去重）
    /// </summary>
    private bool TryMarkFileAsProcessed(string folderName, string relativePath, bool wasCopied = false, bool wasSkipped = false, bool wasFailed = false)
    {
        string key = $@"{folderName}\{relativePath}";
        
        if (_processedFileKeys.Contains(key))
            return false; // Already counted
        
        _processedFileKeys.Add(key);
        _progressInfo.ProcessedFiles++;
        
        if (wasCopied) _progressInfo.CopiedFiles++;
        if (wasSkipped) _progressInfo.SkippedFiles++;
        if (wasFailed) _progressInfo.FailedFiles++;
        
        return true;
    }
    
    /// <summary>
    /// 重置进度跟踪（新任务开始时调用）
    /// </summary>
    private void ResetProgress()
    {
        _processedFileKeys.Clear();
        _progressInfo = new CopyProgressInfo();
        _lastRawProgress = 0;
        ProgressValue = 0;
        ProgressPercentageText = "0%";
        ProgressFileCountText = "No new files";
        ProgressCurrentItemText = "—";
        IsProgressIndeterminate = false;
        // Don't set IsProgressVisible = false here - let UpdateProgress control it
    }
    

    public void ToggleLogPanel()
    {
        IsLogPanelExpanded = !IsLogPanelExpanded;
    }
    
    

}
