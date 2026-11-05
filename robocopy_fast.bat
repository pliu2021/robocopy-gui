@echo off
setlocal enabledelayedexpansion

set source=ftp://vigem_guest@10.104.2.51/2026-06-26_14-22-23/Trigger/
set destination=D:\20260626
set keywords=CV_DRIVING_DEBUG,IPN_IPC,MRR40_DEBUG,SRR50_DEBUG,FAS_DLT_LOW,IPN_DEBUG,BN_RADETH,IPBETH,MT_RE,MRR40_RAW,SRR50_RAW

for /d %%i in ("%source%\*") do (
    set folderName=%%~nxi
    set matchFound=0
    for %%k in (%keywords%) do (
        if not "!folderName!"=="!folderName:%%k=!" (
            set matchFound=1
        )
    )
    if "!matchFound!"=="1" (
        echo Copying .MF4 files from folder: %%i
        robocopy "%%i" "%destination%\%%~nxi" *.MF4 /E
    ) else (
        echo Skipping folder: %%i
    )
)
pause
endlocal