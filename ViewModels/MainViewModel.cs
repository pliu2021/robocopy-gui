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
    
    // Animation fields - Raw and Display progress separation
    private double _rawProgressValue = 0;
    private double _displayProgressValue = 0;
    private readonly System.Windows.Threading.DispatcherTimer _progressAnimationTimer;
    private double _progressAnimationTarget = 0;
    private DateTime _lastProgressAnimationTick;
    private int _lastDisplayedPercentage = -1;
    
    // Progress fill brush (must be Brush type for binding)
    private System.Windows.Media.Brush _progressFillBrush;
    public System.Windows.Media.Brush ProgressFillBrush
    {
        get => _progressFillBrush;
        set
        {
            _progressFillBrush = value;
            OnPropertyChanged(nameof(ProgressFillBrush));
        }
    }
    
    // Display progress ratio (0.0 to 1.0) for ScaleTransform
    public double DisplayProgressRatio
    {
        get
        {
            var value = _displayProgressValue;
            
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }
            
            return Math.Clamp(value / 100.0, 0.0, 1.0);
        }
    }
    
    /// <summary>
    /// Set display progress value with smooth animation
    /// </summary>
    private void SetDisplayProgressValue(double value)
    {
        value = Math.Clamp(value, 0.0, 100.0);
        
        // Don't allow NaN or Infinity
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;
        
        // Update animation target - timer will interpolate
        _progressAnimationTarget = value;
        
        // Start timer if not running and difference is significant
        if (!_progressAnimationTimer.IsEnabled && Math.Abs(_progressAnimationTarget - _displayProgressValue) > 0.05)
        {
            _lastProgressAnimationTick = DateTime.UtcNow;
            _progressAnimationTimer.Start();
        }
    }
    
    // Shimmer visibility control
    private string _shimmerVisibility = "Collapsed";
    public string ShimmerVisibility
    {
        get => _shimmerVisibility;
        set
        {
            _shimmerVisibility = value;
            OnPropertyChanged(nameof(ShimmerVisibility));
        }
    }
    
    [ObservableProperty] private bool _isProgressVisible = false;
    [ObservableProperty] private bool _isProgressIndeterminate = false;
    [ObservableProperty] private string _progressStatusText = "Ready";
    [ObservableProperty] private string _progressPercentageText = "0%";
    [ObservableProperty] private string _progressFileCountText = "No task is running";
    [ObservableProperty] private string _progressCurrentItemText = "Configure the task and select START to begin.";
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
        
        // Initialize progress animation timer
        _progressAnimationTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _progressAnimationTimer.Tick += OnProgressAnimationTick;
        
        // Initialize progress fill brush (default blue)
        _progressFillBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xED));
        
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
            UpdateProgress("Validating", 0, 0, "Checking source and destination paths");
            StartButtonText = "SCANNING...";
            AddLog("INFO", $"Searching for folders matching: {string.Join(", ", selectedKeywords)}");
            
            var matchingFolders = FindMatchingFolders(SourcePath, selectedKeywords);
            FoldersFound = matchingFolders.Count;

            if (matchingFolders.Count == 0)
            {
                UpdateProgress("Scanning", 0, 0, "No folders found");
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
            UpdateProgress("Scanning", 0, 0, "Searching for matching MF4 files");
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
            
            // Update progress with total files found
            UpdateProgress("Preparing", 0, FilesFound, $"{FilesFound} files ready");
            
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
            
            // Apply final completion state atomically on UI dispatcher
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyCopyCycleResult(
                    CopyCycleOutcome.Completed,
                    totalFiles: FilesFound,
                    processedFiles: FilesFound,
                    copiedFiles: FilesFound,
                    failedFiles: 0,
                    totalBytes: TotalSize,
                    errorMessage: null);
            });

            // Mark that we've completed at least one manual run this session
            if (trigger == RunTrigger.Manual)
            {
                _hasCompletedManualRunThisSession = true;
            }

            // Schedule next automatic run AFTER completion state is applied
            if (IsAutomaticRunEnabled)
            {
                ScheduleNextRun();
            }
        }
        catch (OperationCanceledException)
        {
            // Apply cancelled state on UI dispatcher
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyCopyCycleResult(
                    CopyCycleOutcome.Cancelled,
                    totalFiles: FilesFound,
                    processedFiles: _progressInfo.ProcessedFiles,
                    copiedFiles: _progressInfo.CopiedFiles,
                    failedFiles: _progressInfo.FailedFiles,
                    totalBytes: TotalSize,
                    errorMessage: null);
            });
            
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
            // Apply failed state on UI dispatcher
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyCopyCycleResult(
                    CopyCycleOutcome.Failed,
                    totalFiles: FilesFound,
                    processedFiles: _progressInfo.ProcessedFiles,
                    copiedFiles: _progressInfo.CopiedFiles,
                    failedFiles: _progressInfo.FailedFiles,
                    totalBytes: TotalSize,
                    errorMessage: ex.Message);
            });
            
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
        var folderName = Path.GetFileName(sourceFolder);
        
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
        
        // Parse robocopy output for file events
        proc.OutputDataReceived += (s, e) => {
            if (string.IsNullOrEmpty(e.Data)) return;
            
            var trimmed = e.Data.Trim();
            
            // Skip non-file lines
            if (trimmed.StartsWith("100%") || trimmed.StartsWith("New Dir") || 
                trimmed.StartsWith("Bytes") || trimmed.StartsWith("Times") ||
                trimmed.StartsWith("Speed") || trimmed.StartsWith("Ended") ||
                trimmed.StartsWith("New File") || string.IsNullOrWhiteSpace(trimmed))
                return;
            
            // Check if this is a file copy line (contains .MF4 extension)
            if (trimmed.EndsWith(".MF4", StringComparison.OrdinalIgnoreCase) || 
                trimmed.EndsWith(".mf4", StringComparison.OrdinalIgnoreCase))
            {
                // Extract filename from robocopy output
                var parts = trimmed.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var filePath = parts[0].Trim();
                    var fileName = Path.GetFileName(filePath);
                    
                    if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".MF4", StringComparison.OrdinalIgnoreCase))
                    {
                        // Update progress on UI thread using GLOBAL counter
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            dispatcher.BeginInvoke(() => {
                                // Use TryMarkFileAsProcessed for deduplication
                                if (TryMarkFileAsProcessed(folderName, fileName, wasCopied: true))
                                {
                                    // Recalculate progress with updated counts
                                    UpdateProgress("Copying", _progressInfo.ProcessedFiles, FilesFound, fileName);
                                    AddLog("COPY", $"Copying: {fileName}");
                                }
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
        };
        
        proc.Start();
        proc.BeginOutputReadLine();
        
        // Start background polling for destination files as fallback
        var pollingTask = PollDestinationFilesAsync(destFolder, folderName, ct);
        
        await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(Timeout.Infinite, ct));
        
        // Wait for polling to complete (with timeout)
        try { await Task.WhenAny(pollingTask, Task.Delay(2000, ct)); } catch { }
        
        return proc.Id;
    }
    
    /// <summary>
    /// Background polling for destination files - fallback progress tracking
    /// </summary>
    private async Task PollDestinationFilesAsync(string destFolder, string folderName, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                
                // Check if destination folder exists and has MF4 files
                if (Directory.Exists(destFolder))
                {
                    var mf4Files = Directory.GetFiles(destFolder, "*.MF4", SearchOption.AllDirectories);
                    var count = mf4Files.Length;
                    
                    if (count > 0)
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            dispatcher.BeginInvoke(() => {
                                UpdateProgress("Copying", count, FilesFound, $"{count} files in {folderName}");
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog("WARNING", $"Polling error: {ex.Message}");
        }
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
        _progressAnimationTimer.Stop();
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
        // BLOCKED: Reject delayed \"copying\" updates after completion state is finalized
        var statusLower = status.ToLower();
        if (CurrentStatusText == "Completed" && statusLower == "copying")
        {
            return;
        }
        
        _progressInfo.Status = status;
        _progressInfo.CurrentFileName = currentFile;
        
        // Show progress during any active task state AND terminal states
        if (statusLower is "validating" or "scanning" or "preparing" or "copying" or "finalizing" or "cancelling" or "completed" or "cancelled" or "failed" or "up to date")
        {
            IsProgressVisible = true;
        }
        
        // Control shimmer visibility: show only during active task states
        if (statusLower is "validating" or "scanning" or "preparing" or "copying" or "finalizing")
        {
            ShimmerVisibility = "Visible";
        }
        else
        {
            ShimmerVisibility = "Collapsed";
        }
        
        double rawProgress;
        
        switch (statusLower)
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
        
        // Set raw progress (business value)
        _rawProgressValue = rawProgress;
        _lastRawProgress = rawProgress;
        _progressInfo.RawProgressValue = rawProgress;
        
        // Use animation target for smooth interpolation
        SetDisplayProgressValue(rawProgress);
        
        // Update display properties ONLY if not in terminal state
        // This prevents UpdateProgress from overwriting ApplyCopyCycleResult's work
        if (statusLower is not "completed" and not "up to date" and not "cancelled" and not "failed")
        {
            ProgressStatusText = status;
        }
        
        if (!string.IsNullOrEmpty(currentFile))
        {
            ProgressCurrentItemText = currentFile;
        }
        
        // Update progress fill brush based on task status
        if (statusLower == "completed" || statusLower == "up to date")
        {
            _progressFillBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x12, 0xB7, 0x6A)); // Green
        }
        else if (statusLower == "cancelled")
        {
            _progressFillBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x98, 0xA2, 0xB3)); // Gray
        }
        else if (statusLower == "failed")
        {
            _progressFillBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF0, 0x44, 0x38)); // Red
        }
        else
        {
            // Default blue for active states (validating, scanning, copying, etc.)
            _progressFillBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xED));
        }
        OnPropertyChanged(nameof(ProgressFillBrush));
    }
    
    /// <summary>
    /// Set animation target and start timer if needed
    /// </summary>
    private void SetProgressTarget(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return;
        
        value = Math.Clamp(value, 0, 100);
        
        // Don't allow regression during active task
        if (value < _displayProgressValue && 
            ProgressStatusText.ToLower() is not "ready" and not "completed" and not "cancelled" and not "failed")
        {
            value = _displayProgressValue;
        }
        
        _progressAnimationTarget = value;
        
        // Start timer if not running and target is different
        if (!_progressAnimationTimer.IsEnabled && Math.Abs(_progressAnimationTarget - _displayProgressValue) > 0.05)
        {
            _lastProgressAnimationTick = DateTime.UtcNow;
            _progressAnimationTimer.Start();
        }
    }
    
    /// <summary>
    /// Animation tick - smooth追赶 algorithm
    /// </summary>
    private void OnProgressAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Clamp((now - _lastProgressAnimationTick).TotalSeconds, 0, 0.1);
        _lastProgressAnimationTick = now;
        
        var current = _displayProgressValue;
        var target = _progressAnimationTarget;
        
        // Don't exceed target or go backwards
        if (target < current)
            target = current;
        
        var difference = target - current;
        
        // Close enough - snap to target
        if (Math.Abs(difference) < 0.05)
        {
            _displayProgressValue = target;
            UpdateProgressDisplay();
            _progressAnimationTimer.Stop();
            return;
        }
        
        // Dynamic speed based on difference size
        double speed;
        if (difference < 2)
            speed = 22;      // Very small changes: ~100-150ms
        else if (difference < 5)
            speed = 18;      // Small changes: ~150-250ms
        else if (difference < 15)
            speed = 14;      // Medium changes: ~250-400ms
        else if (difference < 30)
            speed = 11;      // Large changes: ~350-550ms
        else
            speed = 9;       // Very large changes: ~450-700ms
        
        // Exponential smoothing
        var factor = 1.0 - Math.Exp(-speed * elapsedSeconds);
        var next = current + difference * factor;
        
        if (next > target)
            next = target;
        
        _displayProgressValue = Math.Clamp(next, current, 100);
        UpdateProgressDisplay();
    }
    
    /// <summary>
    /// Update display properties from DisplayProgressValue
    /// </summary>
    private void UpdateProgressDisplay()
    {
        // Update percentage text only when integer changes
        var percentage = (int)Math.Round(_displayProgressValue);
        if (percentage != _lastDisplayedPercentage)
        {
            _lastDisplayedPercentage = percentage;
            ProgressPercentageText = $"{percentage}%";
        }
        
        // Notify ProgressValue and DisplayProgressRatio for ScaleTransform binding
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(DisplayProgressRatio));
    }
    
    /// <summary>
    /// Atomically applies the final state of a completed copy cycle.
    /// Must be called on the UI dispatcher.
    /// </summary>
    private void ApplyCopyCycleResult(CopyCycleOutcome outcome, int totalFiles, int processedFiles, int copiedFiles, int failedFiles, long totalBytes, string? errorMessage = null)
    {
        // CRITICAL: Stop animation timer FIRST to prevent it from overwriting completion state
        _progressAnimationTimer.Stop();
        
        // Force immediate snap to 100% for all completion outcomes (except Cancelled/Failed which keep current)
        if (outcome is CopyCycleOutcome.Completed or CopyCycleOutcome.UpToDate or CopyCycleOutcome.PartiallyCompleted)
        {
            _progressAnimationTarget = 100;
            _displayProgressValue = 100;
            _rawProgressValue = 100;
        }
        
        // Notify _displayProgressValue and Ratio AFTER setting values to ensure bindings update
        OnPropertyChanged(nameof(_displayProgressValue));
        OnPropertyChanged(nameof(DisplayProgressRatio));
        OnPropertyChanged(nameof(ProgressValue));
        
        // Validate: A completed cycle with files cannot show "No new files"
        if (totalFiles > 0 && ProgressFileCountText.Contains("No new", System.StringComparison.OrdinalIgnoreCase))
        {
            AddLog("WARNING", $"Internal state warning: ProgressFileCountText was 'No new files' despite {totalFiles} total files.");
        }
        
        // Determine final state based on outcome
        switch (outcome)
        {
            case CopyCycleOutcome.Completed:
                ProgressStatusText = "Completed";
                ProgressPercentageText = "100%";
                ProgressFileCountText = totalFiles > 0 
                    ? $"{processedFiles} of {totalFiles} files" 
                    : "No new MF4 files";
                ProgressCurrentItemText = totalFiles > 0
                    ? "Copy cycle completed successfully."
                    : "The destination is already up to date.";
                ProgressFillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x12, 0xB7, 0x6A)); // Success green
                CurrentStatusText = "Completed";
                StartButtonText = "START AGAIN";
                IsCopying = false;
                
                // Log final completion message (only once per cycle)
                if (totalFiles > 0)
                {
                    var sizeMB = totalBytes / (1024.0 * 1024.0);
                    AddLog("SUCCESS", $"Copy cycle completed: {processedFiles} of {totalFiles} files, {sizeMB:F2} MB.");
                }
                else
                {
                    AddLog("INFO", "Copy cycle completed: no new MF4 files found.");
                }
                break;
                
            case CopyCycleOutcome.UpToDate:
                _progressAnimationTarget = 100;
                ProgressStatusText = "Up to date";
                ProgressPercentageText = "100%";
                ProgressFileCountText = "No new MF4 files";
                ProgressCurrentItemText = "The destination is already up to date.";
                ProgressFillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x12, 0xB7, 0x6A)); // Success green
                CurrentStatusText = "Up to date";
                StartButtonText = "START AGAIN";
                IsCopying = false;
                
                AddLog("INFO", "No new MF4 files found.");
                break;
                
            case CopyCycleOutcome.PartiallyCompleted:
                _progressAnimationTarget = 100;
                ProgressStatusText = "Completed with warnings";
                ProgressPercentageText = "100%";
                ProgressFileCountText = $"{processedFiles} of {totalFiles} files processed";
                ProgressCurrentItemText = failedFiles > 0 ? $"{failedFiles} files failed." : "Some files skipped.";
                ProgressFillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xED, 0x89, 0x36)); // Warning orange
                CurrentStatusText = "Completed with warnings";
                StartButtonText = "START AGAIN";
                IsCopying = false;
                
                AddLog("WARNING", $"Copy cycle completed with warnings: {copiedFiles} succeeded, {failedFiles} failed.");
                break;
                
            case CopyCycleOutcome.Cancelled:
                // Keep current progress percentage, don't force 100
                ProgressStatusText = "Cancelled";
                ProgressFileCountText = $"{processedFiles} of {totalFiles} files processed";
                ProgressCurrentItemText = "Copy cycle was cancelled.";
                ProgressFillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x98, 0xA2, 0xB3)); // Neutral gray
                CurrentStatusText = "Cancelled";
                StartButtonText = "START";
                IsCopying = false;
                
                AddLog("WARNING", $"Copy cycle cancelled: {processedFiles} of {totalFiles} files processed.");
                break;
                
            case CopyCycleOutcome.Failed:
                // Keep current progress percentage
                ProgressStatusText = "Failed";
                ProgressFileCountText = $"{processedFiles} of {totalFiles} files processed";
                ProgressCurrentItemText = errorMessage ?? "An error occurred during copy.";
                ProgressFillBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF0, 0x44, 0x38)); // Error red
                CurrentStatusText = "Failed";
                StartButtonText = "START";
                IsCopying = false;
                
                AddLog("ERROR", $"Copy cycle failed: {errorMessage ?? "Unknown error"}");
                break;
        }
        
        // Notify all bound properties
        OnPropertyChanged(nameof(ProgressStatusText));
        OnPropertyChanged(nameof(ProgressPercentageText));
        OnPropertyChanged(nameof(ProgressFileCountText));
        OnPropertyChanged(nameof(ProgressCurrentItemText));
        OnPropertyChanged(nameof(ProgressFillBrush));
        OnPropertyChanged(nameof(CurrentStatusText));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(IsCopying));
        OnPropertyChanged(nameof(IsCopyCompleted));
        OnPropertyChanged(nameof(IsCopyFailed));
        OnPropertyChanged(nameof(IsCopyCancelled));
    }
    
    // Outcome enum for copy cycle results
    private enum CopyCycleOutcome
    {
        Completed,
        UpToDate,
        PartiallyCompleted,
        Cancelled,
        Failed
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
        _rawProgressValue = 0;
        _displayProgressValue = 0;
        _progressAnimationTarget = 0;
        _lastDisplayedPercentage = -1;
        _progressAnimationTimer.Stop();
        
        // Reset progress fill brush to default blue
        _progressFillBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xED));
        OnPropertyChanged(nameof(ProgressFillBrush));
        OnPropertyChanged(nameof(DisplayProgressRatio));
        
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
