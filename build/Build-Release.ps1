# Builds what a release ships into -OutDir: the x64 Store upload (.msixupload, for the Store submission only),
# the user manual and the administrator guide as PDFs. Run from a short path: the XAML compiler fails silently
# near 260 characters.
#
# Usage: pwsh -File build/Build-Release.ps1 -Version 2.0.1 [-ReleaseDate 2026-10-14] [-OutDir dist]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ReleaseDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not X.Y.Z (the Store takes plain numeric versions only)." }

$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'src\TextTemplateManager'
$packages = Join-Path $app 'AppPackages'
if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = (Resolve-Path $OutDir).Path

& dotnet build (Join-Path $app 'TextTemplateManager.csproj') -c Release -p:Platform=x64 -p:StoreBuild=true `
    "-p:Version=$Version" "-p:ReleaseDate=$ReleaseDate" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Store build failed.' }
$upload = Get-ChildItem $packages -Recurse -Filter "TextTemplateManager_$Version.0_x64*.msixupload" | Select-Object -First 1
if (-not $upload) { throw "No TextTemplateManager_$Version.0_x64 .msixupload in $packages." }

# The app package (.msixupload > .msixbundle > x64 .msix) must declare every UI language (Strings\<language>), or the
# Store version would show only some of them.
Add-Type -AssemblyName System.IO.Compression
function Open-Entry($zip, $pattern) {
    $entry = $zip.Entries | Where-Object { $_.FullName -like $pattern } | Select-Object -First 1
    if (-not $entry) { throw "No $pattern in the package." }
    $buffer = New-Object System.IO.MemoryStream
    $stream = $entry.Open(); $stream.CopyTo($buffer); $stream.Close()
    $buffer.Position = 0
    $buffer
}
$uploadZip = [System.IO.Compression.ZipFile]::OpenRead($upload.FullName)
$bundleZip = New-Object System.IO.Compression.ZipArchive (Open-Entry $uploadZip '*.msixbundle')
$appZip = New-Object System.IO.Compression.ZipArchive (Open-Entry $bundleZip '*_x64.msix')
$manifest = (New-Object System.IO.StreamReader (Open-Entry $appZip 'AppxManifest.xml')).ReadToEnd()
$uploadZip.Dispose()
$declared = [regex]::Matches($manifest, '<Resource Language="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$missing = Get-ChildItem (Join-Path $app 'Strings') -Directory | Where-Object { $_.Name -notin $declared } | ForEach-Object Name
if ($missing) { throw "The package declares $($declared -join ', ') but not $($missing -join ', ')." }
"Package languages: $($declared -join ', ')"
Copy-Item $upload.FullName $out

$manualGen = Join-Path $root 'tools\ManualGen\ManualGen.csproj'
foreach ($doc in @(
        @{ Source = 'docs\user\Manual.md'; Target = 'TextTemplateManager-Manual.pdf'; Subtitle = 'User Manual' },
        @{ Source = 'docs\user\AdminManual.md'; Target = 'TextTemplateManager-AdminManual.pdf'; Subtitle = 'Administrator & Deployment Guide' })) {
    & dotnet run --project $manualGen -c Release -- (Join-Path $root $doc.Source) (Join-Path $out $doc.Target) $Version $doc.Subtitle
    if ($LASTEXITCODE -ne 0) { throw "ManualGen failed for $($doc.Source)." }
}

Get-ChildItem $out | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }
