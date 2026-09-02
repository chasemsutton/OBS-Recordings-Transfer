@echo off
setlocal

set ROOT=%~dp0
set PUBLISH=%ROOT%publish
set PAYLOAD=%ROOT%OBS.RecordingsTransfer.Installer\Payload
set OUTPUT=%ROOT%installer-output
set FFMPEG_SRC=%ROOT%FFmpeg

echo ============================================
echo  OBS Recordings Transfer - Full Build
echo ============================================
echo.

echo [1/5] Publishing application...
cd /d "%ROOT%OBS.RecordingsTransfer"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH%"
if %ERRORLEVEL% NEQ 0 (
    echo Application publish failed!
    exit /b 1
)

echo.
echo [2/5] Bundling FFmpeg (if available)...
if exist "%FFMPEG_SRC%\bin\ffmpeg.exe" (
    xcopy "%FFMPEG_SRC%" "%PUBLISH%\FFmpeg\" /E /I /Y /Q >nul
    echo FFmpeg copied into publish folder.
) else (
    echo FFmpeg not found at expected path - skipping. Remux validation will use C:\FFmpeg if installed.
)

echo.
echo [3/5] Packaging payload...
if not exist "%PAYLOAD%" mkdir "%PAYLOAD%"
if exist "%PAYLOAD%\app.zip" del "%PAYLOAD%\app.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH%\*' -DestinationPath '%PAYLOAD%\app.zip' -Force"
if %ERRORLEVEL% NEQ 0 (
    echo Failed to create installer payload zip!
    exit /b 1
)

echo.
echo [4/5] Building installer...
cd /d "%ROOT%OBS.RecordingsTransfer.Installer"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT%"
if %ERRORLEVEL% NEQ 0 (
    echo Installer build failed!
    exit /b 1
)

echo.
echo [5/5] Cleaning up intermediate payload...
del "%PAYLOAD%\app.zip" 2>nul

echo.
echo ============================================
echo  Done!
echo  App:       publish\OBS Recordings Transfer.exe
echo  Installer: installer-output\OBS Recordings Transfer Setup.exe
echo ============================================

endlocal
