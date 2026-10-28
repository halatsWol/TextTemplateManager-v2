# Release notes from the release's English folder, releases/vX.Y.Z/en-US:
#   -GitHubOut  the GitHub release text: release.md with {{VERSION}}, {{TAG}} and {{CHANGELOG}} filled in.
#   -StoreOut   the Store's "What's new" text: the text block under "## What's new" in store.md, as it is; more than
#               the Store's 1500 characters fails.
#
# Usage: pwsh -File build/Release-Notes.ps1 -Version 2.0.1 -GitHubOut notes.md -StoreOut store-notes.txt [-Changelog '...']
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Changelog = '',
    [string]$GitHubOut,
    [string]$StoreOut
)

$ErrorActionPreference = 'Stop'
$tag = "v$Version"
$folder = Join-Path (Split-Path $PSScriptRoot -Parent) 'releases' $tag 'en-US'
$file = Join-Path $folder 'release.md'
if (-not (Test-Path $file)) { throw "No release notes for $tag ($file)." }
$notes = Get-Content $file -Raw

if ($GitHubOut) {
    if ([string]::IsNullOrWhiteSpace($Changelog)) { $Changelog = 'Initial release.' }
    $notes.Replace('{{VERSION}}', $Version).Replace('{{TAG}}', $tag).Replace('{{CHANGELOG}}', $Changelog) |
        Set-Content -Path $GitHubOut -Encoding utf8NoBOM
}

if ($StoreOut) {
    $store = Get-Content (Join-Path $folder 'store.md') -Raw
    $m = [regex]::Match($store, "(?ms)^## What's new[^\n]*\n.*?^``````text\r?\n(.*?)\r?\n``````")
    if (-not $m.Success) { throw "store.md has no text block under ""## What's new""." }
    $text = ($m.Groups[1].Value -replace "\r\n", "`n").Trim()
    if ($text.Length -gt 1500) { throw "The Store's What's new text in store.md has $($text.Length) characters; the Store takes 1500." }
    Set-Content -Path $StoreOut -Value $text -Encoding utf8NoBOM -NoNewline
}
