# Code signing and build provenance

Release files are built by the `build` workflow on GitHub, never on a personal computer. Two things let
anyone check that a download is the file this workflow built.

## Build provenance

Every public release gets a provenance attestation for `EldoUI-Updater.exe` and
`EldoUI-Updater-x86_64.AppImage`: a signed statement from GitHub that this repository's workflow built
them. Check a download with the GitHub CLI:

```
gh attestation verify EldoUI-Updater.exe --repo Eldodeldo/EldoUI-updater-app
```

Nothing has to be set up for this; the workflow does it for every release.

## Code signing (Windows)

The exe is signed through [SignPath](https://signpath.io), which signs open-source projects for free
through the SignPath Foundation. Until then the exe is unsigned, and Windows SmartScreen shows "Windows
protected your PC" on the first start.

Once signing is set up for this repository, every release exe is signed, and a release whose signature
is not valid is not published.

Check a signed exe on Windows: right-click it → Properties → Digital Signatures, or in PowerShell
`Get-AuthenticodeSignature .\EldoUI-Updater.exe`.
