<#
.SYNOPSIS
  Waits for Studio to exit, then copies the freshly built plugin DLL over the installed one.
  Useful during development: the installed DLL is locked while Studio runs. Needs modify rights
  on the BepInEx folder (install.ps1 grants them), no elevation.
#>
param(
    [string]$StudioDir = "C:\Program Files\Studio 2.0 EarlyAccess",
    [string]$PluginDll = "",
    [int]$TimeoutMinutes = 60,
    [string]$LogFile = ""
)
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $PluginDll) { $PluginDll = Join-Path (Split-Path -Parent $scriptRoot) "src\StudioSpaceMouse\bin\Release\net472\StudioSpaceMouse.dll" }
if (-not $LogFile) { $LogFile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Temp\StudioSpaceMouse-replace.log" }
$dest = Join-Path $StudioDir "BepInEx\plugins\StudioSpaceMouse\StudioSpaceMouse.dll"

function Log($m) { $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $m; Add-Content -Path $LogFile -Value $line }

Log "waiting for Studio to exit (source $PluginDll, $((Get-Item $PluginDll).LastWriteTime))"
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Process -Name Studio -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
if (Get-Process -Name Studio -ErrorAction SilentlyContinue) { Log "timed out; Studio still running"; exit 1 }

Start-Sleep -Seconds 1
$wrapper = Join-Path (Split-Path -Parent $PluginDll) "TDx.SpaceMouse.Navigation3D.dll"
for ($i = 0; $i -lt 10; $i++) {
    try {
        Copy-Item -Force $PluginDll $dest
        if (Test-Path $wrapper) { Copy-Item -Force $wrapper (Split-Path -Parent $dest) }
        Log "copied to $dest"; exit 0
    }
    catch { Start-Sleep -Seconds 1 }
}
Log "copy failed: file still locked"
exit 1
