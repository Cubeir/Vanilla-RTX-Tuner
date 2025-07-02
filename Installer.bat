@echo off
setlocal enabledelayedexpansion
title Installing Vanilla RTX Tuner

REM -- Working dir to script's folder
pushd "%~dp0"

echo.
echo ========================================
echo  Vanilla RTX Tuner Installation Helper
echo ========================================
echo.

REM --- Check for Admin Privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Administrator privileges are required
    echo .
    echo Please close this window and follow these steps:
    echo 1. Right-click on this batch file
    echo 2. Select "Run as administrator"
    echo.
    pause
    exit /b 1
)

REM --- Find the .cer file
set "cer_file="
for %%f in (*.cer) do (
    if not defined cer_file (
        set "cer_file=%%f"
    ) else (
        echo ERROR: More than one .cer file found.
        pause
        exit /b 1
    )
)

if not defined cer_file (
    echo ERROR: No .cer file found in this folder.
    pause
    exit /b 1
)

echo Installing certificate: !cer_file!
certutil -addstore "TrustedPeople" "!cer_file!" >nul

if !errorlevel! neq 0 (
    echo ERROR: Failed to install certificate.
    goto :ManualCertInstructions
)

echo SUCCESS: Certificate installed!
echo.

REM --- Find the .msix file
set "msix_file="
for %%f in (*.msix) do (
    if not defined msix_file (
        set "msix_file=%%f"
    ) else (
        echo ERROR: More than one .msix file found.
        pause
        exit /b 1
    )
)

if not defined msix_file (
    echo ERROR: No .msix file found in this folder.
    pause
    exit /b 1
)

echo Opening MSIX package: !msix_file!
start "" "!msix_file!"

echo.
echo This window will close in 5 seconds...
timeout /t 5 >nul
exit /b 0

:ManualCertInstructions
echo Please install the certificate manually:
echo.
echo 1. Open the certificate file: "!cer_file!"
echo 2. Click **Install Certificate**
echo 3. Choose **Local Machine** > Next
echo 4. Select **Place all certificates in the following store**
echo 5. Browse to **Trusted People**, then click OK
echo 6. Complete the wizard, then rerun this installer
echo.
pause
exit /b 1
