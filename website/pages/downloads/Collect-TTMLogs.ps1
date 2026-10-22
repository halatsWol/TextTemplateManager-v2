# Collects TextTemplateManager's logs and the Microsoft Store and Windows events about it into one .zip file, to attach
# to a support e-mail or a GitHub issue. Needs no administrator rights and changes nothing on the PC.
#
# Collected: the app's update.log, crash.log, update-checks.txt and update-attempt.json (not the templates, settings or
# sync files), the installed package, the Store app, update policies and the update watchdog task, and the last -Days
# days of the Store, AppX deployment and application error events that concern the app. The user name, computer name
# and account IDs are replaced with placeholders; every file in the .zip is plain text and can be read before sending.
#
# Usage: powershell -ExecutionPolicy Bypass -File Collect-TTMLogs.ps1 [-Days 14] [-OutputPath <folder>] [-NoOpen]
# The website also offers it as Collect-TTMLogs.cmd for a double-click (built by build/Build-Website.ps1).
param(
    [int]$Days = 14,
    [string]$OutputPath = [Environment]::GetFolderPath('Desktop'),
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'
$name = 'MarflowSoftware.TextTemplateManager'
$family = 'MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny'
$appPattern = 'MarflowSoftware\.TextTemplateManager|9NMBSZLLLN6V|ttm2\.exe'
$since = (Get-Date).AddDays(-$Days)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path ([IO.Path]::GetTempPath()) "TTM-logs-$stamp"
New-Item -ItemType Directory -Path $work -Force | Out-Null

function Hide-Personal([string]$Text) {
    if (-not $Text) { return $Text }
    $Text = $Text -replace [regex]::Escape($env:USERPROFILE), '%USERPROFILE%'
    $Text = $Text -replace 'S-1-5-21-[\d-]+', '<sid>'
    # Very short names would also hit ordinary words.
    if ($env:USERNAME.Length -ge 3) { $Text = $Text -replace "\b$([regex]::Escape($env:USERNAME))\b", '<user>' }
    if ($env:COMPUTERNAME.Length -ge 3) { $Text = $Text -replace "\b$([regex]::Escape($env:COMPUTERNAME))\b", '<computer>' }
    return $Text
}

function Save-Text([string]$FileName, $Content) {
    $text = Hide-Personal (($Content | Out-String -Width 4096).TrimEnd() + "`r`n")
    [IO.File]::WriteAllText((Join-Path $work $FileName), $text, (New-Object Text.UTF8Encoding $true))
}

function Format-Event($Record) {
    $message = if ($Record.Message) { ($Record.Message -replace '\s*\r?\n\s*', ' | ').Trim() } else { "(no message, provider $($Record.ProviderName))" }
    '{0:yyyy-MM-dd HH:mm:ss.fff}  {1,-11} {2,5}  {3}' -f $Record.TimeCreated, $Record.LevelDisplayName, $Record.Id, $message
}

# The app's events, plus every event of the same operations (ActivityId): an install or update logs most of its steps
# without the package name.
function Get-AppEvents([string]$LogName, [string[]]$Provider) {
    $filter = @{ LogName = $LogName; StartTime = $since }
    if ($Provider) { $filter.ProviderName = $Provider }
    try { $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction Stop) }
    catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return "No events since $($since.ToString('yyyy-MM-dd'))." }
        return "Not readable: $($_.Exception.Message)"
    }
    $matching = @($events | Where-Object { $_.Message -match $appPattern })
    $activities = @{}
    foreach ($e in $matching) { if ($e.ActivityId) { $activities[$e.ActivityId] = $true } }
    $selected = @($events | Where-Object { ($_.ActivityId -and $activities.ContainsKey($_.ActivityId)) -or $_.Message -match $appPattern })
    if (-not $selected) { return "None of the $($events.Count) events since $($since.ToString('yyyy-MM-dd')) concern TextTemplateManager." }
    @("$($selected.Count) of $($events.Count) events since $($since.ToString('yyyy-MM-dd')), oldest first:", '')
    $selected | Sort-Object TimeCreated, RecordId | ForEach-Object { Format-Event $_ }
}

Write-Host 'Collecting TextTemplateManager logs...'

# The PC, the app and what decides its updates.
$os = Get-CimInstance Win32_OperatingSystem
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$info = @(
    "Collected:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
    "Windows:     $($os.Caption) $($cv.DisplayVersion) (build $($cv.CurrentBuild).$($cv.UBR), $($os.OSArchitecture))"
    "Language:    $((Get-Culture).Name), display $((Get-UICulture).Name)"
    "PowerShell:  $($PSVersionTable.PSVersion)"
    ''
    '---- TextTemplateManager package ----'
)
$package = @(Get-AppxPackage -Name $name -ErrorAction SilentlyContinue)
if ($package) { $info += $package | Format-List Name, Version, Architecture, Status, SignatureKind, IsDevelopmentMode, InstallLocation | Out-String }
else { $info += 'Not installed for this user.' }
$process = @(Get-Process ttm2 -ErrorAction SilentlyContinue)
$info += if ($process) { $process | Format-Table Id, StartTime, Path -AutoSize | Out-String } else { 'Not running.' }
$info += ''
$info += '---- Microsoft Store app ----'
$store = Get-AppxPackage -Name Microsoft.WindowsStore -ErrorAction SilentlyContinue
$info += if ($store) { "Installed, version $($store.Version)" } else { 'Not installed.' }
$info += ''
$info += '---- Policies ----'
foreach ($key in 'HKLM:\SOFTWARE\Policies\Marflow Software\TextTemplateManager', 'HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore') {
    $info += $key
    $values = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
    $info += if ($values) { $values | Select-Object * -ExcludeProperty PS* | Format-List | Out-String } else { '  not set' }
}
$info += ''
$info += '---- Update watchdog task ----'
$task = Get-ScheduledTask -TaskName 'TextTemplateManager update watchdog' -ErrorAction SilentlyContinue
if ($task) {
    $taskInfo = $task | Get-ScheduledTaskInfo
    $info += "State $($task.State), last run $($taskInfo.LastRunTime), result $($taskInfo.LastTaskResult), next run $($taskInfo.NextRunTime)"
}
else { $info += 'None (normal: it exists only during an update).' }
Save-Text 'system.txt' $info

# The app's own logs; the templates and settings stay out.
$state = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState"
if (Test-Path $state) {
    Save-Text 'app-folder.txt' (Get-ChildItem -LiteralPath $state -Force | Format-Table Name, Length, LastWriteTime -AutoSize)
    foreach ($file in 'update.log', 'crash.log', 'update-checks.txt', 'update-attempt.json') {
        $path = Join-Path $state $file
        if (Test-Path -LiteralPath $path) { Save-Text "app-$file" ([IO.File]::ReadAllText($path)) }
    }
}
else { Save-Text 'app-folder.txt' "No app data folder ($state)." }

Write-Host "Reading the event logs of the last $Days days (this can take a minute)..."
Save-Text 'events-store.txt' (Get-AppEvents 'Microsoft-Windows-Store/Operational')
Save-Text 'events-appx-deployment.txt' (Get-AppEvents 'Microsoft-Windows-AppXDeploymentServer/Operational')
Save-Text 'events-app-errors.txt' (Get-AppEvents 'Application' 'Application Error', 'Application Hang', '.NET Runtime', 'Windows Error Reporting')

if (-not (Test-Path $OutputPath)) { New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null }
$zip = Join-Path $OutputPath "TTM-logs-$stamp.zip"
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip
Remove-Item -LiteralPath $work -Recurse -Force

Write-Host ''
Write-Host "Saved: $zip"
Write-Host 'Attach this file to your e-mail to contact@kmarflow.com or to your GitHub issue.'
if (-not $NoOpen) { Start-Process explorer.exe "/select,`"$zip`"" }
