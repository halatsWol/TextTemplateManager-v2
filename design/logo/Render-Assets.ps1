# Renders every logo asset in src/TextTemplateManager/Assets (the PNGs the manifest references, their scale and
# targetsize variants, and AppIcon.ico) from logo.svg. Each PNG keeps its size; the logo is drawn centred at a
# fraction of the shorter side. Needs ImageMagick 7 (magick) with its librsvg delegate.
#
# Usage: pwsh -File design/logo/Render-Assets.ps1
param(
    [string]$Svg = (Join-Path $PSScriptRoot 'logo.svg'),
    [string]$Assets = (Join-Path $PSScriptRoot '..\..\src\TextTemplateManager\Assets')
)

$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "ttm-logo-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Logo size relative to the shorter side of the canvas, as the Windows asset templates lay them out.
function Get-LogoFraction([string]$name) {
    switch -Regex ($name) {
        '^(AppIcon|StoreLogo)' { return 1.0 }
        'unplated' { return 1.0 }
        '^Square44x44Logo' { return 0.75 }
        '^SmallTile' { return 0.5 }
        default { return 1 / 3 }   # Square150x150Logo, LargeTile, Wide310x150Logo, SplashScreen
    }
}

# Draws the SVG straight at the target size (sharper than scaling a large render down).
function Export-Logo([int]$size, [string]$path) {
    $density = 96 * $size / 1024
    & magick -background none -density $density $Svg -resize "${size}x${size}!" $path
    if ($LASTEXITCODE) { throw "rendering $size px failed" }
}

try {
    foreach ($file in Get-ChildItem $Assets -Filter '*.png') {
        $w, $h = (& magick identify -format '%w %h' $file.FullName) -split ' ' | ForEach-Object { [int]$_ }
        $size = [int][math]::Round([math]::Min($w, $h) * (Get-LogoFraction $file.Name))
        $logo = Join-Path $work 'logo.png'
        Export-Logo $size $logo
        # The smallest of two PNG filter settings; large gradients compress better unfiltered.
        $candidates = foreach ($filter in 0, 5) {
            $out = Join-Path $work "f$filter.png"
            & magick -size "${w}x${h}" xc:none $logo -gravity center -composite -strip `
                -define png:compression-level=9 -define png:compression-filter=$filter "PNG32:$out"
            Get-Item $out
        }
        Copy-Item ($candidates | Sort-Object Length | Select-Object -First 1).FullName $file.FullName -Force
        '{0,-58} {1,5}x{2,-5} logo {3,4} px  {4,7} bytes' -f $file.Name, $w, $h, $size, (Get-Item $file.FullName).Length
    }

    $icons = foreach ($size in 256, 128, 64, 48, 32, 16) {
        $png = Join-Path $work "ico-$size.png"
        Export-Logo $size $png
        $png
    }
    & magick @icons (Join-Path $Assets 'AppIcon.ico')
    'AppIcon.ico ({0} bytes): {1}' -f (Get-Item (Join-Path $Assets 'AppIcon.ico')).Length,
        ((& magick identify -format '%w ' (Join-Path $Assets 'AppIcon.ico')).Trim())
}
finally { Remove-Item $work -Recurse -Force }
