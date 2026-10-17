# Renders the Store's 2:3 poster art (1440x2160) and 1:1 box art (2160x2160), logo and name in the top two thirds,
# and the 16:9 super hero art
# (3840x2160, no text and no app UI: the logo among keycaps and template cards) in both colour schemes into
# amber\ and slate\. Needs ImageMagick 7 with its librsvg delegate.
#
# Usage: pwsh -File design/store/Make-StoreArt.ps1
param([string]$Out = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$svg = Join-Path $PSScriptRoot '..\logo\logo.svg'
$work = Join-Path ([IO.Path]::GetTempPath()) "ttm-art-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null

$schemes = [ordered]@{
    amber = @{ From = '#fbe7b8'; To = '#f0a93e'; Text = '#1c2140'; Accent = '#3a4470'; Key = '#fff6e0'; KeyEdge = '#e0a24a'; Card = '#fffaf0'; Line = '#e9c88a'; Shadow = '#7a4a10' }
    slate = @{ From = '#232848'; To = '#545d7e'; Text = '#ffffff'; Accent = '#ff8c42'; Key = '#3b4366'; KeyEdge = '#6b74a0'; Card = '#46507a'; Line = '#7a84b0'; Shadow = '#0b0d1a' }
}

function Logo([int]$size) {
    $p = Join-Path $work "logo-$size.png"
    if (-not (Test-Path $p)) { & magick -background none -density 1200 $svg -resize "${size}x${size}" -depth 8 $p }
    $p
}

# A layer with a soft drop shadow under whatever is drawn on it.
function WithShadow([string]$layer, [string]$color, [int]$blur, [int]$dy) {
    $p = "$layer.shadow.png"
    & magick $layer `( +clone -background $color -shadow "45x$blur+0+$dy" `) +swap -background none -layers merge +repage $p
    $p
}

# Keycap: rounded square with a lighter top face; template card: rounded rectangle with text-like bars.
function Keycap([string]$p, [int]$s, $c) {
    $r = [int]($s * 0.18); $i = [int]($s * 0.09)
    & magick -size "${s}x${s}" xc:none -fill $c.KeyEdge -draw "roundrectangle 0,0 $($s-1),$($s-1) $r,$r" `
        -fill $c.Key -draw "roundrectangle $i,$i $($s-1-$i),$($s-1-[int]($i*1.6)) $([int]($r*0.8)),$([int]($r*0.8))" $p
}
function Card([string]$p, [int]$w, [int]$h, $c, [string]$accent) {
    $r = [int]($w * 0.06); $pad = [int]($w * 0.1); $bh = [int]($h * 0.07)
    $draw = @('-fill', $c.Card, '-draw', "roundrectangle 0,0 $($w-1),$($h-1) $r,$r", '-fill', $accent, '-draw', "roundrectangle $pad,$pad $([int]($w*0.55)),$($pad+$bh) $([int]($bh/2)),$([int]($bh/2))")
    $y = $pad + [int]($bh * 2.6)
    foreach ($f in 0.8, 0.72, 0.8, 0.5) { $draw += @('-fill', $c.Line, '-draw', "roundrectangle $pad,$y $([int]($pad + ($w - 2*$pad) * $f)),$($y+[int]($bh*0.8)) $([int]($bh*0.4)),$([int]($bh*0.4))"); $y += [int]($bh * 1.9) }
    & magick -size "${w}x${h}" xc:none @draw $p
}

foreach ($name in $schemes.Keys) {
    $c = $schemes[$name]
    $dir = Join-Path $Out $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    # Poster: logo and name in the top two thirds, the bottom third left to the Store's overlays.
    $logo = WithShadow (Logo 820) $c.Shadow 40 24
    & magick -size 1440x2160 -define gradient:angle=135 "gradient:$($c.From)-$($c.To)" `
        $logo -gravity north -geometry +0+250 -composite `
        -font 'C:/Windows/Fonts/seguisb.ttf' -pointsize 112 -fill $c.Text -gravity north -annotate +0+1150 'TextTemplateManager' `
        -fill $c.Accent -draw 'roundrectangle 650,1320 790,1334 7,7' `
        -alpha off -depth 8 -strip (Join-Path $dir "poster-art-$name.png")

    # Box art (1:1): like the poster, logo and name above the bottom third (which starts at 1440).
    $logo = WithShadow (Logo 900) $c.Shadow 44 26
    & magick -size 2160x2160 -define gradient:angle=135 "gradient:$($c.From)-$($c.To)" `
        $logo -gravity north -geometry +0+190 -composite `
        -font 'C:/Windows/Fonts/seguisb.ttf' -pointsize 140 -fill $c.Text -gravity north -annotate +0+1150 'TextTemplateManager' `
        -fill $c.Accent -draw 'roundrectangle 990,1375 1170,1391 8,8' `
        -alpha off -depth 8 -strip (Join-Path $dir "box-art-$name.png")

    # Super hero art: no text, no UI; the logo in the centre of the top two thirds among keycaps and template cards.
    $cmd = @('-size', '3840x2160', '-define', 'gradient:angle=135', "gradient:$($c.From)-$($c.To)")
    $items = @(
        @{ Kind = 'card'; W = 560; H = 400; X = 520; Y = 260; Rot = -8 },
        @{ Kind = 'card'; W = 520; H = 370; X = 2790; Y = 330; Rot = 7 },
        @{ Kind = 'card'; W = 460; H = 330; X = 880; Y = 980; Rot = 5 },
        @{ Kind = 'card'; W = 480; H = 340; X = 2560; Y = 1020; Rot = -6 },
        @{ Kind = 'key'; S = 230; X = 1080; Y = 640; Rot = -10 },
        @{ Kind = 'key'; S = 200; X = 2620; Y = 700; Rot = 9 },
        @{ Kind = 'key'; S = 170; X = 300; Y = 820; Rot = 12 },
        @{ Kind = 'key'; S = 190; X = 3340; Y = 880; Rot = -12 },
        @{ Kind = 'key'; S = 150; X = 1500; Y = 180; Rot = 6 },
        @{ Kind = 'key'; S = 160; X = 2250; Y = 150; Rot = -7 }
    )
    $n = 0
    foreach ($it in $items) {
        $n++; $p = Join-Path $work "$name-item$n.png"
        if ($it.Kind -eq 'card') { Card $p $it.W $it.H $c $c.Accent } else { Keycap $p $it.S $c }
        & magick $p -background none -rotate $it.Rot $p
        $p = WithShadow $p $c.Shadow 24 18
        $cmd += @($p, '-gravity', 'northwest', '-geometry', "+$($it.X)+$($it.Y)", '-composite')
    }
    $hero = WithShadow (Logo 900) $c.Shadow 50 30
    $cmd += @($hero, '-gravity', 'northwest', '-geometry', '+1450+330', '-composite', '-alpha', 'off', '-depth', '8', '-strip', (Join-Path $dir "super-hero-art-$name.png"))
    & magick @cmd
}
Remove-Item $work -Recurse -Force
Get-ChildItem $Out -Recurse -Filter '*-art-*.png' | ForEach-Object { "$($_.Name) $(& magick identify -format '%wx%h' $_.FullName) $([math]::Round($_.Length / 1KB)) KB" }
