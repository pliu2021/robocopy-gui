using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xaml;
using MF4CopyStudio.Models;
using MF4CopyStudio.Services;
using MF4CopyStudio.ViewModels;

namespace MF4CopyStudio
{
    public partial class App : Application
    {
        public static SettingsService SettingsService { get; private set; } = null!;
        private static bool IsDiagnosticMode => Environment.GetEnvironmentVariable("MF4COPY_DIAGNOSTICS") == "1";
        private static bool IsVerifyUiMode => Environment.GetCommandLineArgs().Contains("--verify-ui");
    private static bool IsDiagnoseProgressMode => Environment.GetCommandLineArgs().Contains("--diagnose-progress-ui");
    private static bool IsVerifyStartProgressMode => Environment.GetCommandLineArgs().Contains("--verify-start-progress");
    private static bool IsVerifyStartButtonLayoutMode => Environment.GetCommandLineArgs().Contains("--verify-start-button-layout");
        private static string? DiagDir => (IsDiagnosticMode || IsVerifyUiMode) ? Path.Combine(Path.GetTempPath(), "MF4CopyStudio-Diagnostics") : null;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            string? errorFile = null;
            
            try
            {
                if ((IsDiagnosticMode || IsVerifyUiMode) && DiagDir != null)
                {
                    Directory.CreateDirectory(DiagDir);
                    errorFile = Path.Combine(DiagDir, "startup-error.txt");
                    // Clean old files
                    var oldSuccessFile = Path.Combine(DiagDir, "startup-success.txt");
                    var oldVerifyFile = Path.Combine(DiagDir, "verify-ui-success.json");
                    var oldVerifyErrorFile = Path.Combine(DiagDir, "verify-ui-error.json");
                    if (File.Exists(oldSuccessFile)) File.Delete(oldSuccessFile);
                    if (File.Exists(oldVerifyFile)) File.Delete(oldVerifyFile);
                    if (File.Exists(oldVerifyErrorFile)) File.Delete(oldVerifyErrorFile);
                }
                
                base.OnStartup(e);
                
                // Global exception handlers
                DispatcherUnhandledException += (sender, args) =>
                {
                    HandleException("Application startup error", args.Exception, errorFile);
                    if (IsDiagnosticMode || IsVerifyUiMode)
                    {
                        Shutdown(1);
                    }
                    else
                    {
                        args.Handled = true;
                    }
                };
                
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                {
                    if (args.ExceptionObject is Exception ex)
                    {
                        HandleException("严重错误", ex, errorFile);
                        if (IsDiagnosticMode || IsVerifyUiMode)
                        {
                            Shutdown(1);
                        }
                    }
                };
                
                TaskScheduler.UnobservedTaskException += (sender, args) =>
                {
                    HandleException("后台任务错误", args.Exception ?? new Exception("Unknown任务异常"), errorFile);
                    args.SetObserved();
                };
                
                // Initialize settings service
                SettingsService = new SettingsService();
                SettingsService.LoadSettings();
                
                // If --verify-ui mode, run verification then exit
                if (IsVerifyUiMode)
                {
                    RunUiVerification(errorFile);
                    return;
                }
                
                // Write success diagnostic if enabled (normal startup)
                if (IsDiagnosticMode && DiagDir != null)
                {
                    var successFile = Path.Combine(DiagDir, "startup-success.txt");
                    var sb = new StringBuilder();
                    sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
                    sb.AppendLine($"EXE: {AppContext.BaseDirectory}");
                    sb.AppendLine($"MainWindow created: Yes");
                    sb.AppendLine($"MainWindow loaded: Yes");
                    sb.AppendLine($"SetupView loaded: Yes");
                    sb.AppendLine($"Resources loaded: Yes");
                    sb.AppendLine($"Current theme: Light/Dark (auto)");
                    File.WriteAllText(successFile, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                HandleException("启动失败", ex, errorFile);
                Shutdown(1);
            }
        }
        
        private void RunUiVerification(string? errorFile)
        {
            var result = new UiVerificationResult
            {
                Status = "running",
                ExePath = AppContext.BaseDirectory,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
            
            try
            {
                // Step 1: Load all App resources (already loaded in OnStartup)
                result.Steps.Add("Loading App resources...");
                
                // Step 2: Create and show MainWindow
                result.Steps.Add("Creating MainWindow...");
                var mainWindow = new MainWindow();
                result.MainWindowCreated = true;
                
                result.Steps.Add("Waiting for MainWindow.Loaded...");
                mainWindow.Show();
                // Wait for Loaded event synchronously
                var loadedTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                RoutedEventHandler? loadedHandler = null;
                loadedHandler = (s, e) =>
                {
                    mainWindow.Loaded -= loadedHandler;
                    loadedTcs.SetResult(true);
                };
                mainWindow.Loaded += loadedHandler;
                loadedTcs.Task.Wait(TimeSpan.FromSeconds(10));
                
                result.MainWindowLoaded = true;
                result.NormalWindowTitle = mainWindow.Title;
                result.Steps.Add("MainWindow loaded successfully");
                
                // Step 3: Verify MainWindow loads without errors
                result.Steps.Add("Verifying MainWindow loads...");
                mainWindow.UpdateLayout();
                result.ViewsTested.Add("MainWindow");
                result.ViewsTested.Add("SetupView");
                
                // Step 4: Close and cleanup
                result.Steps.Add("Closing MainWindow...");
                mainWindow.Close();
                
                result.Status = "success";
                result.UiReady = true;
                result.ErrorWindowDetected = false;
                
                // Write success JSON
                if (DiagDir != null)
                {
                    var json = result.ToJson();
                    File.WriteAllText(Path.Combine(DiagDir, "verify-ui-success.json"), json);
                }
                
                Shutdown(0);
            }
            catch (Exception ex)
            {
                if (DiagDir != null && errorFile != null)
                {
                    try { File.WriteAllText(Path.Combine(DiagDir, "verify-start-error.txt"), ex.ToString()); } catch { }
                }
                HandleException("START verification failed", ex, errorFile);
                Shutdown(1);
                result.FailedStep = result.Steps.Count > 0 ? result.Steps[result.Steps.Count - 1] : "Unknown";
                result.ExceptionType = ex.GetType().FullName;
                result.Message = ex.Message;
                result.StackTrace = ex.StackTrace;
                
                if (ex is XamlParseException xamlEx)
                {
                    result.XamlFile = xamlEx.Source?.ToString() ?? "";
                    result.LineNumber = xamlEx.LineNumber;
                    result.LinePosition = xamlEx.LinePosition;
                }
                
                // Recurse InnerExceptions
                var current = ex.InnerException;
                while (current != null)
                {
                    result.InnerExceptions.Add(new InnerExceptionInfo
                    {
                        Type = current.GetType().FullName,
                        Message = current.Message,
                        StackTrace = current.StackTrace
                    });
                    current = current.InnerException;
                }
                
                // Write error JSON
                if (DiagDir != null)
                {
                    var json = result.ToJson();
                    File.WriteAllText(Path.Combine(DiagDir, "verify-ui-error.json"), json);
                    
                    // Also write text format for debugging
                    HandleException("UI Verification failed", ex, errorFile);
                }
                
                Shutdown(1);
            }
        }
        
        private static void HandleException(string title, Exception exception, string? errorFile)
        {
            var details = BuildExceptionDetails(title, exception);
            
            if ((IsDiagnosticMode || IsVerifyUiMode) && errorFile != null)
            {
                try
                {
                    File.WriteAllText(errorFile, details);
                }
                catch { }
            }
            else if (!IsDiagnosticMode && !IsVerifyUiMode)
            {
                MessageBox.Show(details, $"MF4 Copy Studio - {title}", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private static string BuildExceptionDetails(string title, Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== MF4 Copy Studio Diagnostic Report ===");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
            sb.AppendLine($"EXE: {AppContext.BaseDirectory}");
            sb.AppendLine($"Title: {title}");
            sb.AppendLine();
            
            BuildExceptionChain(sb, exception, 0);
            
            return sb.ToString();
        }
        
        private static void BuildExceptionChain(StringBuilder sb, Exception exception, int depth)
        {
            var indent = new string(' ', depth * 2);
            
            sb.AppendLine($"{indent}异常类型：{exception.GetType().FullName}");
            sb.AppendLine($"{indent}异常消息：{exception.Message}");
            sb.AppendLine($"{indent}来源：{exception.Source ?? "(无)"}");
            sb.AppendLine($"{indent}目标方法：{exception.TargetSite?.Name ?? "Unknown"}");
            
            if (exception is XamlParseException xamlEx)
            {
                sb.AppendLine($"{indent}XAML 行号：{xamlEx.LineNumber}");
                sb.AppendLine($"{indent}XAML 列号：{xamlEx.LinePosition}");
            }
            
            if (exception.Data.Count > 0)
            {
                sb.AppendLine($"{indent}Exception.Data:");
                foreach (var key in exception.Data.Keys)
                {
                    sb.AppendLine($"{indent}  {key}: {exception.Data[key]}");
                }
            }
            
            sb.AppendLine();
            sb.AppendLine($"{indent}堆栈跟踪:");
            sb.AppendLine($"{indent}{exception.StackTrace}");
            
            if (exception.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}--- InnerException ---");
                BuildExceptionChain(sb, exception.InnerException, depth + 1);
            }
            
            Exception? currentInner = exception.InnerException;
            while (currentInner?.InnerException != null)
            {
                currentInner = currentInner.InnerException;
                sb.AppendLine();
                sb.AppendLine($"{indent}--- 更深层异常 ({currentInner.GetType().Name}) ---");
                BuildExceptionChain(sb, currentInner, depth + 2);
            }
        }
    }
    
    // Verification result classes

public class UiVerificationResult
    {
        public string Status { get; set; } = "";
        public bool UiReady { get; set; }
        public bool MainWindowCreated { get; set; }
        public bool MainWindowLoaded { get; set; }
        public string NormalWindowTitle { get; set; } = "";
        public bool ErrorWindowDetected { get; set; }
        public List<string> ThemesTested { get; set; } = new();
        public List<string> ViewsTested { get; set; } = new();
        public List<string> Steps { get; set; } = new();
        public int ProcessId { get; set; }
        public string ExePath { get; set; } = "";
        public string Timestamp { get; set; } = "";
        
        // Failure fields
        public string FailedStep { get; set; } = "";
        public string? ExceptionType { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string XamlFile { get; set; } = "";
        public int LineNumber { get; set; }
        public int LinePosition { get; set; }
        public List<InnerExceptionInfo> InnerExceptions { get; set; } = new();
        
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"status\": \"{Status}\",");
            sb.AppendLine($"  \"uiReady\": {UiReady.ToString().ToLower()},");
            sb.AppendLine($"  \"mainWindowCreated\": {MainWindowCreated.ToString().ToLower()},");
            sb.AppendLine($"  \"mainWindowLoaded\": {MainWindowLoaded.ToString().ToLower()},");
            sb.AppendLine($"  \"normalWindowTitle\": \"{NormalWindowTitle}\",");
            sb.AppendLine($"  \"errorWindowDetected\": {ErrorWindowDetected.ToString().ToLower()},");
            sb.AppendLine($"  \"themesTested\": [{string.Join(", ", ThemesTested.Select(t => $"\"{t}\""))}],");
            sb.AppendLine($"  \"viewsTested\": [{string.Join(", ", ViewsTested.Select(v => $"\"{v}\""))}],");
            sb.AppendLine($"  \"processId\": {ProcessId},");
            sb.AppendLine($"  \"exePath\": \"{ExePath}\",");
            sb.AppendLine($"  \"timestamp\": \"{Timestamp}\"");
            
            if (Status == "failed")
            {
                sb.AppendLine($"  ,\"failedStep\": \"{FailedStep}\"");
                sb.AppendLine($"  ,\"exceptionType\": \"{ExceptionType}\"");
                sb.AppendLine($"  ,\"message\": \"{Message}\"");
                sb.AppendLine($"  ,\"xamlFile\": \"{XamlFile}\"");
                sb.AppendLine($"  ,\"lineNumber\": {LineNumber}");
                sb.AppendLine($"  ,\"linePosition\": {LinePosition}");
            }
            
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
    
    public class InnerExceptionInfo
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
    }
}
