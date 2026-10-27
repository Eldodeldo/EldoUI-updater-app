#!/bin/bash
# Runs inside a distro container as an ordinary user (uid 1000). Follows the README's Linux install:
# download, make runnable, run; then checks the first start, a login, a second start and --uninstall.
# Usage: player-test.sh <fuse|extract> <expected EldoUI version> <prefix kind> <expect: ok|blocked> <release folder>
MODE="$1"; WANT="$2"; KIND="${3:-proton}"; EXPECT="${4:-ok}"; REL="/kit/${5:-release}"
pass=0; fail=0
ok()   { echo "PASS  $*"; pass=$((pass+1)); }
bad()  { echo "FAIL  $*"; fail=$((fail+1)); }
info() { echo "INFO  $*"; }
check() { local what="$1"; shift; if "$@" >/dev/null 2>&1; then ok "$what"; else bad "$what"; fi; }
# Updater processes, read from /proc (minimal images have no pgrep).
procs() { local n=0 f; for f in /proc/[0-9]*/cmdline; do tr '\0' ' ' < "$f" 2>/dev/null | grep -q "eldoui-updater" && n=$((n+1)); done; echo $n; }
stopall() { local f; for f in /proc/[0-9]*/cmdline; do tr '\0' ' ' < "$f" 2>/dev/null | grep -q "eldoui-updater" && kill "$(basename "$(dirname "$f")")" 2>/dev/null; done; sleep 2; }

[ "$MODE" = extract ] && export APPIMAGE_EXTRACT_AND_RUN=1
cd "$HOME" || exit 2
info "os: $(. /etc/os-release; echo "$PRETTY_NAME")"
info "glibc: $(getconf GNU_LIBC_VERSION 2>/dev/null)"
info "user: $(id -un) home=$HOME mode=$MODE prefix=$KIND expect=$EXPECT"
info "fusermount: $(command -v fusermount3 || command -v fusermount || echo none)"

# 1. Download and make runnable, as a player does.
mkdir -p Downloads
cp "$REL/EldoUI-Updater-x86_64.AppImage" "$REL/SHA256SUMS.txt" Downloads/
( cd Downloads && check "download matches SHA256SUMS.txt" sha256sum -c --ignore-missing SHA256SUMS.txt )
chmod +x Downloads/EldoUI-Updater-x86_64.AppImage
APP="$HOME/Downloads/EldoUI-Updater-x86_64.AppImage"
if out=$("$APP" --appimage-version 2>&1); then ok "AppImage runtime starts"; else bad "AppImage runtime starts: $out"; fi

# 2. The updater's own requirement check, and the self-test.
"$APP" --check-system > check-system.txt 2>&1; rc=$?
sed 's/^/      /' check-system.txt
if [ "$EXPECT" = ok ]; then [ $rc -eq 0 ] && ok "--check-system: everything needed is here" || bad "--check-system (exit $rc)"
else [ $rc -eq 1 ] && ok "--check-system names what is missing (exit 1)" || bad "--check-system (exit $rc)"; fi
"$APP" --selftest > selftest.txt 2>&1; rc=$?
sed 's/^/      /' selftest.txt
[ $rc -eq 0 ] && grep -q "selftest passed" selftest.txt && ok "--selftest passed" || bad "--selftest (exit $rc)"

# 3. A WoW: Forever install in a Wine prefix, laid out the way each launcher does it (empty game folder;
#    the updater creates Interface/AddOns).
case "$KIND" in
	proton)        PFX="$HOME/.local/share/Steam/steamapps/compatdata/2000000001/pfx" ;;
	flatpak-steam) PFX="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/2000000001/pfx" ;;
	lutris)        PFX="$HOME/Games/battlenet" ;;
	sdcard)        LIB="$HOME/sdcard"; PFX="$LIB/steamapps/compatdata/2000000001/pfx"
	               mkdir -p "$HOME/.local/share/Steam/steamapps"
	               printf '"libraryfolders"\n{\n\t"0"\n\t{\n\t\t"path"\t\t"%s"\n\t}\n\t"1"\n\t{\n\t\t"path"\t\t"%s"\n\t}\n}\n' "$HOME/.local/share/Steam" "$LIB" > "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf" ;;
esac
GAME="$PFX/drive_c/Program Files (x86)/World of Warcraft/_forever_"
mkdir -p "$GAME"
ADDONS="$GAME/Interface/AddOns"
INST="$HOME/.local/share/eldoui-updater/EldoUI-Updater.AppImage"
MENU="$HOME/.local/share/applications/eldoui-updater.desktop"
AUTO="$HOME/.config/autostart/eldoui-updater.desktop"
LOG="$HOME/.local/state/eldoui-updater/updater.log"

# 4. First start from the download.
start=$(date +%s)
"$APP" > first-start.txt 2>&1; rc=$?
if [ "$EXPECT" = blocked ]; then
	sed 's/^/      /' first-start.txt
	[ $rc -eq 1 ] && ok "first start stops with a message (exit 1)" || bad "first start (exit $rc)"
	grep -q "fontconfig" first-start.txt && ok "the message names the missing fontconfig library and how to install it" || bad "the message does not name fontconfig"
	check "nothing installed (no ~/.local/share/eldoui-updater)" test ! -e "$HOME/.local/share/eldoui-updater"
	check "no login entry written" test ! -e "$AUTO"
	check "no app menu entry written" test ! -e "$MENU"
	check "the log says why" grep -q "Cannot start" "$LOG"
	[ "$(procs)" -eq 0 ] && ok "no updater process left running" || bad "updater still running"
	cp "$LOG" updater.log 2>/dev/null
	echo "RESULT pass=$pass fail=$fail"; exit 0
fi
[ $rc -eq 0 ] && ok "first start returns after installing (exit 0)" || { bad "first start (exit $rc)"; sed 's/^/      /' first-start.txt; }
check "installed to ~/.local/share/eldoui-updater/EldoUI-Updater.AppImage" test -x "$INST"
perm=$(stat -c %a "$INST" 2>/dev/null); [ "$perm" = 700 ] && ok "installed file is runnable by this user only (mode 700)" || bad "installed file mode is $perm"
check "app menu entry written" test -f "$MENU"
check "login entry written, starting with --tray" grep -q -- "--tray" "$AUTO"
check "icon written" test -s "$HOME/.local/share/eldoui-updater/eldoui-updater.png"
if [ "$MODE" = extract ]; then check "without FUSE the entries start the updater unpacked" grep -q "^Exec=env APPIMAGE_EXTRACT_AND_RUN=1 " "$AUTO"
else check "with FUSE the entries start the AppImage directly" grep -q '^Exec="' "$AUTO"; fi
cp "$MENU" menu.desktop 2>/dev/null; cp "$AUTO" autostart.desktop 2>/dev/null
command -v desktop-file-validate >/dev/null && { desktop-file-validate "$MENU" && ok "app menu entry passes desktop-file-validate" || bad "desktop-file-validate"; }

# 5. The installed copy finds the prefix and installs the newest EldoUI on its own (auto-install is on).
for i in $(seq 1 150); do
	[ -x "$INST" ] || break
	[ -f "$ADDONS/EldoUI/EldoUI.toc" ] && break
	sleep 1
done
if [ -f "$ADDONS/EldoUI/EldoUI.toc" ]; then
	v=$(grep -m1 '^## Version:' "$ADDONS/EldoUI/EldoUI.toc" | sed 's/^## Version: *//' | tr -d '\r')
	ok "EldoUI installed into the prefix's AddOns by itself after $(( $(date +%s) - start ))s (version $v)"
	[ "$v" = "$WANT" ] && ok "installed EldoUI is the newest release ($WANT)" || bad "installed EldoUI is $v, newest is $WANT"
	info "AddOns now holds: $(ls "$ADDONS" | tr '\n' ' ')"
	touch "$HOME/.shot-ready"; sleep 12
else
	bad "EldoUI was not installed into $ADDONS within 150s"
fi
[ "$(procs)" -ge 1 ] && ok "the installed updater is running" || bad "the installed updater is not running"
check "log written" test -s "$LOG"
check "settings written" test -s "$HOME/.config/eldoui-updater/settings.json"

# 6. Log out and in: the login entry's Exec line, run with the bare environment a desktop session gives it.
stopall
exe=$(grep '^Exec=' "$AUTO" | sed 's/^Exec=//')
before=$(grep -c "Started" "$LOG")
env -i HOME="$HOME" USER="$USER" DISPLAY="$DISPLAY" XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" PATH=/usr/local/bin:/usr/bin:/bin \
	HTTPS_PROXY="$HTTPS_PROXY" SSL_CERT_FILE="$SSL_CERT_FILE" sh -c "$exe" > login.txt 2>&1 &
sleep 12
after=$(grep -c "Started" "$LOG")
[ "$(procs)" -ge 1 ] && [ "$after" -gt "$before" ] && ok "after a new login the updater starts from its login entry" || { bad "login entry did not start the updater"; sed 's/^/      /' login.txt | head -5; }

# 7. Starting it again (app menu, or the downloaded file) does not start a second copy.
n1=$(procs)
timeout 60 "$INST" > second-start.txt 2>&1; rc=$?
sleep 2
n2=$(procs)
[ $rc -eq 0 ] && [ "$n2" -le "$n1" ] && ok "second start hands over to the running copy (exit 0)" || bad "second start (exit $rc, before $n1 after $n2)"

cp "$LOG" updater.log 2>/dev/null

# 8. Remove it: EldoUI stays in the game.
timeout 60 "$INST" --uninstall > uninstall.txt 2>&1; rc=$?
sleep 3
[ $rc -eq 0 ] && ok "--uninstall exits 0 ($(tr -d '\n' < uninstall.txt))" || bad "--uninstall (exit $rc)"
check "program folder removed" test ! -e "$HOME/.local/share/eldoui-updater"
check "app menu and login entries removed" test ! -e "$MENU" -a ! -e "$AUTO"
check "settings and log removed" test ! -e "$HOME/.config/eldoui-updater" -a ! -e "$HOME/.local/state/eldoui-updater"
check "EldoUI stays in the game" test -f "$ADDONS/EldoUI/EldoUI.toc"
[ "$(procs)" -eq 0 ] && ok "no updater process left running" || bad "$(procs) updater process(es) still running"
echo "RESULT pass=$pass fail=$fail"
