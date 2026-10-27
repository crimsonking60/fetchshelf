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

chown -R app:app /config /data

exec runuser -u app -- dotnet Fetchshelf.Web.dll "$@"
