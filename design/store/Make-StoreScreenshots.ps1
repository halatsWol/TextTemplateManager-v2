# Builds a release's Store screenshots for every language: for each releases/vX.Y.Z/<language>/store.md, the lines
# of its "Screenshot images" block and the window captures in design/store/captures/<language>/ go through
# Make-StoreImages.jsx in Photoshop (COM), into releases/vX.Y.Z/<language>/screenshots/<name>.png (2560x1440, one
# colour scheme). A new language folder is picked up on its own. Needs Photoshop on this PC; the captures come from
# the VM capture run.
#
# Usage: pwsh -File design/store/Make-StoreScreenshots.ps1 -Version 2.6.0 [-Scheme amber] [-Language de-DE,fr-FR]
param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('amber', 'slate')][string]$Scheme = 'amber',
    [string[]]$Language,
    [int]$ImageTimeoutSec = 120
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$release = Join-Path $repo 'releases' "v$Version"
$jsx = Join-Path $PSScriptRoot 'Make-StoreImages.jsx'
if (-not (Test-Path $release)) { throw "No release folder $release." }

function Get-ImageLines([string]$storeMd) {
    $m = [regex]::Match((Get-Content $storeMd -Raw), "(?ms)^### Screenshot images[^\n]*\n.*?^``````text\r?\n(.*?)\r?\n``````")
    if (-not $m.Success) { return @() }
    @($m.Groups[1].Value -split "\r?\n" | Where-Object { $_.Trim() })
}

$languages = Get-ChildItem $release -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'store.md') } |
    Where-Object { -not $Language -or $_.Name -in $Language }
$jobs = foreach ($lang in $languages) {
    $lines = Get-ImageLines (Join-Path $lang.FullName 'store.md')
    if (-not $lines) { throw "$($lang.Name)/store.md has no ""Screenshot images"" block." }
    $captures = Join-Path $PSScriptRoot 'captures' $lang.Name
    $missing = $lines | ForEach-Object { ($_ -split '\|')[1].Trim() } | Where-Object { -not (Test-Path (Join-Path $captures "$_.png")) }
    if ($missing) { throw "$($lang.Name): captures missing in $captures`: $($missing -join ', ')" }
    [pscustomobject]@{ Language = $lang.Name; Lines = $lines; Captures = $captures; Out = Join-Path $lang.FullName 'screenshots' }
}

# One image per call: Photoshop once stalled after a few images in one long call; a stall then costs one image.
# Only a Photoshop this script started is closed for a retry, never one that was open already.
$ownPhotoshop = -not (Get-Process Photoshop -ErrorAction SilentlyContinue)
$compose = {
    param($jsx, $linesFile, $captures, $out, $scheme)
    $ps = New-Object -ComObject Photoshop.Application
    # An argument array from PowerShell doesn't reach the script over COM; a global set before the script does.
    $quoted = @($linesFile, $captures, $out, $scheme) | ForEach-Object { "'" + ($_ -replace '\\', '/' -replace "'", "\'") + "'" }
    "$($ps.DoJavaScript("var TTM_ARGS = [$($quoted -join ', ')]; `$.evalFile(new File('$($jsx -replace '\\', '/')'));", $null, 1))"
}
function Invoke-Image($job, [string]$line) {
    $linesFile = Join-Path ([IO.Path]::GetTempPath()) "ttm-shot-$($job.Language).txt"
    [IO.File]::WriteAllText($linesFile, $line, (New-Object Text.UTF8Encoding($false)))
    try {
        $run = Start-Job -ScriptBlock $compose -ArgumentList $jsx, $linesFile, $job.Captures, $job.Out, $Scheme
        if (Wait-Job $run -Timeout $ImageTimeoutSec) { $result = Receive-Job $run; Remove-Job $run; return "$result" }
        Stop-Job $run; Remove-Job $run -Force
        return 'timeout'
    } finally { Remove-Item $linesFile -ErrorAction SilentlyContinue }
}

Add-Type -AssemblyName System.Drawing
# Photoshop places a capture by its physical size: one saved with 120 dpi (a 125 % display) came out at 60 %. The
# captures get 72 dpi, the templates' resolution; only the metadata changes.
foreach ($job in $jobs) {
    foreach ($capture in Get-ChildItem $job.Captures -Filter *.png) {
        $image = [System.Drawing.Image]::FromFile($capture.FullName); $dpi = $image.HorizontalResolution; $image.Dispose()
        if ([math]::Round($dpi) -ne 72) { & magick mogrify -units PixelsPerInch -density 72 $capture.FullName; if ($LASTEXITCODE -ne 0) { throw "magick failed on $($capture.FullName)" } }
    }
}
foreach ($job in $jobs) {
    if (Test-Path $job.Out) { Get-ChildItem $job.Out -Filter *.png | Remove-Item }
    New-Item -ItemType Directory -Force -Path $job.Out | Out-Null
    foreach ($line in $job.Lines) {
        $name = ($line -split '\|')[0].Trim()
        $result = Invoke-Image $job $line
        if ($result -eq 'timeout' -and $ownPhotoshop) {
            Write-Warning "$($job.Language) $name`: Photoshop didn't finish in $ImageTimeoutSec s; restarting it for one retry."
            Get-Process Photoshop -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 5
            $result = Invoke-Image $job $line
        }
        if ($result -ne 'ok') { throw "$($job.Language) $name`: $result" }
    }
    $images = @(Get-ChildItem $job.Out -Filter *.png | Sort-Object Name)
    $wrong = $images | Where-Object { $i = [System.Drawing.Image]::FromFile($_.FullName); $bad = $i.Width -ne 2560 -or $i.Height -ne 1440; $i.Dispose(); $bad }
    if ($images.Count -ne $job.Lines.Count -or $wrong) { throw "$($job.Language): $($images.Count) of $($job.Lines.Count) images, wrong size: $($wrong.Name -join ', ')" }
    "$($job.Language): $($images.Count) screenshots ($Scheme)"
}
