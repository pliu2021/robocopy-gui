param(
    [string]$ExePath = (Join-Path $PSScriptRoot "..\release-candidate\RobocopyTool.exe"),
    [int]$TimeoutSeconds = 10
)

Write-Host "Testing: $ExePath"
Write-Host "Timeout: $TimeoutSeconds seconds"

$process = Start-Process -FilePath $ExePath -ArgumentList "--verify-ui" -PassThru -WindowStyle Hidden
$startTime = Get-Date

Start-Sleep -Milliseconds 500

# Check if process is still running (good sign - means it didn't crash immediately)
for ($i = 0; $i -lt $TimeoutSeconds * 2; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $proc = Get-Process -Id $process.Id -ErrorAction Stop
        if ($proc.HasExited) {
            Write-Host "Process exited with code: $($proc.ExitCode)"
            if ($proc.ExitCode -eq 0) {
                Write-Host "SUCCESS: Application started and exited cleanly" -ForegroundColor Green
                exit 0
            } else {
                Write-Host "FAILED: Application exited with error code $($proc.ExitCode)" -ForegroundColor Red
                exit 1
            }
        }
    } catch {
        Write-Host "Process no longer exists (crashed or closed)"
        exit 1
    }
}

# If we get here, process is still running after timeout
Write-Host "Application is still running after $TimeoutSeconds seconds"
Write-Host "SUCCESS: No startup crash detected" -ForegroundColor Green

# Try to close gracefully
Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue

exit 0
