# MF4 Copy Studio Startup Diagnostic Script
# Usage: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\diagnose-startup.ps1 -ExePath "path\to\MF4CopyStudio.exe"

param(
    [Parameter(Mandatory=$true)]
    [string]$ExePath,
    
    [int]$TimeoutSeconds = 12
)

$ErrorActionPreference = "Stop"

# Enable diagnostic mode
$env:MF4COPY_DIAGNOSTICS = "1"

# Setup paths
$DiagDir = "$env:TEMP\MF4CopyStudio-Diagnostics"
$ErrorFile = "$DiagDir\startup-error.txt"
$SuccessFile = "$DiagDir\startup-success.txt"

# Clean old diagnostic files
if (Test-Path $DiagDir) {
    Remove-Item -Recurse -Force $DiagDir
}
New-Item -ItemType Directory -Path $DiagDir | Out-Null

# Resolve EXE path
$ExePath = Resolve-Path $ExePath
Write-Host "=== MF4 Copy Studio Startup Diagnostic ==="
Write-Host "EXE: $ExePath"
Write-Host "Timeout: ${TimeoutSeconds}s"
Write-Host "Diagnostic Dir: $DiagDir"
Write-Host ""

# Start process
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = $ExePath
$processInfo.UseShellExecute = $true
$processInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal

$process = [System.Diagnostics.Process]::Start($processInfo)
Write-Host "Started process PID: $($process.Id)"

# Wait for timeout or exit
$elapsed = 0
while ($elapsed -lt $TimeoutSeconds) {
    Start-Sleep -Milliseconds 500
    $elapsed += 0.5
    
    if ($process.HasExited) {
        Write-Host "Process exited after ${elapsed}s with code: $($process.ExitCode)"
        break
    }
}

# Check results
$hasError = Test-Path $ErrorFile
$hasSuccess = Test-Path $SuccessFile

if ($hasError) {
    Write-Host ""
    Write-Host "=== STARTUP ERROR DETECTED ===" -ForegroundColor Red
    Get-Content $ErrorFile
    Write-Host ""
    
    # Kill process if still running
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    
    # Cleanup
    Remove-Item -Recurse -Force $DiagDir -ErrorAction SilentlyContinue
    
    exit 1
}

if ($hasSuccess) {
    Write-Host ""
    Write-Host "=== STARTUP SUCCESS ===" -ForegroundColor Green
    Get-Content $SuccessFile
    Write-Host ""
    
    # Close process gracefully
    if (-not $process.HasExited) {
        $process.CloseMainWindow()
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) {
            $process.Kill()
        }
        $process.WaitForExit()
    }
    
    # Cleanup
    Remove-Item -Recurse -Force $DiagDir -ErrorAction SilentlyContinue
    
    exit 0
}

# Timeout or no diagnostic file
if (-not $process.HasExited) {
    Write-Host "Process still running after ${TimeoutSeconds}s - assuming success (window displayed)"
    $process.CloseMainWindow()
    Start-Sleep -Milliseconds 500
    if (-not $process.HasExited) {
        $process.Kill()
    }
    $process.WaitForExit()
    
    # Cleanup
    Remove-Item -Recurse -Force $DiagDir -ErrorAction SilentlyContinue
    
    exit 0
}

Write-Host "Process exited without diagnostic files (exit code: $($process.ExitCode))" -ForegroundColor Yellow

# Try to read Windows Event Log for .NET Runtime errors
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName = "Application"
        ProviderName = ".NET Runtime"
        StartTime = (Get-Date).AddMinutes(-5)
    } -ErrorAction SilentlyContinue | Where-Object {
        $_.Message -like "*MF4CopyStudio*" -or $_.Message -like "*$($process.Id)*"
    } | Select-Object -First 3
    
    if ($events) {
        Write-Host ""
        Write-Host "=== Recent .NET Runtime Events ===" -ForegroundColor Yellow
        $events | ForEach-Object {
            Write-Host "Time: $($_.TimeCreated)"
            Write-Host "Level: $($_.Level)"
            Write-Host "Message: $($_.Message)"
            Write-Host ""
        }
    }
} catch {
    # Event log access may fail, continue silently
}

# Cleanup
Remove-Item -Recurse -Force $DiagDir -ErrorAction SilentlyContinue

exit 1
