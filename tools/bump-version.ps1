# Bumps the application version (the VERSION file), commits it and tags it. Does not push.
#   .\tools\bump-version.ps1 patch    # 1.0.0 -> 1.0.1
#   .\tools\bump-version.ps1 minor    # 1.0.1 -> 1.1.0
#   .\tools\bump-version.ps1 major    # 1.1.0 -> 2.0.0
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Part
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versionFile = Join-Path $root 'VERSION'

$current = (Get-Content $versionFile -Raw).Trim()
if ($current -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    Write-Error "VERSION must be MAJOR.MINOR.PATCH, found '$current'."
    exit 1
}
$major = [int]$Matches[1]; $minor = [int]$Matches[2]; $patch = [int]$Matches[3]

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}
$next = "$major.$minor.$patch"

Set-Content -Path $versionFile -Value $next -NoNewline:$false -Encoding ascii
git -C $root add VERSION
git -C $root commit -m "chore: release v$next"
git -C $root tag "v$next"

Write-Host "Version $current -> $next (committed and tagged v$next)."
Write-Host "Push with: git push origin HEAD v$next"
Write-Host "Then on the server: git pull && docker compose -f docker-compose.prod.yml up -d --build"
