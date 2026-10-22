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
through the SignPath Foundation. Until signing is set up, the workflow builds an unsigned exe exactly as
before, and Windows SmartScreen shows "Windows protected your PC" on the first start.

To turn it on, a maintainer:

1. Applies for the SignPath Foundation's open-source program with this repository and connects it to
   SignPath as a GitHub trusted build system.
2. Adds the repository secret `SIGNPATH_API_TOKEN` (Settings → Secrets and variables → Actions → Secrets).
3. Adds the repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and
   `SIGNPATH_POLICY_SLUG` (same page, Variables tab) with the values SignPath shows for the project.

From the next release on, the workflow sends the exe to SignPath, waits for the signed file, checks that
its signature is valid and publishes that file. If signing fails, the release is not published. Pull
requests are never signed.

Check a signed exe on Windows: right-click it → Properties → Digital Signatures, or in PowerShell
`Get-AuthenticodeSignature .\EldoUI-Updater.exe`.
