# Builds a release, computes the changed-file patch, publishes it and announces it.
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\publish-update.ps1 -Version 1.0.1 `
#       -Notes "Updated to the new Quorum API","Fixed clients not closing" `
#       -Repo owner/legend -Webhook https://discord.com/api/webhooks/...

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Notes = @(),
    [string]$NoteFile = '',
    [string]$Repo = '',
    [string]$Webhook = '',
    [string]$Ping = '@everyone',
    [switch]$NoRelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$appOut = Join-Path $dist 'app'
$release = Join-Path $root 'release'
$stateFile = Join-Path $release 'state.json'

if ($NoteFile) {
    $Notes = Get-Content $NoteFile | Where-Object { $_.Trim() -ne '' } | ForEach-Object { $_.TrimStart('-', '*', '+', ' ') }
}
if (-not $Notes -or $Notes.Count -eq 0) { throw 'Pass -Notes or -NoteFile with at least one change.' }

# ── Resolve the repo (for the asset URLs) ────────────────────────────────────
if (-not $Repo) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $Repo = (& gh repo view --json nameWithOwner --jq .nameWithOwner 2>$null | Out-String).Trim() } catch { $Repo = '' }
    $ErrorActionPreference = $old
    if ($LASTEXITCODE -ne 0) { $Repo = '' }
}
$assetBase = if ($Repo) { "https://github.com/$Repo/releases/latest/download" } else { '' }

# Write the manifest location into update-config.json so fresh installs know where to look
$manifestUrl = if ($assetBase) { "$assetBase/manifest.json" } else { '' }
$updateConfig = Join-Path $root 'update-config.json'
if ($manifestUrl) {
    $json = @{ manifest = $manifestUrl } | ConvertTo-Json
    [System.IO.File]::WriteAllText($updateConfig, $json, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host "[*] Building Legend $Version..."
& (Join-Path $root 'build-installer.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# ── Hash every file, diff against the previous release ───────────────────────
Write-Host '[*] Hashing files...'
$current = [ordered]@{}
Get-ChildItem $appOut -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($appOut.Length + 1)
    $current[$rel] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
}

$previous = @{}
if (Test-Path $stateFile) {
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
    foreach ($p in $state.files.PSObject.Properties) { $previous[$p.Name] = $p.Value }
}

$changed = @($current.Keys | Where-Object { -not $previous.ContainsKey($_) -or $previous[$_] -ne $current[$_] })
Write-Host ("[*] {0} of {1} files changed." -f $changed.Count, $current.Count)

# ── Pack the patch ───────────────────────────────────────────────────────────
if (-not (Test-Path $release)) { New-Item -ItemType Directory $release | Out-Null }
$zipPath = Join-Path $release 'update.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($rel in $changed) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $appOut $rel), $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }

$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
$zipSize = (Get-Item $zipPath).Length

$manifest = [ordered]@{
    version = $Version
    zip     = if ($assetBase) { "$assetBase/update.zip" } else { 'update.zip' }
    sha256  = $zipHash
    size    = $zipSize
    page    = if ($Repo) { "https://github.com/$Repo/releases/tag/v$Version" } else { '' }
    notes   = @($Notes)
    files   = $current
}
$manifestPath = Join-Path $release 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

[System.IO.File]::WriteAllText($stateFile, (@{ version = $Version; files = $current } | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("[+] Patch: {0:N1} MB, sha256 {1}" -f ($zipSize / 1MB), $zipHash.Substring(0, 12))
Write-Host ('    ' + $zipPath)
Write-Host ('    ' + $manifestPath)

# ── Publish the GitHub release ───────────────────────────────────────────────
$published = $false
if (-not $NoRelease -and $Repo) {
    Write-Host '[*] Publishing release...'
    $body = "## Legend $Version`n`n" + (($Notes | ForEach-Object { "- $_" }) -join "`n")
    & gh release create "v$Version" $zipPath $manifestPath --title "Legend $Version" --notes $body
    $published = ($LASTEXITCODE -eq 0)
    if (-not $published) { Write-Warning 'gh release failed (not signed in?). Upload update.zip and manifest.json by hand.' }
}

# ── Announce in Discord ──────────────────────────────────────────────────────
if ($Webhook) {
    $diff = '```diff' + "`n" + (($Notes | ForEach-Object { '+ ' + $_ }) -join "`n") + "`n" + '```'
    $payload = @{
        content    = $Ping
        embeds     = @(@{
            title       = 'Legend has been updated!'
            color       = 15790320
            description = "**Legend v$Version** is live.`nIf you have the Legend bootstrapper installed you can just go ahead and run it."
            fields      = @(@{ name = "What's new?"; value = $diff; inline = $false })
            footer      = @{ text = "v$Version • $(Get-Date -Format 'dd/MM/yyyy HH:mm')" }
        })
    } | ConvertTo-Json -Depth 8
    try {
        Invoke-RestMethod -Uri $Webhook -Method Post -ContentType 'application/json' -Body $payload | Out-Null
        Write-Host '[+] Posted to Discord.'
    } catch { Write-Warning ('Discord webhook failed: ' + $_.Exception.Message) }
}

Write-Host ''
if ($published) { Write-Host "[+] Released v$Version. Installed clients pick it up on next launch." }
else { Write-Host "[!] Not published. Upload $zipPath and $manifestPath, then installed clients pick it up on next launch." }
