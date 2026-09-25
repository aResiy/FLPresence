# FLPresence uninstaller: removes app, bridge script, autostart, registry entry.
$ErrorActionPreference = 'SilentlyContinue'

Write-Host "=== FLPresence uninstaller ===" -ForegroundColor Cyan

Stop-Process -Name 'FLPresence' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run\FLPresence' -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FLPresence' -Recurse -Force

$docs = [Environment]::GetFolderPath('MyDocuments')
Remove-Item (Join-Path $docs 'Image-Line\FL Studio\Settings\Hardware\FLPresence') -Recurse -Force

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FLPresence'
$uninstallSelf = Join-Path $installDir 'uninstall.ps1'
Remove-Item $installDir -Recurse -Force

Write-Host "Removed: app, bridge script, autostart, registry entries."
Write-Host "Kept: settings (%APPDATA%\FLPresence) and logs (%LOCALAPPDATA%\FLPresence\logs)."
Start-Sleep -Seconds 2
