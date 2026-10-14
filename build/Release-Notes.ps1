# Release notes from release.md:
#   -GitHubOut  the GitHub release text: release.md with {{VERSION}}, {{TAG}} and {{CHANGELOG}} filled in.
#   -StoreOut   the Store's "What's new" text: the "What's new in this release" section as plain text, cut to the
#               Store's 1500 characters with a pointer to the full notes.
#
# Usage: pwsh -File build/Release-Notes.ps1 -Version 2.0.1 -GitHubOut notes.md -StoreOut store-notes.txt [-Changelog '...']
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Changelog = '',
    [string]$GitHubOut,
    [string]$StoreOut,
    [string]$ReleaseUrl = ''
)

$ErrorActionPreference = 'Stop'
$notes = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'release.md') -Raw
$tag = "v$Version"

if ($GitHubOut) {
    if ([string]::IsNullOrWhiteSpace($Changelog)) { $Changelog = 'Initial release.' }
    $notes.Replace('{{VERSION}}', $Version).Replace('{{TAG}}', $tag).Replace('{{CHANGELOG}}', $Changelog) |
        Set-Content -Path $GitHubOut -Encoding utf8NoBOM
}

if ($StoreOut) {
    $lines = @(); $inSection = $false
    foreach ($line in ($notes -split "\r?\n")) {
        $t = $line.Trim()
        if (-not $inSection) { $inSection = $t -match "^### What's new"; continue }
        if ($t -match '^(## |### )' -or $t -eq '---') { break }
        if ($t.Contains('{{')) { continue }
        $t = $t -replace '\[([^\]]+)\]\([^)]*\)', '$1' -replace '\*\*', '' -replace '`', ''
        if ($t -match '^#+\s*(.*)$') { $t = $Matches[1] }
        elseif ($t -match '^[-*]\s+(.*)$') { $t = "- $($Matches[1])" }
        $lines += $t
    }
    $text = (($lines -join "`n") -replace "\n{3,}", "`n`n").Trim()
    if (-not $text) { throw "release.md has no ""What's new in this release"" section." }

    $max = 1500
    if ($text.Length -gt $max) {
        $more = if ($ReleaseUrl) { "`n`nFull release notes: $ReleaseUrl" } else { '' }
        $cut = $text.Substring(0, $max - $more.Length - 1)
        $cut = $cut.Substring(0, [Math]::Max($cut.LastIndexOf("`n"), $cut.LastIndexOf(' ')))
        $text = $cut.TrimEnd() + [char]0x2026 + $more
    }
    Set-Content -Path $StoreOut -Value $text -Encoding utf8NoBOM -NoNewline
}
