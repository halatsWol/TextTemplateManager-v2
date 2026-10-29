# Partner Center submission API (https://manage.devcenter.microsoft.com) for the release workflow:
#   -Check                  sign in and read the app; shows the real reason when access is refused
#   -Submit                 a new submission from the last published one, with -Package (the .msixupload; the old
#                           packages are replaced) and the listings from -ListingDir (build/Release-Notes.ps1
#                           -ListingOutDir): every listing gets the texts and screenshots of its language's folder, or
#                           of one with the same base language (es-mx gets es-ES); a listing without either gets only
#                           the English "What's new". A language folder no listing uses is added as a new listing
#                           when it has screenshots.
#                           Packages and screenshots go up as one ZIP to the submission's upload URL. Not committed.
#   -Commit                 commit the pending (or just made) submission; certification starts
#   -Preview <languages>    with -ListingDir: only show what -Submit would do for these listing languages
#                           (comma-separated, as Partner Center names them: en-us,de-de); no sign-in
# The credentials come from the environment: MSSTORE_TENANT_ID, MSSTORE_CLIENT_ID, MSSTORE_CLIENT_SECRET.
#
# Usage: pwsh -File build/Store-Api.ps1 -AppId 9NMBSZLLLN6V -Submit -Package x.msixupload -ListingDir store-listing [-Commit]
param(
    [Parameter(Mandatory)][string]$AppId,
    [switch]$Check,
    [switch]$Submit,
    [string]$Package,
    [string]$ListingDir,
    [switch]$Commit,
    [string]$Preview
)

$ErrorActionPreference = 'Stop'

# The listing folder for a Partner Center listing language: its own, else one with the same base language, else none.
function Select-Language([string]$Listing, [string[]]$Available) {
    $exact = $Available | Where-Object { $_ -eq $Listing } | Select-Object -First 1
    if ($exact) { return $exact }
    $Available | Where-Object { $_.Split('-')[0] -eq $Listing.Split('-')[0] } | Sort-Object | Select-Object -First 1
}

function Read-Listings([string]$Dir) {
    $listings = @{}
    foreach ($d in Get-ChildItem $Dir -Directory) {
        $text = { param($n) Get-Content (Join-Path $d.FullName "$n.txt") -Raw -Encoding utf8 }
        $lines = { param($n) @((& $text $n) -split "`r?`n" | Where-Object { $_.Trim() }) }
        $listings[$d.Name] = [pscustomobject]@{
            Title = & $text 'title'; ShortDescription = & $text 'short-description'; Description = & $text 'description'
            Features = & $lines 'features'; Keywords = & $lines 'search-terms'; WhatsNew = & $text 'whats-new'
            Copyright = & $text 'copyright'; Captions = & $lines 'screenshot-captions'
            Screenshots = @(Get-ChildItem (Join-Path $d.FullName 'screenshots') -Filter *.png -ErrorAction SilentlyContinue | Sort-Object Name)
        }
    }
    if (-not $listings.ContainsKey('en-US')) { throw "No en-US listing in $Dir." }
    $listings
}

function Set-Field($obj, [string]$name, $value) { $obj | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force }

# Texts and, when the folder has them, screenshots of one language into a base listing; returns the images to upload.
function Update-Listing($baseListing, [string]$language, $listing) {
    Set-Field $baseListing title $listing.Title
    Set-Field $baseListing shortDescription $listing.ShortDescription
    Set-Field $baseListing description $listing.Description
    Set-Field $baseListing features @($listing.Features)
    Set-Field $baseListing keywords @($listing.Keywords)
    Set-Field $baseListing releaseNotes $listing.WhatsNew
    Set-Field $baseListing copyrightAndTrademarkInfo $listing.Copyright
    if (-not $listing.Screenshots) { return @() }
    $images = @($baseListing.images | Where-Object { $_ })
    foreach ($image in $images | Where-Object { $_.imageType -eq 'Screenshot' }) { Set-Field $image fileStatus 'PendingDelete' }
    $upload = for ($i = 0; $i -lt $listing.Screenshots.Count; $i++) {
        $shot = $listing.Screenshots[$i]
        # Flat names in the ZIP: one name per language and image.
        $entry = "$language-$($shot.Name)"
        $images += [pscustomobject]@{ fileName = $entry; fileStatus = 'PendingUpload'; imageType = 'Screenshot'
            description = $(if ($i -lt $listing.Captions.Count) { $listing.Captions[$i] } else { '' }) }
        [pscustomobject]@{ Entry = $entry; Path = $shot.FullName }
    }
    Set-Field $baseListing images $images
    @($upload)
}

# Applies the listing folders to the submission's listings and adds the unused languages; returns what was done per
# listing and the images to upload.
function Update-Listings($submission, $listings) {
    $result = [pscustomobject]@{ Report = @(); Upload = @() }
    $used = @()
    foreach ($property in @($submission.listings.PSObject.Properties)) {
        $language = Select-Language $property.Name @($listings.Keys)
        if ($language) {
            $result.Upload += Update-Listing $property.Value.baseListing $language $listings[$language]
            $used += $language
            $result.Report += "$($property.Name) <- $language (texts$(if ($listings[$language].Screenshots) { ", $($listings[$language].Screenshots.Count) screenshots" }))"
        }
        else {
            Set-Field $property.Value.baseListing releaseNotes $listings['en-US'].WhatsNew
            $result.Report += "$($property.Name) <- en-US (What's new only)"
        }
    }
    foreach ($language in $listings.Keys | Where-Object { $_ -notin $used } | Sort-Object) {
        # A listing needs at least one screenshot.
        if (-not $listings[$language].Screenshots) { $result.Report += "$($language.ToLowerInvariant()): not added, no screenshots"; continue }
        $baseListing = [pscustomobject]@{ images = @() }
        $result.Upload += Update-Listing $baseListing $language $listings[$language]
        $submission.listings | Add-Member -NotePropertyName $language.ToLowerInvariant() -NotePropertyValue ([pscustomobject]@{ baseListing = $baseListing; platformOverrides = [pscustomobject]@{} })
        $result.Report += "$($language.ToLowerInvariant()) <- $language (new listing, $($listings[$language].Screenshots.Count) screenshots)"
    }
    $result
}

if ($Preview) {
    if (-not $ListingDir) { throw '-Preview needs -ListingDir.' }
    $listings = Read-Listings $ListingDir
    $fake = [pscustomobject]@{ listings = [pscustomobject]@{} }
    foreach ($l in $Preview.Split(',').Trim()) {
        $image = [pscustomobject]@{ fileName = 'old.png'; fileStatus = 'Uploaded'; imageType = 'Screenshot'; id = '1' }
        $logo = [pscustomobject]@{ fileName = 'logo.png'; fileStatus = 'Uploaded'; imageType = 'StoreLogoSquare'; id = '2' }
        $fake.listings | Add-Member -NotePropertyName $l -NotePropertyValue ([pscustomobject]@{ baseListing = [pscustomobject]@{ images = @($image, $logo) } })
    }
    $result = Update-Listings $fake $listings
    $result.Report
    "zip: $(@($result.Upload).Count) images ($((@($result.Upload) | Select-Object -First 3 | ForEach-Object Entry) -join ', ') ...)"
    foreach ($p in $fake.listings.PSObject.Properties) {
        $i = @($p.Value.baseListing.images)
        "  $($p.Name): $(@($i | Where-Object fileStatus -eq 'PendingUpload').Count) new, $(@($i | Where-Object fileStatus -eq 'PendingDelete').Count) removed, $(@($i | Where-Object fileStatus -eq 'Uploaded').Count) kept; features $(@($p.Value.baseListing.features).Count), keywords $(@($p.Value.baseListing.keywords).Count)"
    }
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

# The ZIP goes up in 4 MB blocks: one request takes at most 64 MB with the upload URL's storage API version.
function Send-Blob([string]$Url, [string]$Path) {
    $blockHeaders = @{ 'x-ms-version' = '2019-12-12' }
    $ids = @()
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] (4MB)
        while (($n = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $id = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(('{0:d6}' -f $ids.Count)))
            $chunk = New-Object byte[] $n
            [Array]::Copy($buffer, $chunk, $n)
            $null = Invoke-WebRequest -Method Put -Uri "$Url&comp=block&blockid=$([Uri]::EscapeDataString($id))" -Headers $blockHeaders -Body $chunk -ContentType 'application/octet-stream'
            $ids += $id
        }
    }
    finally { $stream.Dispose() }
    $list = '<?xml version="1.0" encoding="utf-8"?><BlockList>' + (($ids | ForEach-Object { "<Latest>$_</Latest>" }) -join '') + '</BlockList>'
    $null = Invoke-WebRequest -Method Put -Uri "$Url&comp=blocklist" -Headers $blockHeaders -Body ([Text.Encoding]::UTF8.GetBytes($list)) -ContentType 'application/xml'
    Write-Host "Uploaded $([math]::Round((Get-Item $Path).Length / 1MB, 1)) MB in $($ids.Count) blocks."
}

function Complete-Submission([string]$id) {
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

$app = Invoke-Api Get "$base/$AppId"
Write-Host "Access OK: $($app.primaryName) ($($app.id))"

if ($Check) {
    Write-Host "Last published submission: $($app.lastPublishedApplicationSubmission.id)"
    Write-Host "Pending submission: $(if ($app.pendingApplicationSubmission) { $app.pendingApplicationSubmission.id } else { 'none' })"
    if ($app.pendingApplicationSubmission) {
        $pending = Invoke-Api Get "$base/$AppId/submissions/$($app.pendingApplicationSubmission.id)"
        foreach ($package in $pending.applicationPackages) { Write-Host "  $($package.fileName): $($package.fileStatus)" }
        foreach ($listing in $pending.listings.PSObject.Properties) { Write-Host "  listing $($listing.Name): $(@($listing.Value.baseListing.images | Where-Object imageType -eq 'Screenshot').Count) screenshots" }
    }
}

$submissionId = $app.pendingApplicationSubmission.id
if ($Submit) {
    if (-not (Test-Path $Package)) { throw "No package $Package." }
    if (-not $ListingDir) { throw '-Submit needs -ListingDir.' }
    if ($submissionId) { throw "A submission ($submissionId) is already pending: finish or delete it in Partner Center first." }
    $listings = Read-Listings $ListingDir
    $submission = Invoke-Api Post "$base/$AppId/submissions"
    $submissionId = $submission.id
    Write-Host "Submission $submissionId created."

    $packageName = Split-Path $Package -Leaf
    foreach ($p in @($submission.applicationPackages | Where-Object { $_ })) { Set-Field $p fileStatus 'PendingDelete' }
    Set-Field $submission applicationPackages (@($submission.applicationPackages | Where-Object { $_ }) +
        [pscustomobject]@{ fileName = $packageName; fileStatus = 'PendingUpload'; minimumDirectXVersion = 'None'; minimumSystemRam = 'None' })
    $known = @($submission.listings.PSObject.Properties.Name)
    $result = Update-Listings $submission $listings
    $result.Report | ForEach-Object { Write-Host "Listing $_" }
    $images = @($result.Upload | Where-Object { $_ })
    try { $updated = Invoke-Api Put "$base/$AppId/submissions/$submissionId" $submission }
    catch {
        # Whether the API takes a listing language the last submission didn't have isn't documented; without them
        # the submission still goes through, and the new languages are added in Partner Center by hand once.
        $new = @($submission.listings.PSObject.Properties.Name | Where-Object { $_ -notin $known })
        if (-not $new) { throw }
        $text = "Partner Center refused the new listings ($($new -join ', ')): $($_.Exception.Message) Add them in Partner Center (Store listings, from releases/vX.Y.Z/<language>/store.md and its screenshots)."
        if ($env:GITHUB_ACTIONS) { Write-Host "::warning::$text" } else { Write-Warning $text }
        foreach ($n in $new) { $submission.listings.PSObject.Properties.Remove($n) }
        $images = @($images | Where-Object { $entry = $_.Entry; -not ($new | Where-Object { $entry -like "$_-*" }) })
        $updated = Invoke-Api Put "$base/$AppId/submissions/$submissionId" $submission
    }

    $zip = Join-Path ([IO.Path]::GetTempPath()) "submission-$submissionId.zip"
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Resolve-Path $Package).Path, $packageName)
        foreach ($image in $images) { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $image.Path, $image.Entry) }
    }
    finally { $archive.Dispose() }
    Send-Blob $(if ($updated.fileUploadUrl) { $updated.fileUploadUrl } else { $submission.fileUploadUrl }) $zip
    Remove-Item $zip
    Write-Host "Submission $submissionId ready: $packageName, $($images.Count) screenshots."
}

if ($Commit) {
    if (-not $submissionId) { throw 'No pending submission to commit.' }
    Complete-Submission $submissionId
}
