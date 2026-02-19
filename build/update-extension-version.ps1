param(
    [Parameter(Mandatory=$true)]
    [string]$ExtensionDir
)

$manifestPath = Join-Path $ExtensionDir "manifest.json"
$hashFilePath = Join-Path $ExtensionDir ".content-hash"

if (-not (Test-Path $manifestPath)) {
    Write-Host "No manifest.json found in $ExtensionDir, skipping."
    exit 0
}

# Get all files recursively, excluding manifest.json and .content-hash
$files = Get-ChildItem -Path $ExtensionDir -Recurse -File |
    Where-Object { $_.Name -ne "manifest.json" -and $_.Name -ne ".content-hash" } |
    Sort-Object { $_.FullName }

# Compute combined MD5 hash of all file contents
$md5 = [System.Security.Cryptography.MD5]::Create()
$combined = New-Object System.IO.MemoryStream

foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $combined.Write($bytes, 0, $bytes.Length)
}

$combined.Position = 0
$hashBytes = $md5.ComputeHash($combined)
$combined.Dispose()
$md5.Dispose()

$currentHash = [BitConverter]::ToString($hashBytes) -replace '-', ''

# Read stored hash
$storedHash = ""
if (Test-Path $hashFilePath) {
    $storedHash = (Get-Content $hashFilePath -Raw).Trim()
}

if ($currentHash -eq $storedHash) {
    Write-Host "$ExtensionDir : unchanged (hash $currentHash)"
    exit 0
}

# Hash differs — increment patch version in manifest.json
$manifest = Get-Content $manifestPath -Raw
$versionMatch = [regex]::Match($manifest, '"version"\s*:\s*"(\d+)\.(\d+)\.(\d+)"')

if (-not $versionMatch.Success) {
    Write-Host "$ExtensionDir : WARNING - could not parse version in manifest.json"
    exit 1
}

$major = $versionMatch.Groups[1].Value
$minor = $versionMatch.Groups[2].Value
$patch = [int]$versionMatch.Groups[3].Value + 1

$oldVersion = "$major.$minor.$($versionMatch.Groups[3].Value)"
$newVersion = "$major.$minor.$patch"

$manifest = $manifest -replace ('"version"\s*:\s*"' + [regex]::Escape($oldVersion) + '"'), "`"version`": `"$newVersion`""

Set-Content -Path $manifestPath -Value $manifest -NoNewline
Set-Content -Path $hashFilePath -Value $currentHash -NoNewline

Write-Host "$ExtensionDir : bumped $oldVersion -> $newVersion (hash $currentHash)"
