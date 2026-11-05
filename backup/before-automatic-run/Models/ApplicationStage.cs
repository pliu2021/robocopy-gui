namespace MF4CopyStudio.Models;

/// <summary>
/// Application stage enumeration for state management
/// </summary>
public enum ApplicationStage
{
    Setup,
    Scanning,
    ScanResults,
    Copying,
    Completed,
    PartialFailure,
    FatalError
}
