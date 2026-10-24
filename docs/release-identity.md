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
