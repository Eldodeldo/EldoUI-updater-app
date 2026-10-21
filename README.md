<p align="center"><img src="src/EldoUI.Updater/Assets/logo.png" width="96" alt="EldoUI troll logo"></p>

# EldoUI Updater

A small app for Windows and Linux that keeps [EldoUI](https://github.com/Eldodeldo/EldoUI) up to date for
**WoW: Forever**. It sits in the notification area (the system tray) with the EldoUI troll icon and, when a new EldoUI is released, installs it
in your AddOns folder by itself. Your EldoUI settings stay as they are.

- **Download:** from [Releases](https://github.com/Eldodeldo/EldoUI-updater-app/releases/latest) or the EldoUI
  website: `EldoUI-Updater.exe` for Windows, `EldoUI-Updater-x86_64.AppImage` for Linux.
- **Needs:** Windows 10 or 11, or a 64-bit Linux desktop. Nothing else to install.
- **Size:** under half a megabyte on Windows, about 40 MB on Linux (it carries its own runtime). It checks
  for a new EldoUI every three hours and otherwise does nothing.

## Using it

1. Run `EldoUI-Updater.exe`. It installs itself for your Windows user (no admin rights) and opens its window.
2. It looks for WoW: Forever wherever you installed it: the `_forever_` folder of the released game, or
   `_classic_beta_` during the beta. It asks Battle.net where your games are, reads the registry and looks in
   the usual places on every drive. If it cannot find it, click **Choose folder…** and pick your
   `World of Warcraft` folder. When the released game appears next to the beta, the updater moves to it by itself.
3. That is all. If EldoUI is not installed yet, the updater installs it. When a new version comes out, it
   installs that too and tells you. Type `/reload` in the game if it is open.

Right-click the troll icon for the menu: check now, check for an app update (it always says what it found), choose the WoW folder, open the AddOns folder,
install updates automatically (on or off), start with Windows (on or off), language, and exit.

**Windows SmartScreen** may say "Windows protected your PC" the first time, because the exe is not
code-signed. Click **More info → Run anyway**. The source is all here, and every release lists the exe's
SHA-256 checksum in `SHA256SUMS.txt`.

**To remove it:** Settings → Apps → Installed apps → EldoUI Updater → Uninstall. EldoUI itself stays in the game.

## Linux

WoW: Forever runs on Linux through Wine, so the updater looks for it inside Wine prefixes: `$WINEPREFIX`,
`~/.wine`, Lutris (games in `~/Games` and every prefix in Lutris' game files), Bottles, and Steam's Proton
prefixes in every Steam library (native and Flatpak installs). In each prefix it asks Battle.net where the
games are, then looks in the usual places, and picks `_forever_` before `_classic_beta_`.

1. Download `EldoUI-Updater-x86_64.AppImage` and make it runnable, in your file manager (Properties →
   Allow executing as a program) or in a terminal:
   ```
   chmod +x EldoUI-Updater-x86_64.AppImage
   ```
2. Run it. It installs itself for your user, adds itself to the app menu and to the apps that start when
   you log in, and opens its window. You can delete the downloaded file afterwards.
3. If it cannot find the game, click **Choose folder…** and pick the `World of Warcraft` folder (or the Wine
   prefix that holds it).

The troll icon sits in the system tray. KDE, Cinnamon, XFCE and most others show it as it is; on GNOME,
install the *AppIndicator and KStatusNotifierItem Support* extension (Ubuntu has it already). Without a tray
the window opens instead, and closing it leaves the updater running.

If the AppImage does not start and mentions FUSE, install your distribution's `fuse3` package, or run it
once with `--appimage-extract-and-run` to try it.

**To remove it:** run it with `--uninstall`
(`~/.local/share/eldoui-updater/EldoUI-Updater.AppImage --uninstall`). EldoUI itself stays in the game.

## How it works

- Asks GitHub's public API for the newest release of `Eldodeldo/EldoUI` (no account, no token, nothing
  sent about you). Unchanged answers are cached, so it stays far below GitHub's limits.
- Downloads `EldoUI-<version>.zip` from github.com over HTTPS and checks its size and its SHA-256 against the
  checksum published with the release: the one GitHub computed for the file, or a checksum file in the release
  (`SHA256SUMS.txt` or `<file>.sha256`). A release without a checksum, or a download that does not match it, is
  never installed: nothing in AddOns changes, the window says so, and the updater tries again at the next check.
- Checks the zip: only `EldoUI` folders (`EldoUI`, `EldoUI_*`), each with its own `.toc`, no unsafe paths.
- Unpacks it next to AddOns, then swaps each folder in. If anything fails, the old folders are put back.
  The updater manages only EldoUI's own folders: the ones in the zip and the ones it installed earlier. A
  folder a new release no longer has is removed, but only if the updater installed it. Every other folder in
  AddOns (other addons, and any EldoUI folder that is neither in the zip nor installed by the updater) and
  the game's `WTF` folder (your settings) are never touched.
- A pre-release (a version such as `1.2.3-beta`) counts as older than the release of the same number, so the
  release replaces it at the next check.
- Checks right when it starts (at login too): a new EldoUI, and a new version of the updater itself, install at
  once and quietly, with no notice, no window and no console, and then the updater runs as usual. If the network
  is not up yet, it tries again every minute for ten minutes.
- Updates itself from this repository's releases, with the same checksum rule. A new version found while it is
  running shows quietly in the window and menu (no pop-up) and installs itself 24 hours later, or at once with
  "Update now". The restart into the new version is hidden too.

Files on Windows:

| What | Where |
| --- | --- |
| The program | `%LOCALAPPDATA%\Programs\EldoUI Updater\` |
| Settings and log (`updater.log`) | `%LOCALAPPDATA%\EldoUI Updater\` |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `EldoUI Updater` |

Files on Linux:

| What | Where |
| --- | --- |
| The program and its icon | `~/.local/share/eldoui-updater/` |
| Settings | `~/.config/eldoui-updater/settings.json` |
| Log (`updater.log`) | `~/.local/state/eldoui-updater/` |
| App menu entry | `~/.local/share/applications/eldoui-updater.desktop` |
| Start when you log in | `~/.config/autostart/eldoui-updater.desktop` |

## Building

.NET Framework 4.8 WinForms, built with the plain .NET SDK (8 or newer) on Windows or Linux:

```
dotnet test tests/EldoUI.Updater.Tests
dotnet build src/EldoUI.Updater -c Release
```

The exe is `src/EldoUI.Updater/bin/Release/net48/EldoUI Updater.exe`. On Windows it also takes
`--selftest` (checks the install logic in a temp folder) and `--snapshot <folder>` (pictures of the window).

The Linux app is .NET 8 with Avalonia, published as one self-contained file and wrapped in an AppImage
(needs `desktop-file-utils`):

```
bash packaging/linux/build-appimage.sh publish
```

It also takes `--selftest` and `--snapshot <folder>` (rendered without a display).

`src/Core` has everything without UI (GitHub answer, versions, zip checks and install, WoW folders, Wine
prefixes, settings) and is what the tests cover; both apps compile it. `src/EldoUI.Updater` is the Windows
tray app and its window, `src/EldoUI.Updater.Linux` the Linux one (same texts, design and behaviour).

## Releasing

Raise `<Version>` in `src/Directory.Build.props` (shared by both apps), add a section for it at the top of
`CHANGELOG.md`, and merge. The `build` workflow publishes the release (`v<version>` with
`EldoUI-Updater.exe`, `EldoUI-Updater-x86_64.AppImage` and `SHA256SUMS.txt`), and installed updaters pick it
up within a day.

## License

Apache License 2.0, like EldoUI. EldoUI is not made by or affiliated with Blizzard Entertainment.
World of Warcraft is a trademark of Blizzard Entertainment, Inc.
