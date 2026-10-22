<p align="center"><img src="src/EldoUI.Updater/Assets/logo.png" width="96" alt="EldoUI troll logo"></p>

# EldoUI Updater

A small tray app for Windows and Linux that keeps [EldoUI](https://github.com/Eldodeldo/EldoUI) up to date
for **WoW: Forever**. When a new EldoUI is released, it installs it in your AddOns folder by itself. Your
EldoUI settings stay as they are.

- **Download:** [latest release](https://github.com/Eldodeldo/EldoUI-updater-app/releases/latest):
  `EldoUI-Updater.exe` for Windows, `EldoUI-Updater-x86_64.AppImage` for Linux.
- **Needs:** Windows 10 or 11, or a 64-bit Linux desktop. Nothing else to install, no admin rights.
- **Size:** under half a megabyte on Windows, about 40 MB on Linux (it carries its own runtime).

## Install

**Windows**

1. Run `EldoUI-Updater.exe`. It installs itself for your Windows user and opens its window.
2. It finds WoW: Forever by itself (the `_forever_` folder, or `_classic_beta_` during the beta). If it
   cannot, click **Choose folder…** and pick your `World of Warcraft` folder.
3. That is all. It installs EldoUI if it is missing, and every new version after that. Type `/reload` in
   the game if it is open.

Windows SmartScreen may say "Windows protected your PC" the first time while the exe is not code-signed.
Click **More info → Run anyway**. Every release lists the files' SHA-256 checksums in `SHA256SUMS.txt`, and
you can check that a file was built by this repository (see [SIGNING.md](SIGNING.md)).

**Linux**

WoW: Forever runs through Wine, so the updater looks inside Wine prefixes: `$WINEPREFIX`, `~/.wine`,
Lutris, Bottles and Steam's Proton prefixes (native and Flatpak).

1. Download `EldoUI-Updater-x86_64.AppImage` and make it runnable (file manager: Properties → Allow
   executing as a program, or `chmod +x EldoUI-Updater-x86_64.AppImage`).
2. Run it. It installs itself for your user, adds an app menu entry and starts when you log in. You can
   delete the downloaded file afterwards.
3. If it cannot find the game, click **Choose folder…** and pick the `World of Warcraft` folder or the
   Wine prefix that holds it.

On GNOME, the tray icon needs the *AppIndicator and KStatusNotifierItem Support* extension (Ubuntu has it).
Without a tray the window opens instead. If the AppImage mentions FUSE, install your distribution's `fuse3`
package.

## The menu

Right-click the troll icon: check for updates now, check for an app update (it always tells you what it
found), what's new, choose the WoW folder, open the AddOns folder, install updates automatically (on or
off), start with Windows or at login (on or off), language (English or Danish), EldoUI on GitHub, and exit.

## How it updates

- It checks when it starts and every three hours after that.
- It asks GitHub for the newest EldoUI release and downloads `EldoUI-<version>.zip` from github.com.
- The download must match the SHA-256 checksum published with the release (GitHub's own checksum for the
  file, or a `SHA256SUMS.txt` / `<file>.sha256` in the release). Without a checksum, or when the file does
  not match, nothing is installed and the window says so.
- The zip may only hold EldoUI folders (`EldoUI`, `EldoUI_<Name>`), each with its own `.toc`. They are
  unpacked next to AddOns first and then swapped in; if anything fails, the old folders are put back.
- It manages only EldoUI's own folders: the ones in the zip and the ones it installed before. Other addons,
  any other folder in AddOns and the game's `WTF` folder (your settings) are never touched.
- The updater updates itself from this repository's releases, with the same checksum rule.

## Privacy and safety

- **Sends:** only `GET` requests to GitHub's public API (`api.github.com`) and release downloads from
  `github.com`. No account, no token, no telemetry, nothing about you or your PC.
- **Reads:** where your games are (Battle.net's settings and installed-games list, the registry on
  Windows, Wine prefixes on Linux), and the `.toc` of the installed EldoUI to know its version.
- **Opens:** only pages on `github.com` (release notes, EldoUI on GitHub). Any other link is refused.
- **First start:** a short notice says what the updater does and how to turn its automatic parts off.
- **Writes:** EldoUI's folders in AddOns, and its own files (on Linux, the installed AppImage is runnable
  only by your user):

| What | Windows | Linux |
| --- | --- | --- |
| The program | `%LOCALAPPDATA%\Programs\EldoUI Updater\` | `~/.local/share/eldoui-updater/` |
| Settings | `%LOCALAPPDATA%\EldoUI Updater\` | `~/.config/eldoui-updater/settings.json` |
| Log (`updater.log`) | `%LOCALAPPDATA%\EldoUI Updater\` | `~/.local/state/eldoui-updater/` |
| Start automatically | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `~/.config/autostart/eldoui-updater.desktop` |
| Uninstall entry / app menu | Settings → Apps | `~/.local/share/applications/eldoui-updater.desktop` |

## Remove it

- **Windows:** Settings → Apps → Installed apps → EldoUI Updater → Uninstall.
- **Linux:** `~/.local/share/eldoui-updater/EldoUI-Updater.AppImage --uninstall`.

EldoUI itself stays in the game either way.

## Building

The Windows app is .NET Framework 4.8 WinForms; the Linux app is .NET 8 with Avalonia, packed as an
AppImage. Both build with the .NET SDK 8 or newer:

```
dotnet test tests/EldoUI.Updater.Tests
dotnet build src/EldoUI.Updater -c Release
bash packaging/linux/build-appimage.sh publish
```

`src/Core` holds everything without UI (GitHub answers, versions, checksums, zip checks and install, WoW
folders, Wine prefixes, settings) and is what the tests cover. Both apps take `--selftest` and
`--snapshot <folder>`.

To release, raise `<Version>` in `src/Directory.Build.props`, add its section at the top of `CHANGELOG.md`
and merge. The build workflow publishes `v<version>` with both files and `SHA256SUMS.txt`.

Bug reports and ideas: [Issues](https://github.com/Eldodeldo/EldoUI-updater-app/issues). For EldoUI itself,
use the [EldoUI issues](https://github.com/Eldodeldo/EldoUI/issues).

## License and credits

Apache License 2.0 (see `LICENSE` and `NOTICE`). Made by Eldodeldo, as an AI-made project like EldoUI.

EldoUI is not made by or affiliated with Blizzard Entertainment. World of Warcraft is a trademark of
Blizzard Entertainment, Inc.
