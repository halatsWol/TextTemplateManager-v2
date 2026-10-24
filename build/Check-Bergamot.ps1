# Checks whether Mozilla ships a newer Bergamot engine than the one in src/TextTemplateManager/Assets/translator
# (recorded in build/bergamot.json), for the "Bergamot check" workflow. Dependabot can't see it: the engine isn't a
# package, Firefox loads it from Remote Settings. Compares:
#   - the engine (wasm) record in Remote Settings (translations-wasm-v2): release, revision, checksum;
#   - the JavaScript loader in Firefox's source tree (moz.yaml next to bergamot-translator.js): revision;
#   - the language models (translations-models-v2): whether a newer model format appeared, and whether models in
#     the format the shipped engine reads are still offered (if not, downloads in the app stop working).
# Opens an issue when something changed, or updates the open one.
#   -DryRun   only report; no issue
# Usage: pwsh -File build/Check-Bergamot.ps1 -DryRun
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$settings = 'https://firefox.settings.services.mozilla.com/v1/buckets/main/collections'
$loaderYaml = 'https://raw.githubusercontent.com/mozilla-firefox/firefox/main/toolkit/components/translations/bergamot-translator/moz.yaml'
$title = 'Bergamot engine update available'

$shipped = Get-Content (Join-Path $PSScriptRoot 'bergamot.json') -Raw | ConvertFrom-Json

$wasm = (Invoke-RestMethod "$settings/translations-wasm-v2/changeset?_expected=0" -TimeoutSec 60).changes |
    Where-Object { $_.name -eq 'bergamot-translator' } | Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
$loaderRevision = if ((Invoke-RestMethod $loaderYaml -TimeoutSec 60) -match 'revision:\s*([0-9a-f]+)') { $Matches[1] } else { '' }
$models = (Invoke-RestMethod "$settings/translations-models-v2/changeset?_expected=0" -TimeoutSec 60).changes
$majors = @($models | ForEach-Object { [int]($_.version -replace '^(\d+).*', '$1') } | Sort-Object -Unique)
$current = @($models | Where-Object { [int]($_.version -replace '^(\d+).*', '$1') -eq $shipped.modelMajorVersion }).Count

$findings = @()
if ($wasm.decompressedHash -ne $shipped.wasmSha256) {
    $findings += "- **Engine:** Remote Settings now has release $($wasm.release), revision ``$($wasm.revision)``, record version $($wasm.version) (Firefox $($wasm.fx_release)); shipped: $($shipped.release), ``$($shipped.wasmRevision)``."
}
if ($loaderRevision -and $loaderRevision -ne $shipped.loaderRevision) {
    $findings += "- **Loader:** Firefox's ``bergamot-translator.js`` is at revision ``$loaderRevision``; shipped: ``$($shipped.loaderRevision)``."
}
$newer = @($majors | Where-Object { $_ -gt $shipped.modelMajorVersion })
if ($newer) {
    $findings += "- **Model format:** Mozilla offers models in format $($newer -join ', '); the shipped engine reads format $($shipped.modelMajorVersion), so the app ignores them."
}
if ($current -eq 0) {
    $findings += "- **No more models in format $($shipped.modelMajorVersion):** downloads in Settings > Translation find nothing until the app ships a newer engine."
}

Write-Host "engine: $($wasm.release) $($wasm.revision) | loader: $loaderRevision | model formats: $($majors -join ', ') ($current records in format $($shipped.modelMajorVersion))"
if (-not $findings) { Write-Host 'Bergamot: up to date.'; return }

$body = @"
Mozilla ships something newer than the Bergamot engine in ``src/TextTemplateManager/Assets/translator`` (recorded in ``build/bergamot.json``):

$($findings -join "`n")

To update:
1. Take ``bergamot-translator.js`` from Firefox (``toolkit/components/translations/bergamot-translator``) and the wasm from Remote Settings (``translations-wasm-v2``, record ``bergamot-translator``, unpacked with zstd); check that the loader belongs to the engine (same release, the same Firefox version), and the wasm's SHA-256 against the record's ``decompressedHash``.
2. Run the JavaScript tests with real models (``TTM_TEST_MODELS``, see docs/architecture/12-testing.md) and check translation in the app.
3. Update ``build/bergamot.json``, the version in ``THIRD-PARTY-NOTICES.txt`` and in docs/architecture.

Checked $(Get-Date -AsUTC -Format 'yyyy-MM-dd HH:mm') UTC by the Bergamot check workflow.
"@
Write-Host $body
if ($DryRun) { return }

$open = gh issue list --state open --search "in:title `"$title`"" --json number --jq '.[0].number'
if ($open) { gh issue edit $open --body $body | Out-Null; Write-Host "updated issue #$open" }
else { gh issue create --title $title --body $body | Out-Null; Write-Host 'opened an issue' }
