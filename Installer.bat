@echo off
setlocal enabledelayedexpansion

REM --- Must match your project name, assuming the .cer and .msix files have the same name (which they should) this script can be reused safely.
set "PROJECT_NAME=Vanilla RTX App"

title %PROJECT_NAME% Installation Helper
REM --- Working dir to script's folder
pushd "%~dp0"
echo.
echo =============================================
echo  %PROJECT_NAME% Installation Helper
echo =============================================
echo.

REM --- Get admin privileges if not already elevated
:RequestAdmin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~fs0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    
    REM --- Wait a moment for the elevated process to start
    timeout /t 1 >nul
    
    REM --- Original window should exit here since new elevated window is running
    echo.
    echo An elevated window should have opened. If not,
    echo either UAC Admin request was denied or there was an irregularity.
    echo.
    echo Press any key to try requesting admin privileges again. You can also close this window to abort the installation.
    pause >nul
    goto :RequestAdmin
)

REM --- We have admin privileges, continue with installation
echo Running with administrator privileges...
echo.

REM --- Find the .cer file recursively
set "cer_file="
for /r %%f in ("%PROJECT_NAME%*.cer") do (
    if not defined cer_file (
        set "cer_file=%%f"
    ) else (
        echo ERROR: More than one matching .cer file found.
        echo Found: !cer_file!
        echo Found: %%f
        pause
        exit /b 1
    )
)

if not defined cer_file (
    echo WARNING: No .cer files belonging to %PROJECT_NAME% found in this folder.
    echo Skipping certificate installation...
    echo.
) else (
    echo Installing certificate: !cer_file!
    certutil -addstore "TrustedPeople" "!cer_file!" >nul
    if !errorlevel! neq 0 (
        echo ERROR: Failed to install certificate.
        goto :ManualCertInstructions
    )
    echo SUCCESS: Certificate installed!
    echo.
)

REM --- Find the .msix file recursively
set "msix_file="
for /r %%f in ("%PROJECT_NAME%*.msix") do (
    if not defined msix_file (
        set "msix_file=%%f"
    ) else (
        echo ERROR: More than one matching .msix file found.
        echo Found: !msix_file!
        echo Found: %%f
        pause
        exit /b 1
    )
)

if not defined msix_file (
    echo ERROR: No .msix file starting with %PROJECT_NAME% found in this folder.
    pause
    exit /b 1
)

echo Opening MSIX package: !msix_file!
start "" "!msix_file!"
echo.
echo This window will close in 10 seconds...
timeout /t 10 >nul
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