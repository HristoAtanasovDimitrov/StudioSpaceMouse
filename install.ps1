<#
.SYNOPSIS
  Installs BepInEx and the StudioSpaceMouse plugin into a BrickLink Studio 2.0 folder.
  Needs administrator rights when Studio lives under Program Files; the script re-launches itself elevated.

.PARAMETER StudioDir
  Studio install folder (contains Studio.exe).
.PARAMETER PluginDll
  Built plugin. Defaults to the Release build output next to this script.
#>
[CmdletBinding()]
param(
    [string]$StudioDir = "C:\Program Files\Studio 2.0 EarlyAccess",
    [string]$PluginDll = "",
    [string]$BepInExZipDir = "",
    # Account that runs Studio. BepInEx writes its config, cache and log under <StudioDir>\BepInEx,
    # so that account needs modify rights there when Studio lives under Program Files.
    [string]$GrantUser = "$env:USERDOMAIN\$env:USERNAME",
    # Transcript of the elevated run, so errors are visible from the non-elevated side.
    [string]$LogFile = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Temp\StudioSpaceMouse-install.log"),
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

# $PSScriptRoot is not available inside parameter defaults on Windows PowerShell 5.1.
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $PluginDll) { $PluginDll = Join-Path $scriptRoot "src\StudioSpaceMouse\bin\Release\net472\StudioSpaceMouse.dll" }
if (-not $BepInExZipDir) { $BepInExZipDir = Join-Path $scriptRoot "tools\BepInEx_win_x64_5.4.23.6" }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Path (Join-Path $StudioDir "Studio.exe"))) { throw "Studio.exe not found in '$StudioDir'." }
if (-not (Test-Path $PluginDll)) { throw "Plugin not found at '$PluginDll'. Build it first: dotnet build src\StudioSpaceMouse -c Release" }
if (-not (Test-Path (Join-Path $BepInExZipDir "winhttp.dll"))) { throw "BepInEx files not found in '$BepInExZipDir'." }

if (-not (Test-Admin) -and -not $NoElevate) {
    Write-Host "Elevating (UAC prompt)..."
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"",
              "-StudioDir", "`"$StudioDir`"", "-PluginDll", "`"$PluginDll`"", "-BepInExZipDir", "`"$BepInExZipDir`"",
              "-GrantUser", "`"$GrantUser`"", "-LogFile", "`"$LogFile`"", "-NoElevate")
    Remove-Item $LogFile -ErrorAction SilentlyContinue
    try {
        $p = Start-Process -FilePath "powershell.exe" -ArgumentList $args -Verb RunAs -Wait -PassThru
    } catch {
        Write-Host "Elevation failed or was declined: $($_.Exception.Message)"
        exit 2
    }
    if (Test-Path $LogFile) { Get-Content $LogFile | Where-Object { $_ -notmatch '^\*+$|^Transcript|^Windows PowerShell|^Start time|^End time|^Username|^RunAs|^Configuration|^Machine|^Host|^Process|^PSVersion|^Build|^CLR|^WSMan|^Serialization|^Remoting' } }
    exit $p.ExitCode
}

Start-Transcript -Path $LogFile -Force | Out-Null
trap { Write-Host "ERROR: $($_.Exception.Message)"; try { Stop-Transcript | Out-Null } catch {}; exit 1 }

# Copies a file unless the destination already has identical contents. A locked but identical
# file (Studio running with the loader already in place) is fine; a locked different one is not.
function Copy-IfChanged([string]$Source, [string]$DestDir) {
    $dest = Join-Path $DestDir (Split-Path -Leaf $Source)
    if ((Test-Path $dest) -and ((Get-FileHash $Source).Hash -eq (Get-FileHash $dest).Hash)) { return }
    try {
        Copy-Item -Force $Source $dest
    } catch {
        throw "Cannot replace '$dest' (is Studio running?): $($_.Exception.Message)"
    }
}

Write-Host "Installing BepInEx into $StudioDir"
$bepDir = Join-Path $StudioDir "BepInEx"
New-Item -ItemType Directory -Force (Join-Path $bepDir "core") | Out-Null
$pluginDir = Join-Path $bepDir "plugins\StudioSpaceMouse"
New-Item -ItemType Directory -Force $pluginDir | Out-Null

Write-Host "Granting $GrantUser modify rights on $bepDir (BepInEx writes config, cache and logs there)"
& icacls $bepDir /grant "$($GrantUser):(OI)(CI)M" /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Warning "icacls failed (exit $LASTEXITCODE); BepInEx may not be able to write its log and config." }

foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) {
    Copy-IfChanged (Join-Path $BepInExZipDir $f) $StudioDir
}
Get-ChildItem (Join-Path $BepInExZipDir "BepInEx\core") -File | ForEach-Object {
    Copy-IfChanged $_.FullName (Join-Path $bepDir "core")
}
Copy-IfChanged $PluginDll $pluginDir
# 3Dconnexion's managed navlib wrapper sits next to the plugin (built output copies it there).
$wrapper = Join-Path (Split-Path -Parent $PluginDll) "TDx.SpaceMouse.Navigation3D.dll"
if (Test-Path $wrapper) { Copy-IfChanged $wrapper $pluginDir } else { Write-Warning "TDx.SpaceMouse.Navigation3D.dll not found next to the plugin; driver integration will fall back to raw HID." }
Write-Host "Installed plugin to $pluginDir"

$running = Get-Process -Name Studio -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Studio is running; restart it to load the plugin."
}
Write-Host "Done. Log: $StudioDir\BepInEx\LogOutput.log  Config: $StudioDir\BepInEx\config\studiospacemouse.cfg"
Stop-Transcript | Out-Null
