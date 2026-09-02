# OBS Recordings Transfer

Windows desktop app that transfers remuxed OBS MP4 recordings from a source folder to a destination and cleans up old MKV files.

## Features

- Compact/expandable GUI with transfer queue, live progress, and activity log
- **Transfer modes** (radios above Run Transfer):
  - **Manual start** — run only when you click Run Transfer
  - **Auto-start** — one transfer after launch (delay is in Settings)
  - **Continuous** — keep transferring remux-ready MP4s; **Stop Transfer** cancels work and returns to Manual start
- Wait for OBS remux completion (stable size + `moov`) before moving; ready files transfer first while others keep waiting
- Queue updates while a transfer is running (e.g. waiting → move when remux finishes)
- Old MKV cleanup when a matching MP4 exists (age / free-space rules)
- Continuous helpers: start with Windows, start minimized
- In-app updates via GitHub Releases (Stable channel, or Beta version picker with rollback)
- Settings organized with Advanced / Unsupported options (MD5 and FFmpeg verify)
- Self-contained — no separate .NET install required
- Windows installer with Add/Remove Programs support

## Build

```bat
build.bat              REM app only → publish\
build-installer.bat    REM app + installer → installer-output\
```

## Releasing an update

1. Bump the version in `OBS.RecordingsTransfer/OBS.RecordingsTransfer.csproj`
2. Commit and push
3. Create and push a tag:

```bat
git tag v2.0.1
git push origin v2.0.1
```

GitHub Actions builds the installer and publishes it to [Releases](https://github.com/chasemsutton/OBS-Recordings-Transfer/releases). Installed copies check for updates automatically (or via **Check for Updates** in the app).

**Update channels:**
- **Stable** — latest non-prerelease release (`/releases/latest`)
- **Beta** — choose any compatible GitHub prerelease (upgrade or downgrade). Manual **Check for Updates** opens a version picker; startup only prompts when a newer beta exists.

**Release markers** (optional HTML comments in the GitHub release body):

```text
<!-- update-from: 2.2.13 -->
<!-- compat-min: 2.2.0 -->
```

- **`update-from`** — minimum installed version that may use the in-app updater to reach this release. Older installs are told to uninstall and reinstall from GitHub Releases instead.
- **`compat-min`** — floor for the beta version picker (hide older/broken betas when testing rollbacks). The app also ships `AppCompatibility.MinCompatibleVersion` (currently `2.2.0`); the effective floor is the higher of the two.

When you ship a breaking installer/update-path change, put `update-from: <this version>` on that release (and later ones as needed). When you ship a breaking config/data change that blocks safe downgrade, bump `AppCompatibility.MinCompatibleVersion`.

```bat
git tag v2.3.6
git push origin v2.3.6
```

Then mark the GitHub release as a prerelease for the beta channel.

## Config

Settings are stored in `%LocalAppData%\OBS Recordings Transfer\config.txt`.

## Install

Run `installer-output\OBS Recordings Transfer Setup.exe` (or download the latest installer from GitHub Releases).

### Windows Application Control blocked the app?

If you see **"An Application Control policy has blocked this file"**, the install usually still succeeded. Windows may block unsigned executables (for example with Smart App Control on Windows 11, or AppLocker/WDAC on managed PCs).

**Right now:** check the Start Menu for **OBS Recordings Transfer** — the app may already be at `C:\Program Files\OBS Recordings Transfer\`.

**To run it without the warning**, you may need one of:

1. **Allow the install path** — add an exception for `C:\Program Files\OBS Recordings Transfer\OBS Recordings Transfer.exe`
2. **Code signing** — a signed build avoids most of these prompts (requires a code-signing certificate)
3. **Smart App Control** — on Windows 11, turn it off in Settings or allow the app when prompted

The installer itself may also be blocked until you allow it or use a signed build.
