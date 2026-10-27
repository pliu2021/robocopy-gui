@echo off
echo Cleaning build artifacts...

cd /d "%~dp0"

if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "Tests\bin" rmdir /s /q "Tests\bin"
if exist "Tests\obj" rmdir /s /q "Tests\obj"
if exist "publish" rmdir /s /q "publish"
if exist "release\MF4CopyStudio.exe" del /q "release\MF4CopyStudio.exe"

echo Clean completed.
echo Source code and user settings preserved.
