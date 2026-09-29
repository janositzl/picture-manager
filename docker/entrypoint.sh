#!/bin/sh
# Applies pending EF Core migrations, then starts the API.
# Fails fast (via `set -e`) if the migration bundle errors, so a broken
# schema never reaches a running API instance.
set -e

if [ -z "$ConnectionStrings__PictureManagerDb" ]; then
    echo "ConnectionStrings__PictureManagerDb is not set" >&2
    exit 1
fi

echo "Applying database migrations..."
/app/efbundle --connection "$ConnectionStrings__PictureManagerDb"

echo "Starting PictureManager.Api..."
exec dotnet /app/PictureManager.Api.dll
