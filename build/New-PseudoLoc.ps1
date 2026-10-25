# Writes the pseudo-language qps-ploc from the en-US UI text, for Debug builds only (the csproj calls it; Release and
# Store builds never do, so the package never lists it). Every letter gets an accent, every text is wrapped in [ ] and
# padded by about 40%: text that is still plain English was missed, text that loses its ] at the end gets cut off.
# {0} placeholders stay as they are; access keys stay single letters.
# Run the app in it with TTM_UI_LANGUAGE=qps-ploc.
# Writes <name>.lang-qps-ploc.resw: the language is in the file name, since the output folder is under obj\.
# Usage: powershell -File build/New-PseudoLoc.ps1 -Source <Strings\en-US> -Destination <folder>
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Destination
)

$ErrorActionPreference = 'Stop'

# Code points instead of literal characters: Windows PowerShell reads a script without BOM as ANSI.
$lower = 0xE4, 0x180, 0xE7, 0xF0, 0xE9, 0x192, 0x11D, 0x125, 0xEE, 0x135, 0x137, 0x13C, 0x271,
         0xF1, 0xF6, 0xFE, 0x1EB, 0x155, 0x161, 0x163, 0xFB, 0x1E7D, 0x175, 0x1E8B, 0xFD, 0x17E
$upper = 0xC5, 0x181, 0xC7, 0xD0, 0xC9, 0x191, 0x11C, 0x124, 0xCE, 0x134, 0x136, 0x13B, 0x1E40,
         0xD1, 0xD6, 0xDE, 0x1EA, 0x154, 0x160, 0x162, 0xDB, 0x1E7C, 0x174, 0x1E8A, 0xDD, 0x17D
$map = @{}
for ($i = 0; $i -lt 26; $i++) {
    $map[[char](0x61 + $i)] = [char]$lower[$i]
    $map[[char](0x41 + $i)] = [char]$upper[$i]
}
$pad = [string][char]0xB7

function ConvertTo-Pseudo([string]$text) {
    $parts = [regex]::Split($text, '(\{\d+(?:[,:][^}]*)?\})')
    $out = foreach ($part in $parts) {
        if ($part -match '^\{\d+') { $part; continue }
        -join ($part.ToCharArray() | ForEach-Object { if ($map.ContainsKey($_)) { $map[$_] } else { $_ } })
    }
    $padding = $pad * [math]::Ceiling($text.Length * 0.4)
    '[' + (-join $out) + ' ' + $padding + ']'
}

New-Item -ItemType Directory -Force $Destination | Out-Null
foreach ($file in Get-ChildItem $Source -Filter *.resw) {
    [xml]$doc = Get-Content $file.FullName -Raw -Encoding UTF8
    foreach ($data in $doc.root.data) {
        if ($data.name -like '*.AccessKey') { continue }
        $data.value = ConvertTo-Pseudo $data.value
    }
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($true)
    $writer = [System.Xml.XmlWriter]::Create((Join-Path $Destination "$($file.BaseName).lang-qps-ploc.resw"), $settings)
    try { $doc.Save($writer) } finally { $writer.Dispose() }
}
