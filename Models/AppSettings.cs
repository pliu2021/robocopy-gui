using System.Text.Json.Serialization;

namespace MF4CopyStudio.Models;

/// <summary>
/// Application settings persisted to local app data
/// </summary>
public class AppSettings
{
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = @"\\10.104.3.16\Data\2026-05-18_19-09-17\Trigger";
    
    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; } = @"E:\0518_373643_mtre";
    
    [JsonPropertyName("folderKeywords")]
    public List<string> SelectedKeywords { get; set; } = new() { "MT_RE" };
    
    [JsonPropertyName("customKeyword")]
    public string? CustomKeyword { get; set; }
    
    [JsonPropertyName("isCustomKeywordEnabled")]
    public bool IsCustomKeywordEnabled { get; set; } = false;
    
    [JsonPropertyName("fileExtension")]
    public string FileExtension { get; set; } = ".MF4";
    
    [JsonPropertyName("threadCount")]
    public int ThreadCount { get; set; } = 16;
    
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 2;
    
    [JsonPropertyName("retryWaitSeconds")]
    public int RetryWaitSeconds { get; set; } = 2;
    
    [JsonPropertyName("automaticRunEnabled")]
    public bool AutomaticRunEnabled { get; set; } = false;
    
    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1080;
    
    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 720;
    
    [JsonPropertyName("windowState")]
    public string WindowState { get; set; } = "Normal";
}
