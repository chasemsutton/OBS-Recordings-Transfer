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

### Windows Application Control blocked the app?

If you see **"An Application Control policy has blocked this file"**, the install usually still succeeded. The PC is blocking unsigned executables (common on church/media workstations with AppLocker, WDAC, or Smart App Control).

**On the media PC right now:** check the Start Menu for **ODBC Recordings Transfer** — the app may already be at `C:\Program Files\ODBC Recordings Transfer\`.

**To allow it permanently**, your IT admin needs one of:

1. **Whitelist the install path** — allow `C:\Program Files\ODBC Recordings Transfer\ODBC Recordings Transfer.exe`
2. **Code signing** — sign the exe with an organization code-signing certificate (best long-term fix)
3. **Smart App Control** — if enabled on Windows 11, it blocks unsigned apps until IT adds an exception

The installer itself may also be blocked until IT allows it or the app is signed.
