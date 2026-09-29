#!/bin/sh
# Applies pending EF Core migrations, then hands off to supervisord, which
# runs nginx (public entry point) and the API (loopback-only) as siblings.
set -e

if [ -z "$ConnectionStrings__PictureManagerDb" ]; then
    echo "ConnectionStrings__PictureManagerDb is not set" >&2
    exit 1
fi

echo "Applying database migrations..."
/app/efbundle --connection "$ConnectionStrings__PictureManagerDb"

echo "Starting nginx + PictureManager.Api..."
exec supervisord -c /etc/supervisor/supervisord.conf
