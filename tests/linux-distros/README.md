# Linux install test on several distributions

Proves the Linux install the README describes on each distribution family, in containers, the way a
player does it: as an ordinary user, download the AppImage, check it against `SHA256SUMS.txt`, make it
runnable and run it. The published results are on the wiki page
[Tested Linux distributions](https://github.com/Eldodeldo/EldoUI-updater-app/wiki/Tested-Linux-distributions).

```
bash tests/linux-distros/run-all.sh                                   # the newest release
bash tests/linux-distros/run-all.sh publish/EldoUI-Updater-x86_64.AppImage   # a build of your own
```

It needs Docker, `Xvfb`, `xwd`, ImageMagick's `convert` and `/dev/fuse` on the host. `REGISTRY=<mirror>`
pulls the images from a Docker Hub mirror; `HTTPS_PROXY` and `SSL_CERT_FILE` are passed into the containers
when the host reaches the internet through a proxy. Results land in `tests/linux-distros/results/<name>/`
(the script's output, the updater's log, its app menu and login entries, and a picture of its window).

## What each run checks (`player-test.sh`)

1. The download matches `SHA256SUMS.txt`, and the AppImage runtime starts.
2. `--check-system` reports that everything is there (or, on a system without a desktop, what is missing),
   and `--selftest` passes.
3. A WoW: Forever folder in a Wine prefix, laid out as Steam (Proton), Steam from Flatpak, Lutris or a
   Steam library on an SD card leaves it.
4. **First start:** the updater installs itself (mode 700), writes its app menu and login entries and icon;
   without FUSE the entries start it unpacked.
5. The installed copy finds the game and installs the newest EldoUI from its GitHub release by itself.
6. **Next login:** the login entry's command, run with a desktop session's bare environment, starts it again.
7. **Second start:** hands over to the running copy instead of starting another.
8. **`--uninstall`:** removes the program, entries, settings and log; EldoUI stays in the game.

On a system without a desktop (`EXPECT=blocked`), the first start must stop with a message that names the
missing packages with the install command for that distribution, and install nothing.

## What a container cannot show

The tray icon (it needs a desktop panel), desktop notices, the file manager's "Allow executing" switch, a
real log-in by the desktop session, Wayland sessions, and Battle.net or the game itself. These are checked
by hand on a real desktop.
