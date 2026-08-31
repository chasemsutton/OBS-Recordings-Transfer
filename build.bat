@echo off
setlocal

echo Building ODBC Recordings Transfer...
cd /d "%~dp0ODBC.RecordingsTransfer"

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "..\publish"

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b 1
)

echo.
echo Build complete! Output is in publish\
echo Run build-installer.bat to also create the Setup.exe installer.

endlocal
