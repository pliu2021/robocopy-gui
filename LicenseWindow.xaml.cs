using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MF4CopyStudio
{
    public partial class LicenseWindow : Wpf.Ui.Controls.FluentWindow
    {
        public LicenseWindow()
        {
            InitializeComponent();
            App.PatchAccentResources(Resources);
            LoadLicenseText();

            // Re-seed after WPF-UI re-applies the window theme asynchronously (Loaded+),
            // then re-apply so the acrylic backdrop + palette stay consistent.
            Loaded += (_, _) =>
            {
                var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5B7BC7");
                var accentLight = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B89CE");
                var accentDark = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4A69B5");
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent, accent, accentLight, accentDark);
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
                App.PatchAccentResources(Resources);
                UpdateLayout();
            };
            
            // Handle Escape key to close
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                }
            };
        }
        
        private void LoadLicenseText()
        {
            try
            {
                // Try to load from embedded resource first
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "MF4CopyStudio.LICENSE";
                
                string licenseText;
                
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            licenseText = reader.ReadToEnd();
                        }
                    }
                    else
                    {
                        // Fallback: try to load from LICENSE file in app directory
                        var appPath = AppDomain.CurrentDomain.BaseDirectory;
                        var licensePath = Path.Combine(appPath, "LICENSE");
                        
                        if (File.Exists(licensePath))
                        {
                            licenseText = File.ReadAllText(licensePath);
                        }
                        else
                        {
                            // Last resort: hardcoded license text
                            licenseText = GetHardcodedLicenseText();
                        }
                    }
                }
                
                LicenseTextBox.Text = licenseText;
            }
            catch (Exception ex)
            {
                LicenseTextBox.Text = $"Error loading license text:\n\n{ex.Message}\n\nPlease contact support.";
            }
        }
        
        private string GetHardcodedLicenseText()
        {
            return @"MIT License

Copyright (c) 2026 Liu Pengfei

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
