# Release identity

The updater selects one published EldoUI release and keeps that release's
version, notes and download together until the check completes. Documentation
changes alone do not trigger an addon install.

Issue #33 tracks the release identity audit. Its implementation checks the
selected zip's version against the selected release before installing, and
preserves the existing size and SHA-256 verification. The addon release,
updater release and optional companion releases remain separate products.

Validation includes successive four-digit addon releases, an incorrectly named
zip, a mismatched version inside a verified zip, and a newer release published
while an earlier check downloads. Native release rollout is verified separately
after the owner merges and publishes the resulting changes.

## Selection and installation

- Only the selected release's `EldoUI-<version>.zip` (or the existing
  `EldoUI-v<version>.zip` spelling) at that release's official public download
  URL is considered. An unrelated or older zip does not make a release ready.
- A check keeps its selected release while downloading. A release published
  during that download is considered at the next check.
- The existing published size and SHA-256 checks run before extraction. The
  staged main TOC must then match the selected version before any old addon
  folder moves. A missing or different TOC version leaves the install intact.
- Updated notes for an already installed release are returned by the next
  check, without reinstalling the same version.
- Wiki and changelog automation remains upstream. The updater reads published
  GitHub releases directly and requires no separate EldoUI JSON feed.

## Audit evidence

Four new regression cases failed before the correction: wrong asset version,
wrong release URL, and older/newer content inside a checksum-valid archive.
The new success/race test already passed and is retained as a regression guard.
Additional cases cover missing TOC versions and fallback to a complete older
release. Platform self-tests also check rejection before replacement.

The Windows .NET Framework self-test and shared unit tests were run locally.
Both platform projects compile; the Linux runtime self-test and actual public
release rollout remain separate CI/owner checks.

## Complete package

From EldoUI 0.7.1000 the release zip holds the main addon and five companion
addons that load only when needed: `EldoUI_Emoji`, `EldoUI_GuideData`,
`EldoUI_Locale_daDK`, `EldoUI_LootData` and `EldoUI_LootSim`. Each is a
sibling of `EldoUI` with its own matching TOC. The developer companion is not
part of the standard package. A companion that the game has not loaded yet is
installed and complete all the same.

- One trusted SHA-256 covers the whole zip, so every folder in it is verified
  before anything is unpacked, including companions the player never loads.
- Only the main TOC must match the selected release version. Companion TOCs
  keep their own versions and are never required to equal the main version.
- A wrong or missing checksum, an unsafe path in any folder, a folder without
  its matching TOC, a folder that is not EldoUI's, or a main TOC with another
  version leaves the existing install, WTF and other addons byte for byte as
  they were.
- If a move fails partway, every folder that already moved is put back. The
  next check installs the release again.
- An upgrade replaces only the folders in the zip and removes only folders
  the updater installed earlier that the new release no longer has. Saved
  settings in WTF, the player's in-game addon choices, other addons and
  EldoUI folders the updater did not install (such as a developer companion)
  stay untouched. A fresh addon profile is the addon's own one-time step; the
  updater never deletes saved settings for it.

`tests/EldoUI.Updater.Tests/FullPackageTests.cs` covers these cases. To check
a published release zip end to end in a temporary game folder:

```sh
ELDOUI_RELEASE_ZIP=/path/to/EldoUI-<version>.zip \
ELDOUI_RELEASE_VERSION=<version> \
ELDOUI_RELEASE_SHA256=<published sha256> \
ELDOUI_PREVIOUS_ZIP=/path/to/EldoUI-<previous>.zip \
dotnet test tests/EldoUI.Updater.Tests --filter ReleasePackageTests --logger "console;verbosity=detailed"
```

`ELDOUI_PREVIOUS_ZIP` is optional and checks the upgrade from that release.
The expected folder list is `FullPackage.Companions` in that file; change it
together with the published package.
