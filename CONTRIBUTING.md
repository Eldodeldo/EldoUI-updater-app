# Contributing

Thanks for helping with the EldoUI Updater.

- **Bugs and ideas:** open an [issue](https://github.com/Eldodeldo/EldoUI-updater-app/issues). For EldoUI
  itself (the addon), use the [EldoUI issues](https://github.com/Eldodeldo/EldoUI/issues).
- **Code:** open an issue first, then a pull request to `main`. Keep the change focused, add a unit test in
  `tests/EldoUI.Updater.Tests` for anything in `src/Core`, and run `dotnet test tests/EldoUI.Updater.Tests`.
- **Texts:** every player text exists in English and Danish (`src/EldoUI.Updater/Strings.cs`).
- **Changelog:** add a line in plain player language under the new version in `CHANGELOG.md`.

The updater only talks to GitHub, installs only files with a matching published checksum, and touches only
EldoUI's own folders, the explicitly enabled companion packages' scoped folders, and the addon folders (and, when
the box is ticked, their own saved settings) that the player confirmed to uninstall. EldoUI's own folders are
never uninstalled. Changes must keep all of this.

By contributing, you agree that your contribution is licensed under the Apache License 2.0.
