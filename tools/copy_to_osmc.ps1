param(
    [Parameter(Mandatory=$true)]
    [string]$File,

    [Parameter(Mandatory=$true)]
    [string]$RemoteFolder,
	
    [string]$Server = "osmc",
    [string]$User = "osmc"
	
)

if (-not (Test-Path $File -PathType Leaf)) {
    Write-Host "File not found: $File" -ForegroundColor Red
    exit 1
}

$fileName = Split-Path $File -Leaf
$remotePath = "$User@$Server`:$RemoteFolder/$fileName"

Write-Host "Copying '$File' to '$remotePath'..."

scp "$File" "$remotePath"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Copy completed successfully."
} else {
    Write-Error "Copy failed. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}