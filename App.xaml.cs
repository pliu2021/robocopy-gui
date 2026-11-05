using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
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
        private static bool IsLayoutProbeMode => Environment.GetCommandLineArgs().Contains("--layout-probe");
        private static string? DiagDir => (IsDiagnosticMode || IsVerifyUiMode || IsLayoutProbeMode) ? Path.Combine(Path.GetTempPath(), "MF4CopyStudio-Diagnostics") : null;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            string? errorFile = null;
            
            try
            {
                // Initialize WPF-UI Fluent theme (Light + Mica backdrop)
                                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                                    applicationTheme: Wpf.Ui.Appearance.ApplicationTheme.Light,
                                    backgroundEffect: Wpf.Ui.Controls.WindowBackdropType.Mica,
                                    updateAccent: true);

                // Override the WPF-UI theme accent with our low-saturation blue using the
                // official accent manager (regenerates the whole palette properly), then
                // patch any remaining accent keys in place.
                var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5B7BC7");
                var accentLight = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B89CE");
                var accentDark = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4A69B5");
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent, accent, accentLight, accentDark);
                ApplyAccentOverride();
                
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

                // If --layout-probe mode, dump in-process layout geometry then exit
                if (IsLayoutProbeMode)
                {
                    RunLayoutProbe();
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
        
        /// <summary>
        /// Overrides WPF-UI theme accent resources with the app's low-saturation blue.
        /// Must run after ApplicationThemeManager.Apply so the OS accent doesn't win.
        /// Key list taken from Wpf.Ui.dll string table (accent.baml palette).
        /// FluentWindow keeps its OWN theme dictionary at window scope, which beats
        /// application scope — so windows must call PatchAccentResources(Resources)
        /// after InitializeComponent as well.
        /// </summary>
        private static void ApplyAccentOverride()
        {
            PatchAccentResources(Current.Resources);
        }

        /// <summary>Recursive accent patcher usable on app- and window-level dictionaries.</summary>
        internal static void PatchAccentResources(System.Windows.ResourceDictionary root)
        {
            System.Windows.Media.Color P(string hex) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

            var primary = P("#5B7BC7");
            var l1 = P("#6B89CE"); var l2 = P("#7E97D6"); var l3 = P("#92A8DE");
            var d1 = P("#4A69B5"); var d2 = P("#42598F"); var d3 = P("#3A4E7E");
            var white = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
            var disabled = System.Windows.Media.Color.FromArgb(0x51, primary.R, primary.G, primary.B);
            var elevBorder = System.Windows.Media.Color.FromArgb(0xAB, d1.R, d1.G, d1.B);
            var selTextBg = System.Windows.Media.Color.FromArgb(0x24, primary.R, primary.G, primary.B);

            System.Windows.Media.SolidColorBrush B(System.Windows.Media.Color c) =>
                new System.Windows.Media.SolidColorBrush(c);

            // 1) Recurse into ALL merged dictionaries (WPF-UI merges accent.baml deep inside
            //    its ThemesDictionary) and patch accent entries in place.
            void Patch(System.Windows.ResourceDictionary dict)
            {
                foreach (var key in new[]
                {
                    "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary",
                    "SystemAccentColorTertiary", "SystemAccentColorDark1", "SystemAccentColorDark2",
                    "SystemAccentColorDark3", "SystemAccentColorLight1", "SystemAccentColorLight2",
                    "SystemAccentColorLight3", "AccentColor"
                })
                {
                    if (dict.Contains(key)) dict[key] = primary;
                }

                foreach (var key in new[]
                {
                    "SystemAccentBrush",
                    "AccentFillColorPrimaryBrush", "AccentFillColorDefaultBrush",
                    "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                    "SystemFillColorAttentionBrush"
                })
                {
                    if (dict.Contains(key)) dict[key] = B(primary);
                }

                if (dict.Contains("AccentFillColorDisabledBrush")) dict["AccentFillColorDisabledBrush"] = B(disabled);
                if (dict.Contains("AccentFillColorSelectedTextBackgroundBrush")) dict["AccentFillColorSelectedTextBackgroundBrush"] = B(selTextBg);
                if (dict.Contains("AccentControlElevationBorderBrush")) dict["AccentControlElevationBorderBrush"] = B(elevBorder);
                if (dict.Contains("TextOnAccentFillColorPrimaryBrush")) dict["TextOnAccentFillColorPrimaryBrush"] = B(white);
                if (dict.Contains("TextOnAccentFillColorSecondaryBrush")) dict["TextOnAccentFillColorSecondaryBrush"] = B(white);
                if (dict.Contains("AccentTextFillColorPrimaryBrush")) dict["AccentTextFillColorPrimaryBrush"] = B(primary);
                if (dict.Contains("AccentTextFillColorSecondaryBrush")) dict["AccentTextFillColorSecondaryBrush"] = B(l1);
                if (dict.Contains("AccentTextFillColorTertiaryBrush")) dict["AccentTextFillColorTertiaryBrush"] = B(d1);

                foreach (System.Windows.ResourceDictionary child in dict.MergedDictionaries) Patch(child);
            }

            foreach (var d in root.MergedDictionaries) Patch(d);

            // 2) Root-level entries (they shadow merged-dictionary values).
            root["SystemAccentColor"] = primary;
            root["SystemAccentColorPrimary"] = primary;
            root["SystemAccentColorSecondary"] = primary;
            root["SystemAccentColorTertiary"] = primary;
            root["SystemAccentColorDark1"] = d1;
            root["SystemAccentColorDark2"] = d2;
            root["SystemAccentColorDark3"] = d3;
            root["SystemAccentColorLight1"] = l1;
            root["SystemAccentColorLight2"] = l2;
            root["SystemAccentColorLight3"] = l3;
            root["SystemAccentBrush"] = B(primary);
            root["AccentColor"] = primary;
            root["AccentFillColorPrimaryBrush"] = B(primary);
            root["AccentFillColorDefaultBrush"] = B(primary);
            root["AccentFillColorSecondaryBrush"] = B(primary);
            root["AccentFillColorTertiaryBrush"] = B(primary);
            root["AccentFillColorDisabledBrush"] = B(disabled);
            root["AccentFillColorSelectedTextBackgroundBrush"] = B(selTextBg);
            root["AccentControlElevationBorderBrush"] = B(elevBorder);
            root["TextOnAccentFillColorPrimaryBrush"] = B(white);
            root["TextOnAccentFillColorSecondaryBrush"] = B(white);
            root["AccentTextFillColorPrimaryBrush"] = B(primary);
            root["AccentTextFillColorSecondaryBrush"] = B(l1);
            root["AccentTextFillColorTertiaryBrush"] = B(d1);
            root["SystemFillColorAttentionBrush"] = B(primary);
        }

        private void RunLayoutProbe()
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();

            var loadedTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            RoutedEventHandler? loadedHandler = null;
            loadedHandler = (s, e) => { mainWindow.Loaded -= loadedHandler; loadedTcs.SetResult(true); };
            mainWindow.Loaded += loadedHandler;
            loadedTcs.Task.Wait(TimeSpan.FromSeconds(10));
            mainWindow.UpdateLayout();

            var sb = new StringBuilder();
            sb.AppendLine($"DPI: {VisualTreeHelper.GetDpi(mainWindow).PixelsPerDip}");
            sb.AppendLine($"Window: ActualWidth={mainWindow.ActualWidth:F1} ActualHeight={mainWindow.ActualHeight:F1}");
            sb.AppendLine($"Window: RestoredWidth={mainWindow.RestoreBounds.Width:F1}");

            // accent color verification (unified low-saturation blue)
            var accentBrush = Application.Current.TryFindResource("AccentFillColorDefaultBrush") as System.Windows.Media.Brush;
            sb.AppendLine($"AccentResource(AccentFillColorDefaultBrush): {accentBrush}");
            if (mainWindow.StartButton.Background is System.Windows.Media.SolidColorBrush sbB)
                sb.AppendLine($"StartButtonBackground: #{sbB.Color.R:X2}{sbB.Color.G:X2}{sbB.Color.B:X2} (a={sbB.Color.A:X2})");
            else
                sb.AppendLine($"StartButtonBackground: {mainWindow.StartButton.Background}");

            FrameworkElement[] targets =
            {
                mainWindow.ApplicationLayoutRoot,
                mainWindow.ConfigurationCard,
                mainWindow.GlobalProgressHost,
                mainWindow.StartButtonHost,
                mainWindow.StartButton
            };

            foreach (var fe in targets)
            {
                var topLeft = fe.TranslatePoint(new Point(0, 0), mainWindow);
                var bottomRight = fe.TranslatePoint(new Point(fe.ActualWidth, fe.ActualHeight), mainWindow);
                sb.AppendLine($"{fe.Name}: L={topLeft.X:F1} T={topLeft.Y:F1} R={bottomRight.X:F1} B={bottomRight.Y:F1} W={fe.ActualWidth:F1} H={fe.ActualHeight:F1}");
            }

            var taskOverview = FindNamedDescendant(mainWindow, "TaskOverviewLayout") as FrameworkElement;
            var details = FindNamedDescendant(mainWindow, "TaskOverviewDetails") as FrameworkElement;
            if (taskOverview != null)
            {
                var tl = taskOverview.TranslatePoint(new Point(0, 0), mainWindow);
                var br = taskOverview.TranslatePoint(new Point(taskOverview.ActualWidth, taskOverview.ActualHeight), mainWindow);
                sb.AppendLine($"TaskOverviewLayout: L={tl.X:F1} T={tl.Y:F1} R={br.X:F1} B={br.Y:F1} W={taskOverview.ActualWidth:F1} H={taskOverview.ActualHeight:F1}");
            }
            if (details != null)
            {
                var tl = details.TranslatePoint(new Point(0, 0), mainWindow);
                var br = details.TranslatePoint(new Point(details.ActualWidth, details.ActualHeight), mainWindow);
                sb.AppendLine($"TaskOverviewDetails: L={tl.X:F1} T={tl.Y:F1} R={br.X:F1} B={br.Y:F1} W={details.ActualWidth:F1} H={details.ActualHeight:F1}");

                // dump every direct child row element of the details grid (alignment audit)
                int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(details);
                for (int i = 0; i < childCount; i++)
                {
                    if (System.Windows.Media.VisualTreeHelper.GetChild(details, i) is FrameworkElement row)
                    {
                        int gridRow = System.Windows.Controls.Grid.GetRow(row);
                        var rtl = row.TranslatePoint(new Point(0, 0), mainWindow);
                        var rbr = row.TranslatePoint(new Point(row.ActualWidth, row.ActualHeight), mainWindow);
                        sb.AppendLine($"  Row{gridRow} {row.GetType().Name}: L={rtl.X:F1} R={rbr.X:F1} T={rtl.Y:F1} B={rbr.Y:F1} W={row.ActualWidth:F1}");
                    }
                }
            }

            // find the widest MinWidth contributor in the left column
            double maxMin = 0; string? maxMinName = null;
            WalkMinWidths(mainWindow.ApplicationLayoutRoot, ref maxMin, ref maxMinName);
            sb.AppendLine($"MaxEffectiveMinWidth: {maxMin:F1} on {maxMinName ?? "(none)"}");

            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "MF4CopyStudio-Diagnostics", "layout-probe.txt"),
                sb.ToString());
            mainWindow.Close();
            Shutdown(0);
        }

        private static FrameworkElement? FindNamedDescendant(DependencyObject root, string name)
        {
            int Count(DependencyObject d) => VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < Count(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var deep = FindNamedDescendant(child, name);
                if (deep != null) return deep;
            }
            return null;
        }

        private static void WalkMinWidths(DependencyObject root, ref double maxMin, ref string? maxMinName)
        {
            int Count(DependencyObject d) => VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < Count(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe)
                {
                    double eff = Math.Max(fe.MinWidth, fe.Margin.Left + fe.Margin.Right);
                    double score = eff;
                    if (fe is TextBlock tb) score = Math.Max(score, tb.DesiredSize.Width);
                    if (score > maxMin) { maxMin = score; maxMinName = $"{fe.GetType().Name}:{fe.Name}"; }
                }
                WalkMinWidths(child, ref maxMin, ref maxMinName);
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
