using System.IO;
using System.Windows;
using System.Threading.Tasks;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using MF4CopyStudio.ViewModels;

namespace MF4CopyStudio
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        
        public MainWindow()
        {
            InitializeComponent();
            
            _viewModel = new MainViewModel(new MF4CopyStudio.Services.SettingsService());
            DataContext = _viewModel;
            
            // Bind ShowAboutCommand to actual dialog
            _viewModel.ShowAboutCommand = new AsyncRelayCommand(async () => await ShowAboutDialog());
            
            // Setup auto-scroll
            SetupAutoScroll();
            
            // NO manual progress fill binding - using standard WPF ProgressBar
        }
        
        private async Task ShowAboutDialog()
        {
            var aboutWindow = new AboutWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            aboutWindow.ShowDialog();
            await Task.CompletedTask;
        }
        
        // Auto-scroll implementation
        private void SetupAutoScroll()
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsAutoScrollEnabled))
                    {
                        SetupLogCollectionChanged(vm);
                    }
                };
                SetupLogCollectionChanged(vm);
            }
        }
        
        private void SetupLogCollectionChanged(MainViewModel vm)
        {
            // Listen to ActivityLog changes
            var prop = vm.GetType().GetProperty("ActivityLog");
            if (prop != null)
            {
                var log = prop.GetValue(vm) as System.Collections.ObjectModel.ObservableCollection<MF4CopyStudio.Models.LogEntry>;
                if (log != null)
                {
                    log.CollectionChanged += (s, e) =>
                    {
                        if (vm.IsAutoScrollEnabled && LogScrollViewer != null)
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                try
                                {
                                    LogScrollViewer.ScrollToEnd();
                                }
                                catch { }
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    };
                }
            }
        }
        
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            
            _viewModel.SaveSettings();
        
            if (_viewModel.IsCopying)
            {
                var result = MessageBox.Show(
                    "A copy task is in progress. Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
            
                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            
                _viewModel.CancelCopyCommand.Execute(null);
            }
            
            _viewModel?.Dispose();
        }
        
        private bool _advancedSettingsExpanded = false;
        
        private void AdvancedSettingsHeader_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _advancedSettingsExpanded = !_advancedSettingsExpanded;
            
            if (AdvancedSettingsContent != null)
            {
                AdvancedSettingsContent.Visibility = _advancedSettingsExpanded ? 
                    System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            
            if (ChevronRotation != null)
            {
                ChevronRotation.Angle = _advancedSettingsExpanded ? 180 : 0;
            }
        }
    }
}
