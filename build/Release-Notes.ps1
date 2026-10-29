# Release notes and Store listings from the release's folder, releases/vX.Y.Z (one folder per language):
#   -GitHubOut      the GitHub release text: en-US/release.md with {{VERSION}}, {{TAG}} and {{CHANGELOG}} filled in.
#   -ListingOutDir  the Store listing of every language folder with a store.md, one field per file, as Partner Center
#                   takes them (title, short-description, description, features, search-terms, whats-new, copyright,
#                   screenshot-captions) plus screenshots\ (from the release's <language>/screenshots, else from the
#                   newest earlier release that has them for the language). Partner Center's limits are checked; a
#                   new language folder is picked up on its own; en-US must be there.
#
# Usage: pwsh -File build/Release-Notes.ps1 -Version 2.0.1 -GitHubOut notes.md -ListingOutDir store-listing [-Changelog '...']
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Changelog = '',
    [string]$GitHubOut,
    [string]$ListingOutDir
)

$ErrorActionPreference = 'Stop'
$tag = "v$Version"
$releases = Join-Path (Split-Path $PSScriptRoot -Parent) 'releases'
$release = Join-Path $releases $tag
$file = Join-Path $release 'en-US' 'release.md'
if (-not (Test-Path $file)) { throw "No release notes for $tag ($file)." }

if ($GitHubOut) {
    if ([string]::IsNullOrWhiteSpace($Changelog)) { $Changelog = 'Initial release.' }
    (Get-Content $file -Raw).Replace('{{VERSION}}', $Version).Replace('{{TAG}}', $tag).Replace('{{CHANGELOG}}', $Changelog) |
        Set-Content -Path $GitHubOut -Encoding utf8NoBOM
}

# The text block under the "### <heading>" (or "## <heading>") line of a store.md.
function Get-Block([string]$text, [string[]]$headings, [string]$language) {
    foreach ($heading in $headings) {
        $m = [regex]::Match($text, "(?ms)^#{2,3} $([regex]::Escape($heading))[^\n]*\n.*?^``````text\r?\n(.*?)\r?\n``````")
        if ($m.Success) { return ($m.Groups[1].Value -replace "\r\n", "`n").Trim() }
    }
    throw "$language/store.md has no text block under ""$($headings[0])""."
}
function Lines([string]$block) { @($block -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

# The newest release up to this one with screenshots for the language.
function Get-Screenshots([string]$language) {
    $versions = Get-ChildItem $releases -Directory -Filter 'v*' |
        Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' -and [version]$_.Name.Substring(1) -le [version]$Version } |
        Sort-Object { [version]$_.Name.Substring(1) } -Descending
    foreach ($v in $versions) {
        $shots = @(Get-ChildItem (Join-Path $v.FullName $language 'screenshots') -Filter *.png -ErrorAction SilentlyContinue | Sort-Object Name)
        if ($shots) { return $shots }
    }
    @()
}

if ($ListingOutDir) {
    $languages = Get-ChildItem $release -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'store.md') }
    if ('en-US' -notin $languages.Name) { throw "No en-US/store.md for $tag." }
    foreach ($language in $languages) {
        $name = $language.Name
        $text = Get-Content (Join-Path $language.FullName 'store.md') -Raw
        $fields = [ordered]@{
            'title'               = Get-Block $text 'Product name' $name
            'short-description'   = Get-Block $text 'Short description' $name
            'description'         = Get-Block $text 'Description' $name
            'features'            = Get-Block $text 'Product features' $name
            'search-terms'        = Get-Block $text 'Search terms' $name
            'whats-new'           = Get-Block $text "What's new" $name
            'copyright'           = Get-Block $text 'Copyright and trademark info' $name
            # Partner Center's caption field: the lines without their "01  " numbers.
            'screenshot-captions' = (Lines (Get-Block $text 'Screenshot captions', 'Screenshots and logo' $name) | ForEach-Object { $_ -replace '^\d+\s+' }) -join "`n"
        }
        $problems = @()
        if ($fields['short-description'].Length -gt 1000) { $problems += 'short description over 1000 characters' }
        if ($fields['description'].Length -gt 10000) { $problems += 'description over 10000 characters' }
        if ($fields['whats-new'].Length -gt 1500) { $problems += "What's new has $($fields['whats-new'].Length) characters; the Store takes 1500" }
        $features = Lines $fields['features']
        if ($features.Count -gt 20 -or ($features | Where-Object Length -gt 200)) { $problems += 'more than 20 features or one over 200 characters' }
        $terms = Lines $fields['search-terms']
        if ($terms.Count -gt 7 -or ($terms | Where-Object Length -ge 30) -or (($terms | ForEach-Object { ($_ -split '\s+').Count } | Measure-Object -Sum).Sum -gt 21)) { $problems += 'search terms over the limits (7, under 30 characters, 21 words)' }
        $captions = Lines $fields['screenshot-captions']
        if ($captions | Where-Object Length -gt 200) { $problems += 'a screenshot caption over 200 characters' }
        if ($problems) { throw "$name/store.md: $($problems -join '; ')." }

        $dir = Join-Path $ListingOutDir $name
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        foreach ($f in $fields.GetEnumerator()) { Set-Content -Path (Join-Path $dir "$($f.Key).txt") -Value $f.Value -Encoding utf8NoBOM -NoNewline }
        $shots = Get-Screenshots $name
        if ($shots) {
            New-Item -ItemType Directory -Force -Path (Join-Path $dir 'screenshots') | Out-Null
            $shots | Copy-Item -Destination (Join-Path $dir 'screenshots')
            if ($shots.Count -ne $captions.Count) { Write-Warning "$name`: $($shots.Count) screenshots but $($captions.Count) captions." }
        }
        else { Write-Warning "$name`: no screenshots in this or an earlier release." }
        "$name`: listing, $($shots.Count) screenshots$(if ($shots) { " (from $($shots[0].Directory.Parent.Parent.Name))" })"
    }
}
