# ODBC Recordings Transfer

Windows desktop app for Open Door Baptist Church Media Ministry. Transfers MP4 recordings from a source folder to a destination and cleans up old MKV files.

## Features

- GUI for configuring paths and running transfers
- MP4 transfer with optional MD5 verification
- Old MKV cleanup when matching MP4 exists
- Optional FFmpeg remux validation
- Self-contained — no separate .NET install required
- In-app updates via GitHub Releases
- Windows installer with Add/Remove Programs support

## Build

```bat
build.bat              REM app only → publish\
build-installer.bat    REM app + installer → installer-output\
```

## Releasing an update

1. Bump the version in `ODBC.RecordingsTransfer/ODBC.RecordingsTransfer.csproj`
2. Commit and push
3. Create and push a tag:

```bat
git tag v2.0.1
git push origin v2.0.1
```

GitHub Actions builds the installer and publishes it to [Releases](https://github.com/chasemsutton/ODBC-Recordings-Transfer/releases). Installed copies check for updates automatically (or via **Check for Updates** in the app).

## Config

Settings are stored in `config.txt` next to the executable.

## Install

Run `installer-output\ODBC Recordings Transfer Setup.exe` (or download the latest installer from GitHub Releases).
