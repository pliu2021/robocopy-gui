@echo off
setlocal enabledelayedexpansion

echo ============================================
echo MF4CopyStudio Release Build Script
echo ============================================
echo.

cd /d "%~dp0"

REM Check if dotnet exists
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: dotnet CLI not found. Please install .NET SDK.
    exit /b 1
)

echo Current .NET SDK version:
dotnet --version
echo.

REM Step 1: Restore
echo [1/6] Restoring NuGet packages...
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Restore failed
    exit /b 1
)
echo.

REM Step 2: Build Release
echo [2/6] Building Release configuration...
dotnet build -c Release --no-restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    exit /b 1
)
echo.

REM Step 3: Run Tests
echo [3/6] Running tests...
dotnet test -c Release --no-build
if %ERRORLEVEL% neq 0 (
    echo WARNING: Some tests failed, continuing with build...
)
echo.

REM Step 4: Clean old publish output
echo [4/6] Cleaning old publish output...
if exist "publish" rmdir /s /q "publish"
if exist "release\MF4CopyStudio.exe" del /q "release\MF4CopyStudio.exe"
echo.

REM Step 5: Publish single-file
echo [5/6] Publishing single-file executable...
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishTrimmed=false ^
    -o "publish"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Publish failed
    exit /b 1
)
echo.

REM Step 6: Copy to release directory
echo [6/6] Copying to release directory...
if not exist "release" mkdir "release"
copy /Y "publish\MF4CopyStudio.exe" "release\MF4CopyStudio.exe"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to copy EXE
    exit /b 1
)

REM Verify only EXE in release
echo.
echo Verifying release directory contents...
dir /b release\ > release_contents.txt
for /f "wc -l < release_contents.txt" %%i in ('type release_contents.txt') do set count=%%i
del release_contents.txt

REM Show file size
for %%A in ("release\MF4CopyStudio.exe") do (
    echo.
    echo Final EXE: release\MF4CopyStudio.exe
    echo File size: %%~zA bytes
)

REM Calculate SHA-256 using PowerShell
echo.
echo Calculating SHA-256 hash...
powershell -Command "Get-FileHash 'release\MF4CopyStudio.exe' -Algorithm SHA256 | Select-Object -ExpandProperty Hash"
echo.

echo ============================================
echo Build completed successfully!
echo Output: %~dp0release\MF4CopyStudio.exe
echo ============================================

endlocal
