using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MF4CopyStudio.Controls
{
    public partial class PathPicker : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(PathPicker));
        
        public static readonly DependencyProperty PathTextProperty =
            DependencyProperty.Register(nameof(PathText), typeof(string), typeof(PathPicker),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPathTextChanged));
        
        public static readonly DependencyProperty TargetPropertyProperty =
            DependencyProperty.Register(nameof(TargetProperty), typeof(string), typeof(PathPicker));
        
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
        
        public string PathText
        {
            get => (string)GetValue(PathTextProperty);
            set => SetValue(PathTextProperty, value);
        }
        
        public string TargetProperty
        {
            get => (string)GetValue(TargetPropertyProperty);
            set => SetValue(TargetPropertyProperty, value);
        }
        
        public PathPicker()
        {
            InitializeComponent();
        }
        
        private static void OnPathTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PathPicker picker && !string.IsNullOrEmpty(picker.TargetProperty))
            {
                picker.UpdateViewModelProperty(e.NewValue as string);
            }
        }
        
        private void UpdateViewModelProperty(string newPath)
        {
            if (this.DataContext is MF4CopyStudio.ViewModels.MainViewModel vm)
            {
                if (TargetProperty == "SourcePath")
                    vm.SourcePath = newPath;
                else if (TargetProperty == "DestinationPath")
                    vm.DestinationPath = newPath;
            }
        }
        
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Select a folder",
                UseDescriptionForTitle = true
            };
            
            try
            {
                if (!string.IsNullOrEmpty(PathText) && System.IO.Directory.Exists(PathText))
                {
                    dialog.SelectedPath = PathText;
                }
                
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    PathText = dialog.SelectedPath;
                }
            }
            finally
            {
                dialog.Dispose();
            }
        }
    }
}
