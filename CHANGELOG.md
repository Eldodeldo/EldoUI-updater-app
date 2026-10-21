# Changelog

Each release of the EldoUI Updater. The newest is at the top.

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
