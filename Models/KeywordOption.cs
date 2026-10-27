using CommunityToolkit.Mvvm.ComponentModel;

namespace MF4CopyStudio.Models;

/// <summary>
/// Keyword option for UI selection (memory only, not persisted)
/// </summary>
public partial class KeywordOption : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isSelected;
}
