# FLPresence installer (per-user, no admin required).
# - publishes the Companion as a self-contained single-file exe
# - installs to %LOCALAPPDATA%\Programs\FLPresence
# - deploys the FL Studio MIDI bridge script
# - registers optional autostart + an Uninstall entry
#
# Usage:  powershell -ExecutionPolicy Bypass -File install.ps1 [-NoAutostart] [-NoBridge]
param(
    [switch]$NoAutostart,
    [switch]$NoBridge,
    [string]$InstallDir = ''
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$version = "1.3.1"
$installDir = $InstallDir
if (-not $installDir) {
    $installDir = Join-Path $env:LOCALAPPDATA 'Programs\FLPresence'
    # fall back to the repo's drive when the profile drive is out of space
    $lappdataDrive = ($env:LOCALAPPDATA)[0]
    if ((Get-PSDrive $lappdataDrive).Free -lt 200MB) {
        $installDir = ($repo[0] + ':\Programs\FLPresence')
        Write-Host "NOTE: $lappdataDrive`: is low on space - installing to $installDir" -ForegroundColor Yellow
    }
}
if (-not $env:NUGET_PACKAGES) {
    $cacheDrive = ($env:USERPROFILE)[0]
    $repoDrive = $repo[0]
    if ((Get-PSDrive $cacheDrive).Free -lt 2GB -and $cacheDrive -ne $repoDrive) {
        $env:NUGET_PACKAGES = $repoDrive + ':\.nuget-packages'
        Write-Host "NOTE: NuGet cache redirected to $env:NUGET_PACKAGES" -ForegroundColor Yellow
    }
}

Write-Host "=== FLPresence installer v$version ===" -ForegroundColor Cyan

# 1. locate a dotnet with an actual SDK installed (a runtime-only dotnet is not enough)
$dotnet = $null
foreach ($cand in @("$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe", 'D:\dotnet\dotnet.exe', "$env:ProgramFiles\dotnet\dotnet.exe", 'dotnet')) {
    $exe = $cand
    if ($cand -eq 'dotnet') { try { $exe = (Get-Command dotnet -ErrorAction Stop).Source } catch { continue } }
    if (-not (Test-Path $exe)) { continue }
    $sdks = & $exe --list-sdks 2>$null
    if ($sdks) { $dotnet = $exe; break }
}
if (-not $dotnet) { throw ".NET 8 SDK not found. Install it: https://dot.net/v1/dotnet-install.ps1 (or winget install Microsoft.DotNet.SDK.8)" }
Write-Host "[1/6] dotnet SDK: $dotnet"

# 2. publish single-file self-contained exe
Write-Host "[2/6] Publishing Companion (self-contained, may take a minute)..."
& $dotnet publish (Join-Path $repo 'src\FLPresence.Companion\FLPresence.Companion.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $installDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 3. deploy bridge into FL Studio user data
if (-not $NoBridge) {
    Write-Host "[3/6] Deploying FL Studio bridge script..."
    $docs = [Environment]::GetFolderPath('MyDocuments')
    $hw = Join-Path $docs 'Image-Line\FL Studio\Settings\Hardware'
    if (-not (Test-Path $hw)) { throw "FL Studio user data not found: $hw" }
    $dest = Join-Path $hw 'FLPresence'
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $repo 'flstudio\FLPresence\device_FLPresence.py') (Join-Path $dest 'device_FLPresence.py') -Force
    # .ini marker: sits NEXT TO the script folder (like FL's own scripts)
    Set-Content (Join-Path $hw 'FLPresence.ini') "[Ini]`r`nVersion=2`r`n" -Encoding Ascii
    Write-Host "      Bridge: $dest\device_FLPresence.py"

    # auto-assign the script to MIDI inputs that have no controller script
    # (only while FL Studio is not running; never overwrites existing scripts).
    # Optional bridge enhancement for MIDI-keyboard owners; the base app
    # needs no MIDI at all.
    $fl = Get-Process 'FL64','FL32' -ErrorAction SilentlyContinue
    if ($fl) {
        Write-Host "      FL Studio is running - auto-assign skipped. Select manually:" -ForegroundColor Yellow
        Write-Host "      Options -> MIDI Settings -> Input -> Controller type -> FLPresence"
    } else {
        $midiKey = 'HKCU:\Software\Image-Line\FL Studio 24\Devices\MIDI input'
        if (Test-Path $midiKey) {
            $n = 0
            Get-ChildItem $midiKey | ForEach-Object {
                $name = $_.PSChildName
                $p = Get-ItemProperty $_.PSPath
                $virtual = ($name -eq 'FLPresence') -or ($name -like '*loopMIDI*')
                if (($virtual -or $p.Enabled -ne '0') -and [string]::IsNullOrWhiteSpace($p.ScriptFolder)) {
                    Set-ItemProperty $_.PSPath -Name ScriptFolder -Value 'FLPresence'
                    if ($virtual -and $p.Enabled -eq '0') { Set-ItemProperty $_.PSPath -Name Enabled -Value '1' }
                    Write-Host "      Auto-assigned FLPresence -> $name" -ForegroundColor Green
                    $n++
                }
            }
            if ($n -eq 0) { Write-Host "      No free MIDI inputs to auto-assign (or already assigned)." }
        }
    }

} else { Write-Host "[3/6] Bridge deployment skipped" }

# 4. uninstaller
Write-Host "[4/6] Writing uninstaller..."
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $installDir 'uninstall.ps1') -Force

# 5. autostart (watchdog: starts FLPresence only when FL Studio opens)
if (-not $NoAutostart) {
    Write-Host "[5/6] Registering autostart (starts with FL Studio)..."
    New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'FLPresence' `
        -Value "`"$installDir\FLPresence.exe`" --watch" -PropertyType String -Force | Out-Null
} else { Write-Host "[5/6] Autostart skipped" }

# 6. add/remove programs entry
Write-Host "[6/6] Registering uninstall entry..."
$uninst = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FLPresence'
New-Item -Force $uninst | Out-Null
Set-ItemProperty $uninst 'DisplayName' 'FLPresence'
Set-ItemProperty $uninst 'DisplayVersion' $version
Set-ItemProperty $uninst 'Publisher' 'FLPresence'
Set-ItemProperty $uninst 'DisplayIcon' "$installDir\FLPresence.exe"
Set-ItemProperty $uninst 'InstallLocation' $installDir
Set-ItemProperty $uninst 'UninstallString' "powershell -ExecutionPolicy Bypass -File `"$installDir\uninstall.ps1`""
Set-ItemProperty $uninst 'NoModify' 1 -Type DWord
Set-ItemProperty $uninst 'NoRemove' 0 -Type DWord

Write-Host ""
Write-Host "Installed: $installDir\FLPresence.exe" -ForegroundColor Green

# Start the tray app right away: presence works immediately (FL running) and
# the watchdog takes over afterwards. Single-instance mutex guards duplicates.
try {
    Start-Process (Join-Path $installDir 'FLPresence.exe') -WorkingDirectory $installDir
    Write-Host ""
    Write-Host "FLPresence started - presence is live in Discord while FL Studio runs." -ForegroundColor Green
} catch { Write-Host "Start FLPresence.exe to activate." -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== What you get ===" -ForegroundColor Yellow
Write-Host "- Works out of the box, NO MIDI keyboard needed: while FL Studio is in"
Write-Host "  focus, Discord shows the project name; when you switch away, Discord"
Write-Host "  shows nothing (no 'producing music' while you are not producing)."
Write-Host "- Optional (owners of a physical MIDI keyboard): assign the FLPresence"
Write-Host "  script in FL Studio (Options -> MIDI Settings -> Input -> Controller"
Write-Host "  type -> FLPresence) to also show transport, pattern, plugin and live notes."
Write-Host "- Discord Application ID is built in - no configuration required."
Write-Host "  Settings (tray -> Open Settings) are only for customization (privacy,"
Write-Host "  producer name, templates)."
