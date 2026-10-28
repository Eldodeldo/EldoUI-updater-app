<p align="center"><img src="src/EldoUI.Updater/Assets/logo.png" width="96" alt="EldoUI troll logo"></p>

# EldoUI Updater

A small tray app for Windows and Linux that keeps [EldoUI](https://github.com/Eldodeldo/EldoUI) up to date
for **WoW: Forever**. When a new EldoUI is released, it installs it in your AddOns folder by itself. Your
EldoUI settings stay as they are. It can also keep other addons you choose up to date (**Extra addons**),
and on Linux it helps you install the game itself (**WoW Linux setup**).

- **Download:** [latest release](https://github.com/Eldodeldo/EldoUI-updater-app/releases/latest):
  `EldoUI-Updater.exe` for Windows, `EldoUI-Updater-x86_64.AppImage` for Linux.
- **Needs:** Windows 10 or 11, or a 64-bit Linux desktop. Nothing else to install, no admin rights.
- **Size:** under half a megabyte on Windows, about 40 MB on Linux (it carries its own runtime).

## Install

**Windows**

1. Run `EldoUI-Updater.exe`. It installs itself for your Windows user and opens its window.
2. It finds WoW: Forever by itself (the `_forever_` folder, or `_classic_beta_` during the beta). If it
   cannot, click **Choose folder…** and pick your `_forever_` game folder. Interface/AddOns is created when needed.
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
Without a tray the window opens instead.

Every desktop distribution has what the updater needs. Before it installs itself, it checks for a graphical
session and the fontconfig and X11 libraries; if one is missing it installs nothing and shows the install
command for your distribution. To check a system yourself, run
`./EldoUI-Updater-x86_64.AppImage --check-system`.

If the AppImage says it cannot mount itself (FUSE), either install your distribution's `fuse3` package or
start it once with `APPIMAGE_EXTRACT_AND_RUN=1 ./EldoUI-Updater-x86_64.AppImage`. It then unpacks itself on
each start, from the app menu and at login too, which takes a few seconds longer.

Tested with each release on Ubuntu, Linux Mint, Debian, Fedora, Arch, openSUSE and a Steam Deck setup: see
[Tested Linux distributions](https://github.com/Eldodeldo/EldoUI-updater-app/wiki/Tested-Linux-distributions).

No WoW on Linux yet? See [WoW Linux setup](#wow-linux-setup) below.

The **This app** row also has a **Check update** button. It shows **Checking…** during a request
and becomes **Update now** when a newer updater is ready.

## The menu

Right-click the troll icon: check for updates now, check for an app update (it always tells you what it
found), what's new, choose the WoW folder, open the AddOns folder, WoW Linux setup (Linux only),
install updates automatically (on or off), start with Windows or at login (on or off), language (English or
Danish), EldoUI on GitHub, and exit.

## How it updates

- It checks when it starts and every three hours after that.
- It asks GitHub for the newest EldoUI release and downloads `EldoUI-<version>.zip` from github.com.
- The download must match the SHA-256 checksum published with the release (GitHub's own checksum for the
  file, or a `SHA256SUMS.txt` / `<file>.sha256` in the release). Without a checksum, or when the file does
  not match, nothing is installed and the window says so.
- The zip may only hold EldoUI folders (`EldoUI`, `EldoUI_<Name>`), each with its own `.toc`. They are
  unpacked next to AddOns first and then swapped in; if anything fails, the old folders are put back.
- It manages only EldoUI's own folders: the ones in the zip and the ones it installed before. Other addons
  are managed only after you choose them in **Extra addons** (below). The game's `WTF` folder (your
  settings) is never touched, except for an addon's own saved settings when you uninstall it and tick the
  box for them.
- The updater updates itself from this repository's releases, with the same checksum rule.

## Privacy and safety

- **Sends:** only `GET` requests to GitHub's public API (`api.github.com`) and release downloads from
  `github.com`. For an addon you add from WoWInterface or TukUI, it also asks that site's public version
  API (`api.mmoui.com`, `api.tukui.org`) for that addon only. No account, no token, no telemetry, nothing
  about you or your PC.
- **Reads:** where your games are (Battle.net's settings and installed-games list, the registry on
  Windows, Wine prefixes on Linux), the `.toc` files in AddOns (to show what is installed and what it
  needs), and on Linux, when you open WoW Linux setup, your distribution, glibc version, fonts and free
  space.
- **Opens:** pages on `github.com` and the addon publishers' own pages (CurseForge, WoWInterface, TukUI)
  for addons in the catalog. Other external pages are refused.
- **First start:** a short notice says what the updater does and how to turn its automatic parts off.
- **Writes:** EldoUI's folders and the folders of addons you chose in AddOns, and its own files (on Linux,
  the installed AppImage is runnable only by your user). WoW Linux setup writes Lightforge's
  files only when you click its install button (see below).
- **Deletes:** only addon folders you chose to uninstall in **Extra addons** and confirmed, and their saved
  settings only when you ticked the box for them.

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
folders, Wine prefixes, settings, the addon catalog, sources and scan) and is what the tests cover, with the
engine of WoW Linux setup (`src/EldoUI.Updater.Linux/Lightforge.cs`). The catalog is rebuilt with
`tools/build_catalog.py`, and the notes on what EldoUI does for each addon with `tools/build_coverage.py`
from the source of each EldoUI release and the reviewed answers in `tools/coverage-review.json`. Both apps take `--selftest` and
`--snapshot <folder>`; the Linux app also takes `--check-system`.

To release, raise `<Version>` in `src/Directory.Build.props`, add its section at the top of `CHANGELOG.md`
and merge. The build workflow publishes `v<version>` with both files and `SHA256SUMS.txt`.

The Linux install is tested on several distributions with `tests/linux-distros/` (see its README).

Bug reports and ideas: [Issues](https://github.com/Eldodeldo/EldoUI-updater-app/issues). For EldoUI itself,
use the [EldoUI issues](https://github.com/Eldodeldo/EldoUI/issues).

## Extra addons

Click **Extra addons…** in the updater window. **Find addons** lists the catalog: about a hundred addons
for WoW: Forever, each with the place its author publishes it. Tick an addon to have the updater install
it and keep it up to date with EldoUI. **Install selected addons** installs the ticked ones now;
**Install recommended** adds DBM, RestedXP and WeakAuras SharedMedia. Automatic installation follows
**Install updates automatically**; when that is off, **Update now** applies what is waiting.

The window can be resized. Drag the edge between two column headings to change a column's width, and click
a heading to sort the list by it; the window keeps its size and column widths for next time.

- **Installs only what it can check.** An addon is installed only from its author's GitHub release, only
  when the download has a published SHA-256 checksum, and only when it is made for WoW: Forever (a Forever
  file, release manifest or `.toc`). A download that holds programs or scripts, links, folders outside the
  addon, or folders another addon already uses is refused before anything changes.
- **WoWInterface and TukUI** publish no checksum, so their addons are never installed by the updater. When
  you add one, the updater checks it for new versions and links to its page.
- **Addons that only have a page** (CurseForge, for example) show a link to that page.
- **What EldoUI already does.** A gold note (in the catalog and in the Installed list) names the EldoUI
  modules that matter for an addon: "EldoUI already does this" when a module does the addon's whole job,
  "EldoUI does part of this" when a module covers some of it, and "Works with EldoUI" when EldoUI is made
  to work alongside it. You can skip an addon EldoUI already covers, or turn the module off if you prefer
  the addon. Every catalog addon is checked against EldoUI, and the notes are updated with each EldoUI
  release.
- **Your own copy stays yours.** If an addon is already in AddOns, put there by hand, the updater says so
  and does nothing until you click **Update it** and confirm. Files you added to its folders are kept.
- **Choices per addon** (the **⋯** button): keep it at your version, take pre-releases, open its page, or
  stop updating it (it stays installed).
- **Installed** shows everything in your AddOns folder grouped by addon: EldoUI, the Eldostar guide data,
  addons the updater keeps up to date, and the rest, with each addon's size and when it was last installed
  or updated. It shows addons not made for Forever and missing dependencies, and offers a catalog addon
  that another addon needs. Hide rows you do not want to see. Its **⋯** menu also opens the addon's folder,
  and for an addon the updater keeps up to date, **Reinstall it** installs the current version again.
- **Uninstall** any addon in **Installed** from its **⋯** menu, or tick several and click **Uninstall
  selected**. The updater first asks, listing the folders that go and the installed addons that need them.
  The addon's saved settings in the game's `WTF` folder stay unless you tick **Also delete its saved
  settings**; close the game first if you do, since it writes them again when you log out. The folders are
  moved aside first, so a folder in use stops the whole uninstall and nothing is lost. An addon the updater
  kept up to date is taken off its list, so it is not installed again. EldoUI's own folders and the
  Eldostar guide data cannot be uninstalled here: the updater looks after them.
- **Import and Export** save your choices to a small file (`eldoui-addons.json`) with catalog names only,
  to move them to another PC.
- Updates keep files you added, install into a staging folder first and put the old folders back if
  anything fails; a backup that cannot be put back is kept, not deleted.

## WoW Linux setup

On Linux, **WoW Linux setup…** (window or tray menu) helps you install Battle.net and WoW: Forever with
[Lightforge](https://github.com/LightforgeProject/lightforge), a free, separate program by its own authors
(GPL-3.0-or-later).

1. The assistant checks your system: distribution, x86_64, glibc 2.36 or newer, fonts, 32-bit support and
   free space. Anything missing is shown with what to do; installing system packages is up to you.
2. **Install Lightforge** downloads Lightforge 1.0.0-beta.7 from its GitHub release, checks it against the
   SHA-256 in the release's checksum list (and the value built into the updater), and puts `lightforge`,
   `lightforge-gui`, its app menu entry and icons in `~/.local/bin` and `~/.local/share`, the same places
   Lightforge's own installer uses. Nothing is piped into a shell, no password is asked for, and a failed
   install leaves the earlier files in place.
3. **Open Lightforge** opens its own window, where you set up Proton, Battle.net and the game. The assistant
   never starts Battle.net or the game.
4. The assistant shows what Lightforge reports, with read-only commands only. When WoW: Forever is
   installed there, **Use its WoW: Forever** points the updater at it.

Lightforge connects to Blizzard's servers, its project's own servers (`tuxcore.pro`: artwork, its catalog
and a download proxy), GitHub (Proton downloads) and, when you use its addon features, CurseForge,
WoWInterface and TukUI. The updater sends nothing to these. A newer Lightforge version comes with an update
of the updater; until then the assistant reads only the version it knows.

## License and credits

Apache License 2.0 (see `LICENSE` and `NOTICE`). Made by Eldodeldo, as an AI-made project like EldoUI.

The addon catalog is adapted from [AddonForge](https://github.com/Topher121/AddonForge) (MIT License,
Copyright (c) 2026 Pocket Forge Studios; full notice in `NOTICE`). [Lightforge](https://github.com/LightforgeProject/lightforge)
is a separate program (GPL-3.0-or-later) that WoW Linux setup downloads unchanged; it is not part
of the updater.

EldoUI is not made by or affiliated with Blizzard Entertainment. World of Warcraft is a trademark of
Blizzard Entertainment, Inc.
