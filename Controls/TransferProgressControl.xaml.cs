using System.Windows;
using System.Windows.Controls;

namespace MF4CopyStudio.Controls
{
    /// <summary>
    /// Modern transfer progress control with Fluent 2 inspired design
    /// </summary>
    public partial class TransferProgressControl : UserControl
    {
        // Dependency Properties for binding
        
        public static readonly DependencyProperty IsProgressVisibleProperty =
            DependencyProperty.Register(nameof(IsProgressVisible), typeof(bool), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty IsProgressIndeterminateProperty =
            DependencyProperty.Register(nameof(IsProgressIndeterminate), typeof(bool), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressStatusTextProperty =
            DependencyProperty.Register(nameof(ProgressStatusText), typeof(string), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressPercentageTextProperty =
            DependencyProperty.Register(nameof(ProgressPercentageText), typeof(string), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressFileCountTextProperty =
            DependencyProperty.Register(nameof(ProgressFileCountText), typeof(string), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressCurrentItemTextProperty =
            DependencyProperty.Register(nameof(ProgressCurrentItemText), typeof(string), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressValueProperty =
            DependencyProperty.Register(nameof(ProgressValue), typeof(double), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty ProgressTriggerTextProperty =
            DependencyProperty.Register(nameof(ProgressTriggerText), typeof(string), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty IsCompletedProperty =
            DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty IsFailedProperty =
            DependencyProperty.Register(nameof(IsFailed), typeof(bool), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty IsCancelledProperty =
            DependencyProperty.Register(nameof(IsCancelled), typeof(bool), typeof(TransferProgressControl));
        
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(TransferProgressControl));
        
        // Property wrappers
        
        public bool IsProgressVisible
        {
            get => (bool)GetValue(IsProgressVisibleProperty);
            set => SetValue(IsProgressVisibleProperty, value);
        }
        
        public bool IsProgressIndeterminate
        {
            get => (bool)GetValue(IsProgressIndeterminateProperty);
            set => SetValue(IsProgressIndeterminateProperty, value);
        }
        
        public string ProgressStatusText
        {
            get => (string)GetValue(ProgressStatusTextProperty);
            set => SetValue(ProgressStatusTextProperty, value);
        }
        
        public string ProgressPercentageText
        {
            get => (string)GetValue(ProgressPercentageTextProperty);
            set => SetValue(ProgressPercentageTextProperty, value);
        }
        
        public string ProgressFileCountText
        {
            get => (string)GetValue(ProgressFileCountTextProperty);
            set => SetValue(ProgressFileCountTextProperty, value);
        }
        
        public string ProgressCurrentItemText
        {
            get => (string)GetValue(ProgressCurrentItemTextProperty);
            set => SetValue(ProgressCurrentItemTextProperty, value);
        }
        
        public double ProgressValue
        {
            get => (double)GetValue(ProgressValueProperty);
            set => SetValue(ProgressValueProperty, value);
        }
        
        public string ProgressTriggerText
        {
            get => (string)GetValue(ProgressTriggerTextProperty);
            set => SetValue(ProgressTriggerTextProperty, value);
        }
        
        public bool IsCompleted
        {
            get => (bool)GetValue(IsCompletedProperty);
            set => SetValue(IsCompletedProperty, value);
        }
        
        public bool IsFailed
        {
            get => (bool)GetValue(IsFailedProperty);
            set => SetValue(IsFailedProperty, value);
        }
        
        public bool IsCancelled
        {
            get => (bool)GetValue(IsCancelledProperty);
            set => SetValue(IsCancelledProperty, value);
        }
        
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }
        
        public TransferProgressControl()
        {
            InitializeComponent();
        }
    }
}
