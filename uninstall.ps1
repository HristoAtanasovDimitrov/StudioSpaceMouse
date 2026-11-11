<#
.SYNOPSIS
  Removes BepInEx and the StudioSpaceMouse plugin from a BrickLink Studio 2.0 folder.
  Re-launches itself elevated when needed.
#>
[CmdletBinding()]
param(
    [string]$StudioDir = "C:\Program Files\Studio 2.0 EarlyAccess",
    [switch]$KeepBepInEx,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin) -and -not $NoElevate) {
    Write-Host "Elevating (UAC prompt)..."
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-StudioDir", "`"$StudioDir`"", "-NoElevate")
    if ($KeepBepInEx) { $args += "-KeepBepInEx" }
    $p = Start-Process -FilePath "powershell.exe" -ArgumentList $args -Verb RunAs -Wait -PassThru
    exit $p.ExitCode
}

$pluginDir = Join-Path $StudioDir "BepInEx\plugins\StudioSpaceMouse"
if (Test-Path $pluginDir) { Remove-Item -Recurse -Force $pluginDir; Write-Host "Removed $pluginDir" }

if (-not $KeepBepInEx) {
    foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx")) {
        $p = Join-Path $StudioDir $f
        if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "Removed $p" }
    }
}
Write-Host "Done."
