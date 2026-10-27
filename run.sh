#!/bin/sh
# Runs Fetchshelf.Web the same way Rider's selected run configuration does: `dotnet run` against
# the Web project using the "http" launch profile from launchSettings.json (env vars, dev config
# path, and port come from there, not from this script).
set -e
cd "$(dirname "$0")"
exec dotnet run --project src/Fetchshelf.Web --launch-profile http "$@"
