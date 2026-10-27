using System;
using System.Windows;
using System.Windows.Media;

namespace MF4CopyStudio
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            Loaded += AboutWindow_Loaded;
        }
        
        private void AboutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Layout validation for diagnostic mode
            if (Environment.GetEnvironmentVariable("MF4COPY_DIAGNOSTICS") == "1")
            {
                UpdateLayout();
                
                var copyrightText = FindName("CopyrightText") as System.Windows.Controls.TextBlock;
                var closeButton = FindName("CloseButton") as System.Windows.Controls.Button;
                
                if (copyrightText != null)
                {
                    var isVisible = copyrightText.IsVisible;
                    var actualWidth = copyrightText.ActualWidth;
                    var actualHeight = copyrightText.ActualHeight;
                    var text = copyrightText.Text;
                    
                    // Calculate position relative to window
                    var transform = copyrightText.TransformToAncestor(this);
                    var position = transform.Transform(new Point(0, 0));
                    var bottom = position.Y + actualHeight;
                    
                    System.Diagnostics.Debug.WriteLine($"[About Validation]");
                    System.Diagnostics.Debug.WriteLine($"  CopyrightText.Text: {text}");
                    System.Diagnostics.Debug.WriteLine($"  IsVisible: {isVisible}");
                    System.Diagnostics.Debug.WriteLine($"  ActualWidth: {actualWidth:F1}px");
                    System.Diagnostics.Debug.WriteLine($"  ActualHeight: {actualHeight:F1}px");
                    System.Diagnostics.Debug.WriteLine($"  Position: ({position.X:F1}, {position.Y:F1})");
                    System.Diagnostics.Debug.WriteLine($"  Bottom: {bottom:F1}px (Window.Height={ActualHeight:F1}px)");
                    
                    if (!isVisible || actualWidth < 80 || actualHeight < 10 || bottom > ActualHeight)
                    {
                        System.Diagnostics.Debug.WriteLine($"  [ERROR] Layout validation FAILED!");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  [OK] Layout validation PASSED");
                    }
                }
                
                if (closeButton != null)
                {
                    var transform = closeButton.TransformToAncestor(this);
                    var position = transform.Transform(new Point(0, 0));
                    var bottom = position.Y + closeButton.ActualHeight;
                    
                    System.Diagnostics.Debug.WriteLine($"  CloseButton Position: ({position.X:F1}, {position.Y:F1})");
                    System.Diagnostics.Debug.WriteLine($"  CloseButton Bottom: {bottom:F1}px");
                }
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void ViewLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            var licenseWindow = new LicenseWindow();
            licenseWindow.Owner = this;
            licenseWindow.ShowDialog();
        }
    }
}
