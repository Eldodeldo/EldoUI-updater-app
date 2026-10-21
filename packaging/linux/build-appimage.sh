#!/usr/bin/env bash
# Builds EldoUI-Updater-x86_64.AppImage: the Linux app published as one self-contained file (no .NET to
# install), wrapped in an AppImage with its icon and app menu entry.
# Usage: bash packaging/linux/build-appimage.sh [output folder]   (default: publish)
# Needs the .NET 8 SDK, curl and desktop-file-validate (package desktop-file-utils). appimagetool is
# downloaded once into the work folder.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(mkdir -p "${1:-publish}" && cd "${1:-publish}" && pwd)"
WORK="$ROOT/packaging/linux/.work"
APPDIR="$WORK/AppDir"
# A pinned release of appimagetool, so a build today and a build next month use the same tool.
TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.0/appimagetool-x86_64.AppImage"

command -v desktop-file-validate >/dev/null || { echo "desktop-file-validate is missing: install desktop-file-utils" >&2; exit 1; }

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$WORK"

dotnet publish "$ROOT/src/EldoUI.Updater.Linux" -c Release -r linux-x64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
	-o "$WORK/bin" --nologo
cp "$WORK/bin/eldoui-updater" "$APPDIR/usr/bin/eldoui-updater"
chmod 755 "$APPDIR/usr/bin/eldoui-updater"

cp "$ROOT/src/EldoUI.Updater.Linux/Assets/logo.png" "$APPDIR/eldoui-updater.png"
cp "$APPDIR/eldoui-updater.png" "$APPDIR/.DirIcon"
cat > "$APPDIR/eldoui-updater.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=EldoUI Updater
Comment=Keeps EldoUI up to date for WoW: Forever
Exec=eldoui-updater
Icon=eldoui-updater
Terminal=false
Categories=Game;Utility;
DESKTOP
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/eldoui-updater" "$@"
APPRUN
chmod 755 "$APPDIR/AppRun"

if [ ! -x "$WORK/appimagetool" ]; then
	curl -fsSL "$TOOL_URL" -o "$WORK/appimagetool"
	chmod 755 "$WORK/appimagetool"
fi
# No FUSE on build machines: let the tool unpack itself instead of mounting.
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 "$WORK/appimagetool" --no-appstream "$APPDIR" "$OUT/EldoUI-Updater-x86_64.AppImage"
chmod 755 "$OUT/EldoUI-Updater-x86_64.AppImage"
ls -l "$OUT/EldoUI-Updater-x86_64.AppImage"
