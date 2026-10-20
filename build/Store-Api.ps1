# Partner Center submission API (https://manage.devcenter.microsoft.com) for the release workflow:
#   -Check                  sign in and read the app; shows the real reason when access is refused
#   -ReleaseNotesFile <f>   set "What's new" in every listing language of the pending submission
#   -Commit                 commit the pending submission (certification starts)
# The credentials come from the environment: MSSTORE_TENANT_ID, MSSTORE_CLIENT_ID, MSSTORE_CLIENT_SECRET.
#
# Usage: pwsh -File build/Store-Api.ps1 -AppId 9NMBSZLLLN6V -Check
param(
    [Parameter(Mandatory)][string]$AppId,
    [switch]$Check,
    [string]$ReleaseNotesFile,
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
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
        $call.Body = if ($null -ne $Body) { [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 100)) } else { [byte[]]@() }
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

if ($ReleaseNotesFile) {
    $id = $app.pendingApplicationSubmission.id
    if (-not $id) { throw 'No pending submission to set "What''s new" in (the package upload creates it).' }
    $notes = Get-Content $ReleaseNotesFile -Raw
    $submission = Invoke-Api Get "$base/$AppId/submissions/$id"
    $languages = @($submission.listings.PSObject.Properties)
    if (-not $languages) { throw 'The submission has no listings.' }
    foreach ($language in $languages) {
        $language.Value.baseListing | Add-Member -NotePropertyName releaseNotes -NotePropertyValue $notes -Force
    }
    $null = Invoke-Api Put "$base/$AppId/submissions/$id" $submission
    Write-Host "What's new set for: $($languages.Name -join ', ')"
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
