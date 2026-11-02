#!/bin/sh
set -e

if [ "$(id -u)" != "0" ]; then
    # Not running as root (e.g. the container was started with a fixed `user:`,
    # often alongside cap_drop/no-new-privileges) - there's no privilege to remap
    # the app user or chown the volumes, so just run as whatever user Docker set.
    exec dotnet Fetchshelf.Web.dll "$@"
fi

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"

if [ "$(id -g app)" != "$PGID" ]; then
    groupmod -o -g "$PGID" app
fi
if [ "$(id -u app)" != "$PUID" ]; then
    usermod -o -u "$PUID" app
fi

chown -R app:app /config /database

# setpriv replaces this shell with the app, so `docker stop` signals the app directly and it gets the whole
# stop timeout to shut down. runuser would stay in between and kill the app 2 seconds after SIGTERM.
# setpriv keeps the environment as it is, so set what runuser used to set.
export HOME=/home/app USER=app LOGNAME=app
exec setpriv --reuid app --regid app --init-groups dotnet Fetchshelf.Web.dll "$@"
