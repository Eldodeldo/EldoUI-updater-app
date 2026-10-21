# Changelog

Each release of the EldoUI Updater. The newest is at the top.

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
