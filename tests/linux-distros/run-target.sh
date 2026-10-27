#!/bin/bash
# One distribution: starts its container with an X display from the host, gives it an ordinary user
# (uid 1000) as a desktop has, runs player-test.sh as that user and saves the output and a picture of the
# updater's window in results/<name>/.
# Usage: run-target.sh <name> <image> <display number> <fuse|extract> <prefix kind> [root setup command] [docker options]
# Environment: TEST_SCRIPT (default player-test.sh; clean-test.sh for a clean home), EXPECT=ok|blocked, USER_HOME (default /home/player), ELDOUI_VERSION (the newest EldoUI release),
# and HTTPS_PROXY / SSL_CERT_FILE when the machine reaches the internet through a proxy.
set -u
NAME="$1"; IMAGE="$2"; DISP="$3"; MODE="$4"; KIND="$5"; SETUP="${6:-true}"; EXTRA="${7:-}"
KIT="$(cd "$(dirname "$0")" && pwd)"
OUT="$KIT/results/$NAME"; mkdir -p "$OUT"
WANT="${ELDOUI_VERSION:?set ELDOUI_VERSION to the newest EldoUI release}"
UHOME="${USER_HOME:-/home/player}"
C="eldoui-updater-test-$NAME"
docker rm -f "$C" >/dev/null 2>&1

Xvfb ":$DISP" -screen 0 1280x800x24 -ac -nolisten tcp >/dev/null 2>&1 &
XPID=$!
sleep 1

# A desktop always has an account for the person logged in (fusermount3 needs it): add uid 1000 when the image has none.
ACC="$OUT/account"; mkdir -p "$ACC"
docker run --rm --entrypoint cat "$IMAGE" /etc/passwd > "$ACC/passwd"
docker run --rm --entrypoint cat "$IMAGE" /etc/group > "$ACC/group"
grep -q '^[^:]*:[^:]*:1000:' "$ACC/passwd" || echo "$(basename "$UHOME"):x:1000:1000:Player:$UHOME:/bin/bash" >> "$ACC/passwd"
grep -q '^[^:]*:[^:]*:1000:' "$ACC/group" || echo "$(basename "$UHOME"):x:1000:" >> "$ACC/group"

PROXY=(); PROXY_ENV=()
[ -n "${HTTPS_PROXY:-}" ] && PROXY_ENV+=(-e "HTTPS_PROXY=$HTTPS_PROXY")
[ -n "${SSL_CERT_FILE:-}" ] && { PROXY+=(-v "$SSL_CERT_FILE:/etc/eldoui-test-ca.crt:ro"); PROXY_ENV+=(-e SSL_CERT_FILE=/etc/eldoui-test-ca.crt); }

# shellcheck disable=SC2086
docker run -d --name "$C" --network host --device /dev/fuse --cap-add SYS_ADMIN --security-opt apparmor=unconfined \
	-v "$KIT:/kit:ro" -v /tmp/.X11-unix:/tmp/.X11-unix -v "$ACC/passwd:/etc/passwd:ro" -v "$ACC/group:/etc/group:ro" \
	"${PROXY[@]}" "${PROXY_ENV[@]}" $EXTRA --entrypoint sleep "$IMAGE" infinity >/dev/null || { echo "container did not start" > "$OUT/result.txt"; kill $XPID; exit 1; }
# /dev/fuse is open to every user on a desktop; a container passes it in as root's only.
docker exec -u 0 "$C" sh -c "mkdir -p $UHOME /tmp/xdg-1000 && chown 1000:1000 $UHOME /tmp/xdg-1000 && chmod 700 /tmp/xdg-1000; chmod 666 /dev/fuse 2>/dev/null; $SETUP" > "$OUT/setup.txt" 2>&1
echo "setup exit $?" >> "$OUT/setup.txt"

( docker exec -u 1000:1000 -e HOME="$UHOME" -e USER="$(basename "$UHOME")" -e DISPLAY=":$DISP" -e XDG_RUNTIME_DIR=/tmp/xdg-1000 \
	"${PROXY_ENV[@]}" -w "$UHOME" "$C" \
	bash "/kit/${TEST_SCRIPT:-player-test.sh}" "$MODE" "$WANT" "$KIND" "${EXPECT:-ok}" release > "$OUT/result.txt" 2>&1 ) &
TPID=$!
# A picture of the updater's window once EldoUI is installed.
for i in $(seq 1 400); do
	if docker exec "$C" test -f "$UHOME/.shot-ready" 2>/dev/null; then
		sleep 4
		xwd -root -display ":$DISP" -silent | convert xwd:- "$OUT/window.png" 2>/dev/null
		break
	fi
	kill -0 $TPID 2>/dev/null || break
	sleep 1
done
wait $TPID
for f in check-system.txt login.txt selftest.txt first-start.txt updater.log menu.desktop autostart.desktop uninstall.txt; do
	docker cp "$C:$UHOME/$f" "$OUT/$f" >/dev/null 2>&1
done
docker rm -f "$C" >/dev/null 2>&1
kill $XPID 2>/dev/null
echo "$NAME: $(tail -1 "$OUT/result.txt")"
