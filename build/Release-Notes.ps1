# Release notes from the release's folder, releases/vX.Y.Z (one folder per language):
#   -GitHubOut    the GitHub release text: en-US/release.md with {{VERSION}}, {{TAG}} and {{CHANGELOG}} filled in.
#   -StoreOutDir  the Store's "What's new" per language: <language>.txt for every folder with a store.md, from the
#                 text block under "## What's new" there, as it is; more than the Store's 1500 characters fails.
#                 A new language folder is picked up on its own; en-US must be there.
#
# Usage: pwsh -File build/Release-Notes.ps1 -Version 2.0.1 -GitHubOut notes.md -StoreOutDir store-notes [-Changelog '...']
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Changelog = '',
    [string]$GitHubOut,
    [string]$StoreOutDir
)

$ErrorActionPreference = 'Stop'
$tag = "v$Version"
$release = Join-Path (Split-Path $PSScriptRoot -Parent) 'releases' $tag
$file = Join-Path $release 'en-US' 'release.md'
if (-not (Test-Path $file)) { throw "No release notes for $tag ($file)." }

if ($GitHubOut) {
    if ([string]::IsNullOrWhiteSpace($Changelog)) { $Changelog = 'Initial release.' }
    (Get-Content $file -Raw).Replace('{{VERSION}}', $Version).Replace('{{TAG}}', $tag).Replace('{{CHANGELOG}}', $Changelog) |
        Set-Content -Path $GitHubOut -Encoding utf8NoBOM
}

if ($StoreOutDir) {
    New-Item -ItemType Directory -Force -Path $StoreOutDir | Out-Null
    $languages = Get-ChildItem $release -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'store.md') }
    if ('en-US' -notin $languages.Name) { throw "No en-US/store.md for $tag." }
    foreach ($language in $languages) {
        $store = Get-Content (Join-Path $language.FullName 'store.md') -Raw
        $m = [regex]::Match($store, "(?ms)^## What's new[^\n]*\n.*?^``````text\r?\n(.*?)\r?\n``````")
        if (-not $m.Success) { throw "$($language.Name)/store.md has no text block under ""## What's new""." }
        $text = ($m.Groups[1].Value -replace "\r\n", "`n").Trim()
        if ($text.Length -gt 1500) { throw "The Store's What's new text in $($language.Name)/store.md has $($text.Length) characters; the Store takes 1500." }
        Set-Content -Path (Join-Path $StoreOutDir "$($language.Name).txt") -Value $text -Encoding utf8NoBOM -NoNewline
    }
    "Store What's new: $($languages.Name -join ', ')"
}
