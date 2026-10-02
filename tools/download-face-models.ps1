# Downloads InsightFace buffalo_l (SCRFD-10G detector + ArcFace R50 recognizer) for local development.
# The pretrained weights are for non-commercial use only (see https://github.com/deepinsight/insightface).
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\models\buffalo_l'),
    # Skips the SHA-256 check, for when the release asset was legitimately replaced (verify the new file yourself).
    [switch]$SkipChecksum
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar slows Invoke-WebRequest down by orders of magnitude
$url = 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip'
$expectedSha256 = '80ffe37d8a5940d59a7384c201a2a38d4741f2f3c51eef46ebb28218a7b0ca2f'   # same as BUFFALO_L_SHA256 in the Dockerfile
$zip = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l.zip'
$extract = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l'

Invoke-WebRequest -Uri $url -OutFile $zip
$actualSha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($SkipChecksum) {
    Write-Warning "SHA-256 check skipped. buffalo_l.zip SHA-256: $actualSha256"
}
elseif ($actualSha256 -ne $expectedSha256) {
    Remove-Item $zip -Force
    throw "buffalo_l.zip SHA-256 mismatch: expected $expectedSha256, got $actualSha256. The download is corrupt or the release asset changed. Re-run, or pass -SkipChecksum once you have verified the file."
}
Expand-Archive -Path $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($name in 'det_10g.onnx', 'w600k_r50.onnx') {
    $file = Get-ChildItem -Path $extract -Recurse -Filter $name | Select-Object -First 1
    if (-not $file) { throw "$name not found in buffalo_l.zip" }
    Copy-Item $file.FullName (Join-Path $Destination $name) -Force
}
Remove-Item $zip, $extract -Recurse -Force
Write-Host "Models copied to $((Resolve-Path $Destination).Path)"
