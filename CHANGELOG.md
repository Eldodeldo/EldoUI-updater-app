# Changelog

Each release of the EldoUI Updater. The newest is at the top.

## [1.2.6] - 2026-10-24

### New
- Check for an updater update directly beside This app in the open window. The button shows checking progress and switches to Update now when an update is available.

## [1.2.5] - 2026-10-24

### Fixed
- Installing recommended addons from the Extra addons menu no longer closes the menu with an application error.

## [1.2.4] - 2026-10-24

### New
- Choose companion addons individually in the Extra addons menu, or install all three EldoUI recommendations at once. Their official Forever releases are checked alongside EldoUI.
- The Extra addons menu links to Details! on CurseForge for a separate manual installation.
- Companion updates preserve your own files, including extra RestedXP guides, and keep each addon's update results separate.

## [1.2.3] - 2026-10-24

### Fixed
- Point at a failed update status to see its error details and retry.
- A temporary connection failure no longer forgets a pending updater self-update.
- Choosing a game folder now rejects other WoW versions and unrelated Interface/AddOns folders.
- Manual checks from the tray show their progress and result, including when a check is already running.
- An unreadable installed version reports a failed check and can be retried without restarting the updater.
- Select a fresh _forever_ game folder to install EldoUI; Interface and AddOns are created automatically.
- Official EldoUI releases containing the Danish language addon now install correctly.

## [1.2.2] - 2026-10-22

### New
- The first time the window opens, a short notice explains what the updater does, that it only talks to GitHub, and how to turn off automatic installs or the start at login. It shows once.
- Release files come with a build provenance record, so anyone can check that a download was built from this repository. The Windows exe is ready to be code-signed.

### Improved
- Links from the updater open only pages on GitHub.
- On Linux, the installed AppImage can be run only by your own user.

## [1.2.1] - 2026-10-22

### New
- "Check for an app update" in the tray menu checks for a new version of the updater right away and always tells you the result: a new version is out (it installs at once and the updater restarts), you already have the newest version, or GitHub could not be reached.

### Improved
- The menu's EldoUI link now opens EldoUI's page on GitHub, where every release and its notes are.

## [1.2.0] - 2026-10-22

### Improved
- The updater checks for updates right when it starts, also at login, and installs what it finds at once: a new EldoUI and a new version of the updater itself. It happens quietly, with no notice and no window, and then the updater runs as usual. If the network is not up yet, it tries again every minute for ten minutes.
- A new version of the updater found while it is running still installs itself 24 hours later, or at once with "Update now".
- The restart into a new version runs hidden, so no console or window flashes. When you start the updater yourself, its window opens once that first check is done, so an update never closes it again.

## [1.1.1] - 2026-10-21

### Improved
- EldoUI is installed only when the download matches the checksum published with its release: the one GitHub keeps for the file, or a checksum file in the release. If a release has no checksum, or the download does not match it, nothing in your AddOns folder changes; the window says so, and the updater tries again at the next check.
- The updater's own updates follow the same rule.

### Fixed
- An EldoUI pre-release is replaced by the full release of the same version at the next check, instead of counting as up to date.

## [1.1.0] - 2026-10-21

### New
- EldoUI Updater for Linux, as one AppImage file. It finds WoW: Forever inside Wine, Lutris, Bottles and Steam (Proton) and keeps EldoUI up to date there, with the same window, tray icon, languages and quiet self-update as on Windows.
- On Linux it adds itself to the app menu and starts when you log in (you can turn that off).

## [1.0.3] - 2026-10-21

### Improved
- Finds the released WoW: Forever (its `_forever_` folder) wherever it is installed, and still the beta's `_classic_beta_` folder. It asks Battle.net where your games are before looking in the usual places on every drive.
- When the released game appears, the updater moves from the beta folder to it by itself and installs EldoUI there.

## [1.0.2] - 2026-10-21

### New
- The updater keeps itself up to date too. A new version of the app shows quietly in the window ("This app") and in the menu, never as a pop-up, and installs itself 24 hours later. "Update now" in the window or menu installs it at once.

### Improved
- No notice after the app has updated itself; the window shows the version it runs.

## [1.0.1] - 2026-10-21

### Fixed
- A new EldoUI release whose download is not attached yet no longer shows as a failed update. The updater installs the newest release that has its download and checks again later.

## [1.0.0] - 2026-10-21

### New
- Keeps EldoUI up to date by itself: when a new EldoUI is released, the updater downloads it, checks it and puts it in your AddOns folder. Your settings in the game stay as they are.
- Lives in the notification area with the EldoUI troll icon. Left-click opens the window; right-click opens the menu.
- Finds WoW: Forever on its own, or you choose the folder.
- Starts with Windows (you can turn it off), and can just tell you about a new version instead of installing it.
- English and Danish.
- Installs for your Windows user only, without admin rights, and can be removed from Settings → Apps.
