# Publishes a draft GitHub release once its version is live in the Microsoft Store, for the "Publish release when
# live" workflow. Reads the Store's public catalog (no sign-in): the live package's full name carries the version.
#   -Tag v2.3.5      the draft release to publish; empty: the newest draft release
#   -Until <time>    keep checking every -EveryMinutes until then (UTC)
#   -DryRun          only report what the catalog and GitHub say; publish nothing
# Writes "result=published|waiting|nothing" (and "tag=...") to $env:GITHUB_OUTPUT when it runs in Actions.
#
# Usage: pwsh -File build/Publish-WhenLive.ps1 -Tag v2.3.5 -DryRun
param(
    [string]$Tag,
    [string]$ProductId = '9NMBSZLLLN6V',
    [string]$Market = 'US',
    [datetime]$Until = [datetime]::UtcNow,
    [int]$EveryMinutes = 10,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Set-ActionOutput([string]$name, [string]$value) {
    if ($env:GITHUB_OUTPUT) { "$name=$value" >> $env:GITHUB_OUTPUT }
}

function Get-LiveVersion {
    $url = "https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=$ProductId&market=$Market&languages=en-US"
    try { $catalog = Invoke-RestMethod $url -TimeoutSec 60 }
    catch { Write-Host "Store catalog not reachable: $($_.Exception.Message)"; return $null }
    $names = @($catalog.Products | ForEach-Object { $_.DisplaySkuAvailabilities } | ForEach-Object { $_.Sku.Properties.Packages } |
        ForEach-Object { $_.PackageFullName })
    $versions = @($names | ForEach-Object { if ($_ -match '_(\d+\.\d+\.\d+\.\d+)_') { [version]$Matches[1] } })
    if (-not $versions) { Write-Host "No package version in the Store catalog ($Market)."; return $null }
    return ($versions | Sort-Object -Descending)[0]
}

# The draft to publish: -Tag, or the newest draft release. Null when there is none (or it's published already).
function Get-Draft {
    $releases = gh release list --limit 30 --json tagName,isDraft,createdAt | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'gh release list failed' }
    $drafts = @($releases | Where-Object isDraft | Sort-Object createdAt -Descending)
    if ($Tag) { return $drafts | Where-Object tagName -eq $Tag | Select-Object -First 1 }
    return $drafts | Select-Object -First 1
}

$draft = Get-Draft
if (-not $draft) {
    Write-Host "No draft release$(if ($Tag) { " $Tag" }) to publish (published already, or none)."
    Set-ActionOutput result nothing
    return
}
$Tag = $draft.tagName
Set-ActionOutput tag $Tag
if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "'$Tag' is not vX.Y.Z." }
$wanted = [version]"$($Matches[1]).0"
Write-Host "Waiting for $wanted in the Store ($Market) to publish the draft release $Tag, until $($Until.ToString('u'))."

while ($true) {
    $live = Get-LiveVersion
    if ($live) { Write-Host "$((Get-Date).ToUniversalTime().ToString('u')) live: $live" }
    # A newer version live counts too: the draft's version was in the Store before it.
    if ($live -and $live -ge $wanted) {
        if ($DryRun) { Write-Host "Would publish $Tag (dry run)." }
        else {
            gh release edit $Tag --draft=false
            if ($LASTEXITCODE -ne 0) { throw "gh release edit $Tag failed" }
            Write-Host "Published the release $Tag."
            if ($env:GITHUB_STEP_SUMMARY) { "### Release $Tag published`n`n$live is live in the Microsoft Store ($Market)." >> $env:GITHUB_STEP_SUMMARY }
        }
        Set-ActionOutput result published
        return
    }
    if ([datetime]::UtcNow.AddMinutes($EveryMinutes) -gt $Until) { break }
    Start-Sleep -Seconds ($EveryMinutes * 60)
    # Published by hand meanwhile: nothing left to do.
    if (-not (Get-Draft)) { Write-Host "$Tag isn't a draft any more."; Set-ActionOutput result nothing; return }
}
Write-Host "$wanted isn't live yet."
Set-ActionOutput result waiting
