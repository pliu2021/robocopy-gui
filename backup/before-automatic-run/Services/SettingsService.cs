using System.IO;
using System.Text.Json;
using MF4CopyStudio.Models;

namespace MF4CopyStudio.Services;

/// <summary>
/// Manages application settings persistence
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _cachedSettings;
    
    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(localAppData, "RobocopyTool");
        
        if (!Directory.Exists(appDir))
        {
            Directory.CreateDirectory(appDir);
        }
        
        _settingsPath = Path.Combine(appDir, "settings.json");
    }
    
    public AppSettings Settings => _cachedSettings ?? LoadSettings();
    
    // Direct property access for new ViewModel
    public string? SourcePath
    {
        get => Settings.SourcePath;
        set { Settings.SourcePath = value; SaveSettings(Settings); }
    }
    
    public string? DestinationPath
    {
        get => Settings.DestinationPath;
        set { Settings.DestinationPath = value; SaveSettings(Settings); }
    }
    
    public int ThreadCount
    {
        get => Settings.ThreadCount;
        set { Settings.ThreadCount = value; SaveSettings(Settings); }
    }
    
    public int RetryCount
    {
        get => Settings.RetryCount;
        set { Settings.RetryCount = value; SaveSettings(Settings); }
    }
    
    public int RetryWaitSeconds
    {
        get => Settings.RetryWaitSeconds;
        set { Settings.RetryWaitSeconds = value; SaveSettings(Settings); }
    }
    
    public List<string>? SelectedKeywords
    {
        get => Settings.SelectedKeywords;
        set { Settings.SelectedKeywords = value; SaveSettings(Settings); }
    }
    
    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                if (_cachedSettings != null) return _cachedSettings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        
        // Return defaults
        _cachedSettings = new AppSettings();
        return _cachedSettings;
    }
    
    public void SaveSettings(AppSettings settings)
    {
        try
        {
            _cachedSettings = settings;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
    
    public void SaveSettings()
    {
        if (_cachedSettings != null)
        {
            SaveSettings(_cachedSettings);
        }
    }
}
