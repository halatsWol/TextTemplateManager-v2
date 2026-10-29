# Partner Center submission API (https://manage.devcenter.microsoft.com) for the release workflow:
#   -Check                  sign in and read the app; shows the real reason when access is refused
#   -ReleaseNotesDir <d>    set "What's new" in every listing language of the pending submission, from the
#                           <language>.txt files in <d> (build/Release-Notes.ps1 -StoreOutDir): the listing's own
#                           language, else one with the same base language (es-mx gets es-ES), else en-US
#   -Commit                 commit the pending submission (certification starts)
#   -Preview <languages>    with -ReleaseNotesDir: only show which file each of these listing languages would get
#                           (comma-separated, as Partner Center names them: en-us,de-de); no sign-in
# The credentials come from the environment: MSSTORE_TENANT_ID, MSSTORE_CLIENT_ID, MSSTORE_CLIENT_SECRET.
#
# Usage: pwsh -File build/Store-Api.ps1 -AppId 9NMBSZLLLN6V -Check
param(
    [Parameter(Mandatory)][string]$AppId,
    [switch]$Check,
    [string]$ReleaseNotesDir,
    [switch]$Commit,
    [string]$Preview
)

$ErrorActionPreference = 'Stop'

function Select-ReleaseNotes([string]$Listing, [string[]]$Available) {
    $exact = $Available | Where-Object { $_ -eq $Listing } | Select-Object -First 1
    if ($exact) { return $exact }
    $sameBase = $Available | Where-Object { $_.Split('-')[0] -eq $Listing.Split('-')[0] } | Sort-Object | Select-Object -First 1
    if ($sameBase) { return $sameBase }
    'en-US'
}

function Get-ReleaseNotes([string]$Dir) {
    $notes = @{}
    foreach ($f in Get-ChildItem $Dir -Filter *.txt) { $notes[$f.BaseName] = Get-Content $f.FullName -Raw }
    if (-not $notes.ContainsKey('en-US')) { throw "No en-US.txt in $Dir." }
    $notes
}

# Languages with a What's new that no listing uses; a listing needs its screenshots, so it is added by hand.
function Get-Unlisted($Notes, [string[]]$Listings) {
    $used = @($Listings | ForEach-Object { Select-ReleaseNotes $_ @($Notes.Keys) })
    @($Notes.Keys | Where-Object { $_ -notin $used } | Sort-Object)
}

if ($Preview) {
    if (-not $ReleaseNotesDir) { throw '-Preview needs -ReleaseNotesDir.' }
    $notes = Get-ReleaseNotes $ReleaseNotesDir
    $listings = $Preview.Split(',').Trim()
    foreach ($listing in $listings) { "$listing <- $(Select-ReleaseNotes $listing @($notes.Keys)).txt" }
    if ($unlisted = Get-Unlisted $notes $listings) { "no listing yet: $($unlisted -join ', ')" }
    return
}
$base = 'https://manage.devcenter.microsoft.com/v1.0/my/applications'

foreach ($name in 'MSSTORE_TENANT_ID', 'MSSTORE_CLIENT_ID', 'MSSTORE_CLIENT_SECRET') {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing $name (see docs/PUBLISHING.md)." }
}

try {
    $token = (Invoke-RestMethod -Method Post "https://login.microsoftonline.com/$env:MSSTORE_TENANT_ID/oauth2/token" -Body @{
        grant_type = 'client_credentials'; client_id = $env:MSSTORE_CLIENT_ID; client_secret = $env:MSSTORE_CLIENT_SECRET
        resource = 'https://manage.devcenter.microsoft.com' }).access_token
}
catch { throw "Sign-in to Microsoft Entra failed (tenant ID, client ID or client secret): $($_.ErrorDetails.Message)" }
if ($env:GITHUB_ACTIONS) { Write-Host "::add-mask::$token" }
$headers = @{ Authorization = "Bearer $token" }

function Invoke-Api([string]$Method, [string]$Uri, $Body) {
    $call = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    # The API refuses a POST without a JSON content type, even one without a body (commit).
    if ($Method -ne 'Get') {
        $call.ContentType = 'application/json; charset=utf-8'
        # Assigned in each branch: an if expression would unroll the byte array into separate numbers.
        if ($null -ne $Body) { $call.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 100)) }
        else { $call.Body = [byte[]]@() }
    }
    try { Invoke-RestMethod @call }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        $hint = if ($code -eq 403) { ' The Entra application needs the Manager role in Partner Center (User management) and access to this app.' } else { '' }
        throw "Partner Center: $Method $Uri failed with HTTP ${code}: $($_.ErrorDetails.Message)$hint"
    }
}

$app = Invoke-Api Get "$base/$AppId"
Write-Host "Access OK: $($app.primaryName) ($($app.id))"

if ($Check) {
    Write-Host "Last published submission: $($app.lastPublishedApplicationSubmission.id)"
    Write-Host "Pending submission: $(if ($app.pendingApplicationSubmission) { $app.pendingApplicationSubmission.id } else { 'none' })"
    # Partner Center shows an uploaded package only after the commit; until then only the API lists it (PendingUpload).
    if ($app.pendingApplicationSubmission) {
        $pending = Invoke-Api Get "$base/$AppId/submissions/$($app.pendingApplicationSubmission.id)"
        foreach ($package in $pending.applicationPackages) { Write-Host "  $($package.fileName): $($package.fileStatus)" }
    }
}

if ($ReleaseNotesDir) {
    $id = $app.pendingApplicationSubmission.id
    if (-not $id) { throw 'No pending submission to set "What''s new" in (the package upload creates it).' }
    $notes = Get-ReleaseNotes $ReleaseNotesDir
    $submission = Invoke-Api Get "$base/$AppId/submissions/$id"
    $languages = @($submission.listings.PSObject.Properties)
    if (-not $languages) { throw 'The submission has no listings.' }
    foreach ($language in $languages) {
        $from = Select-ReleaseNotes $language.Name @($notes.Keys)
        $language.Value.baseListing | Add-Member -NotePropertyName releaseNotes -NotePropertyValue $notes[$from] -Force
        Write-Host "What's new for $($language.Name): $from"
    }
    $null = Invoke-Api Put "$base/$AppId/submissions/$id" $submission
    if ($unlisted = Get-Unlisted $notes @($languages.Name)) {
        $text = "No Store listing yet for $($unlisted -join ', '): add it in Partner Center (Store listings, with its screenshots) from releases/vX.Y.Z/<language>/store.md."
        if ($env:GITHUB_ACTIONS) { Write-Host "::warning::$text" } else { Write-Warning $text }
    }
}

if ($Commit) {
    $id = $app.pendingApplicationSubmission.id
    if (-not $id) { throw 'No pending submission to commit.' }
    $null = Invoke-Api Post "$base/$AppId/submissions/$id/commit"
    # The commit is processed asynchronously: wait until it has left CommitStarted (certification itself takes hours).
    for ($i = 0; $i -lt 30; $i++) {
        $status = Invoke-Api Get "$base/$AppId/submissions/$id/status"
        if ($status.status -ne 'CommitStarted') { break }
        Start-Sleep 20
    }
    Write-Host "Submission $id status: $($status.status)"
    if ($status.status -like '*Failed') {
        $errors = @($status.statusDetails.errors) + @($status.statusDetails.warnings) | Where-Object { $_ } | ForEach-Object { "$($_.code): $($_.details)" }
        throw "The commit failed: $($errors -join '; ')"
    }
}
