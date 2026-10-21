<p align="center"><img src="src/EldoUI.Updater/Assets/logo.png" width="96" alt="EldoUI troll logo"></p>

# EldoUI Updater

A small Windows app that keeps [EldoUI](https://github.com/Eldodeldo/EldoUI) up to date for **WoW: Forever**.
It sits in the notification area with the EldoUI troll icon and, when a new EldoUI is released, installs it
in your AddOns folder by itself. Your EldoUI settings stay as they are.

- **Download:** the newest `EldoUI-Updater.exe` from [Releases](https://github.com/Eldodeldo/EldoUI-updater-app/releases/latest)
  or the EldoUI website.
- **Needs:** Windows 10 or 11. Nothing else to install.
- **Size:** under half a megabyte. It checks for a new EldoUI every three hours and otherwise does nothing.

## Using it

1. Run `EldoUI-Updater.exe`. It installs itself for your Windows user (no admin rights) and opens its window.
2. It looks for WoW: Forever (`World of Warcraft\_classic_beta_`). If it cannot find it, click **Choose folder…**
   and pick your `World of Warcraft` folder.
3. That is all. If EldoUI is not installed yet, the updater installs it. When a new version comes out, it
   installs that too and tells you. Type `/reload` in the game if it is open.

Right-click the troll icon for the menu: check now, choose the WoW folder, open the AddOns folder,
install updates automatically (on or off), start with Windows (on or off), language, and exit.

**Windows SmartScreen** may say "Windows protected your PC" the first time, because the exe is not
code-signed. Click **More info → Run anyway**. The source is all here, and every release lists the exe's
SHA-256 checksum in `SHA256SUMS.txt`.

**To remove it:** Settings → Apps → Installed apps → EldoUI Updater → Uninstall. EldoUI itself stays in the game.

## How it works

- Asks GitHub's public API for the newest release of `Eldodeldo/EldoUI` (no account, no token, nothing
  sent about you). Unchanged answers are cached, so it stays far below GitHub's limits.
- Downloads `EldoUI-<version>.zip` from github.com over HTTPS and checks its size and the SHA-256 that GitHub
  computed for the file.
- Checks the zip: only `EldoUI` folders (`EldoUI`, `EldoUI_*`), each with its own `.toc`, no unsafe paths.
- Unpacks it next to AddOns, then swaps each folder in. If anything fails, the old folders are put back.
  A folder a new release no longer has is removed, but only if the updater installed it. Other addons
  and the game's `WTF` folder (your settings) are never touched.
- Updates itself the same way from this repository's releases.

Files on your PC:

| What | Where |
| --- | --- |
| The program | `%LOCALAPPDATA%\Programs\EldoUI Updater\` |
| Settings and log (`updater.log`) | `%LOCALAPPDATA%\EldoUI Updater\` |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `EldoUI Updater` |

## Building

.NET Framework 4.8 WinForms, built with the plain .NET SDK (8 or newer) on Windows or Linux:

```
dotnet test tests/EldoUI.Updater.Tests
dotnet build src/EldoUI.Updater -c Release
```

The exe is `src/EldoUI.Updater/bin/Release/net48/EldoUI Updater.exe`. On Windows it also takes
`--selftest` (checks the install logic in a temp folder) and `--snapshot <folder>` (pictures of the window).

`src/Core` has everything without UI (GitHub answer, versions, zip checks and install, WoW folders,
settings) and is what the tests cover. `src/EldoUI.Updater` is the tray app and its window.

## Releasing

Raise `<Version>` in `src/EldoUI.Updater/EldoUI.Updater.csproj`, add a section for it at the top of
`CHANGELOG.md`, and merge. The `build` workflow publishes the release (`v<version>` with
`EldoUI-Updater.exe` and `SHA256SUMS.txt`), and installed updaters pick it up within a day.

## License

Apache License 2.0, like EldoUI. EldoUI is not made by or affiliated with Blizzard Entertainment.
World of Warcraft is a trademark of Blizzard Entertainment, Inc.
