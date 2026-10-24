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
