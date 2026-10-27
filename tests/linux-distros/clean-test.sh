#!/bin/bash
# A clean install: no Wine prefix and no game when the updater is installed. The game appears later, the
# way Lightforge leaves it after installing WoW: Forever. The updater must find it and install EldoUI by itself.
# Usage: clean-test.sh <mode> <expected EldoUI> <default|lightforge> <unused> <release folder>
MODE="$1"; WANT="$2"; KIND="$3"; REL="/kit/${5:-release}"
pass=0; fail=0
ok()   { echo "PASS  $*"; pass=$((pass+1)); }
bad()  { echo "FAIL  $*"; fail=$((fail+1)); }
[ "$MODE" = extract ] && export APPIMAGE_EXTRACT_AND_RUN=1
cd "$HOME"
echo "INFO  os: $(. /etc/os-release; echo "$PRETTY_NAME") case=$KIND"
mkdir -p Downloads && cp "$REL/EldoUI-Updater-x86_64.AppImage" Downloads/ && chmod +x Downloads/EldoUI-Updater-x86_64.AppImage
if [ "$KIND" = lightforge ]; then
	# Lightforge installed by the WoW Linux setup, its game library on a second drive. This stand-in answers
	# Lightforge's three read-only commands in Lightforge 1.0.0-beta.7's own format.
	GAME="$HOME/drive2/Blizzard/games/World of Warcraft/_forever_"
	mkdir -p .local/bin
	cat > .local/bin/lightforge <<STUB
#!/bin/sh
case "\$*" in
	"--version") echo "lightforge 1.0.0-beta.7" ;;
	"setup --status") echo "ready           true" ;;
	"status --product wowf --offline")
		if [ -e "\$HOME/.lf-game-installed" ]; then inst=true; else inst=false; fi
		printf 'product         wowf\ninstall dir     %s\ninstalled       %s\n' "$GAME" "\$inst" ;;
	*) exit 2 ;;
esac
STUB
	chmod 755 .local/bin/lightforge
else
	GAME="$HOME/Games/Blizzard/games/World of Warcraft/_forever_"
fi
ADDONS="$GAME/Interface/AddOns"
LOG="$HOME/.local/state/eldoui-updater/updater.log"
"$HOME/Downloads/EldoUI-Updater-x86_64.AppImage" > first-start.txt 2>&1 && ok "first start on a clean home installs the updater" || bad "first start"
sleep 20
grep -q "Found WoW" "$LOG" && bad "a game was reported before one existed" || ok "no game yet: the updater waits (no folder chosen)"
# The game arrives.
mkdir -p "$GAME"; touch "$HOME/.lf-game-installed"
start=$(date +%s)
limit=$([ "$KIND" = lightforge ] && echo 420 || echo 120)
for i in $(seq 1 $limit); do [ -f "$ADDONS/EldoUI/EldoUI.toc" ] && break; sleep 1; done
if [ -f "$ADDONS/EldoUI/EldoUI.toc" ]; then
	v=$(grep -m1 '^## Version:' "$ADDONS/EldoUI/EldoUI.toc" | sed 's/^## Version: *//' | tr -d '\r')
	ok "EldoUI $v installed by itself $(( $(date +%s) - start ))s after the game appeared"
	[ "$v" = "$WANT" ] && ok "it is the newest release ($WANT)" || bad "installed $v, newest is $WANT"
else
	bad "EldoUI not installed within ${limit}s after the game appeared"
fi
grep -E "Found WoW|Lightforge|Installed EldoUI" "$LOG" | sed 's/^/      /'
cp "$LOG" updater.log
echo "RESULT pass=$pass fail=$fail"
