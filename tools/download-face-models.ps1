# Downloads InsightFace buffalo_l (SCRFD-10G detector + ArcFace R50 recognizer) for local development.
# The pretrained weights are for non-commercial use only (see https://github.com/deepinsight/insightface).
param([string]$Destination = (Join-Path $PSScriptRoot '..\models\buffalo_l'))

$ErrorActionPreference = 'Stop'
$url = 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip'
$zip = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l.zip'
$extract = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l'

Invoke-WebRequest -Uri $url -OutFile $zip
Write-Host "buffalo_l.zip SHA-256: $((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Expand-Archive -Path $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($name in 'det_10g.onnx', 'w600k_r50.onnx') {
    $file = Get-ChildItem -Path $extract -Recurse -Filter $name | Select-Object -First 1
    if (-not $file) { throw "$name not found in buffalo_l.zip" }
    Copy-Item $file.FullName (Join-Path $Destination $name) -Force
}
Remove-Item $zip, $extract -Recurse -Force
Write-Host "Models copied to $((Resolve-Path $Destination).Path)"
