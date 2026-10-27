#!/bin/bash
# The whole matrix: desktop images of each family, two desktops without FUSE, a Steam Deck setup (Arch,
# read-only system, "deck" user, game in an SD-card Steam library), and plain images without a desktop,
# where the updater must stop with a message before it installs anything; then three clean installs.
# Usage: run-all.sh [AppImage]   (default: the newest release from GitHub)
# Needs Docker, Xvfb, xwd and ImageMagick's convert on the host, and /dev/fuse.
set -eu
KIT="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$KIT/release"
if [ $# -ge 1 ]; then
	cp "$1" "$KIT/release/EldoUI-Updater-x86_64.AppImage"
	(cd "$KIT/release" && sha256sum EldoUI-Updater-x86_64.AppImage > SHA256SUMS.txt)
else
	base=https://github.com/Eldodeldo/EldoUI-updater-app/releases/latest/download
	curl -fsSL "$base/EldoUI-Updater-x86_64.AppImage" -o "$KIT/release/EldoUI-Updater-x86_64.AppImage"
	curl -fsSL "$base/SHA256SUMS.txt" -o "$KIT/release/SHA256SUMS.txt"
fi
export ELDOUI_VERSION="${ELDOUI_VERSION:-$(curl -fsSL https://api.github.com/repos/Eldodeldo/EldoUI/releases/latest | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p')}"
echo "EldoUI release to expect: $ELDOUI_VERSION"
R="${REGISTRY:-docker.io}"
UB="sh /kit/ubuntu-desktop-libs.sh"
cd "$KIT"
./run-target.sh ubuntu-24.04 "$R/library/ubuntu:24.04" 101 fuse proton "$UB libssl3t64 fuse3 desktop-file-utils" &
./run-target.sh ubuntu-22.04 "$R/library/ubuntu:22.04" 102 fuse lutris "$UB libssl3 fuse3" &
./run-target.sh mint-22 "$R/linuxmintd/mint22-amd64" 103 fuse proton &
./run-target.sh debian-12-desktop "$R/kasmweb/core-debian-bookworm:1.16.0" 104 fuse proton &
./run-target.sh fedora-desktop "$R/linuxserver/webtop:fedora-xfce" 105 fuse flatpak-steam &
./run-target.sh arch-desktop "$R/linuxserver/webtop:arch-xfce" 106 fuse lutris &
./run-target.sh opensuse-leap-15.5-desktop-nofuse "$R/kasmweb/core-opensuse-15:1.16.0" 107 extract proton &
wait
./run-target.sh ubuntu-24.04-nofuse "$R/library/ubuntu:24.04" 114 extract proton "$UB libssl3t64" &
USER_HOME=/home/deck ./run-target.sh steamdeck "$R/linuxserver/webtop:arch-xfce" 113 fuse sdcard true \
	"--read-only --tmpfs /tmp:exec,mode=1777 --tmpfs /run --tmpfs /home/deck:exec,mode=755,uid=1000,gid=1000" &
EXPECT=blocked ./run-target.sh debian-13-plain "$R/library/debian:13" 108 extract proton &
EXPECT=blocked ./run-target.sh fedora-43-plain "$R/library/fedora:43" 109 extract proton &
EXPECT=blocked ./run-target.sh opensuse-tumbleweed-plain "$R/opensuse/tumbleweed" 110 extract proton &
EXPECT=blocked ./run-target.sh opensuse-leap-15.6-plain "$R/opensuse/leap:15.6" 111 extract proton &
EXPECT=blocked ./run-target.sh arch-plain "$R/library/archlinux" 112 extract proton &
wait
# Clean installs: the updater first, the game later (in Lightforge's default library, or in a Lightforge
# library on a second drive that only Lightforge's status names).
export TEST_SCRIPT=clean-test.sh
./run-target.sh clean-ubuntu-24.04 "$R/library/ubuntu:24.04" 140 fuse default "$UB libssl3t64 fuse3" &
./run-target.sh clean-fedora-lightforge "$R/linuxserver/webtop:fedora-xfce" 141 fuse lightforge &
USER_HOME=/home/deck ./run-target.sh clean-steamdeck "$R/linuxserver/webtop:arch-xfce" 142 fuse default true \
	"--read-only --tmpfs /tmp:exec,mode=1777 --tmpfs /run --tmpfs /home/deck:exec,mode=755,uid=1000,gid=1000" &
wait
unset TEST_SCRIPT
grep -h "^RESULT" results/*/result.txt >/dev/null
for d in results/*/; do echo "$(basename "$d"): $(tail -1 "$d/result.txt")"; done
! grep -q "^FAIL" results/*/result.txt
