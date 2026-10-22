# Builds the documentation site (GitHub Pages, https://software.kmarflow.com/TextTemplateManager-v2/) with MkDocs.
# The pages in website/pages are the site's own; the manuals and the privacy policy are taken from where they live
# (docs/user, PRIVACY.md), so the site, the PDFs and the in-app manual come from one text. Image links that point
# into the app's Assets/ManualImages are rewritten to the site's images/ folder.
#
# Usage: pwsh -File build/Build-Website.ps1 -OutDir out/website     (needs mkdocs-material: pip install mkdocs-material)
param(
    [string]$OutDir = 'out/website',
    [string]$MkDocs = 'mkdocs'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root $OutDir
$docs = Join-Path $stage 'docs'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $docs 'images') | Out-Null

Copy-Item (Join-Path $root 'website/mkdocs.yml') $stage
Copy-Item (Join-Path $root 'website/overrides') $stage -Recurse
Copy-Item (Join-Path $root 'website/pages/*') $docs -Recurse
Copy-Item (Join-Path $root 'src/TextTemplateManager/Assets/ManualImages/*') (Join-Path $docs 'images')
Copy-Item (Join-Path $root 'design/logo/logo.svg') (Join-Path $docs 'images/logo.svg')
# The Store's slate art: the home page's hero and gallery.
New-Item -ItemType Directory -Force -Path (Join-Path $docs 'images/store') | Out-Null
foreach ($art in '01-main', '02-quick-paste', '04-paste-modes', '06-sync', 'super-hero-art') {
    Copy-Item (Join-Path $root "design/store/slate/$art-slate.png") (Join-Path $docs 'images/store')
}

# Each source gets the page title the manuals leave to their PDF cover.
$sources = @(
    @{ From = 'docs/user/Manual.md'; To = 'manual.md'; Title = 'User manual' },
    @{ From = 'docs/user/AdminManual.md'; To = 'admin.md'; Title = 'Administrator & Deployment Guide' },
    @{ From = 'PRIVACY.md'; To = 'privacy.md'; Title = $null }
)
foreach ($s in $sources) {
    $text = Get-Content (Join-Path $root $s.From) -Raw -Encoding utf8
    # Both link forms: ](<path with (parentheses)>) and ](path).
    $text = $text -replace '\]\(<(?:\.\./)+src/TextTemplateManager/Assets/ManualImages/([^>]+)>\)', '](<images/$1>)'
    $text = $text -replace '\]\((?:\.\./)+src/TextTemplateManager/Assets/ManualImages/([^)\s]+)\)', '](images/$1)'
    $outside = [regex]::Match($text, '\]\([^)]*\.\./[^)]*\)')
    if ($outside.Success) { Write-Warning "$($s.From) still links outside the site: $($outside.Value)" }
    if ($s.Title) { $text = "# $($s.Title)`n`n$text" }
    Set-Content (Join-Path $docs $s.To) $text -Encoding utf8 -NoNewline
}

Push-Location $stage
try {
    & $MkDocs build --strict
    if ($LASTEXITCODE -ne 0) { throw 'mkdocs build failed' }
}
finally { Pop-Location }
Write-Host "Site built: $(Join-Path $stage 'site')"
