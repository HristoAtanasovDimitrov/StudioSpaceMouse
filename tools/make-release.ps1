<#
.SYNOPSIS
  Builds the plugin and packs an installable release zip into dist\.

  The zip contains the BepInEx loader files, the plugin with its 3Dconnexion wrapper, the
  install/uninstall scripts, the emulation-disabling script and the README, laid out so that
  install.ps1 works straight from the extracted folder.

  Must run on a machine with Studio installed: the plugin compiles against Studio's own
  assemblies, which are not redistributed.
#>
param(
    [string]$StudioDir = "C:\Program Files\Studio 2.0 EarlyAccess",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $root "src\StudioSpaceMouse\StudioSpaceMouse.csproj"

[xml]$csproj = Get-Content $proj
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> in $proj" }

Write-Host "Building $Configuration v$version"
dotnet build $proj -c $Configuration --nologo -v q -p:StudioDir="$StudioDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out = Join-Path $root "src\StudioSpaceMouse\bin\$Configuration\net472"
$bep = Join-Path $root "tools\BepInEx_win_x64_5.4.23.6"
$stage = Join-Path $root "dist\stage\StudioSpaceMouse"
$zip = Join-Path $root "dist\StudioSpaceMouse-v$version.zip"

if (Test-Path (Join-Path $root "dist\stage")) { Remove-Item -Recurse -Force (Join-Path $root "dist\stage") }
New-Item -ItemType Directory -Force "$stage\BepInEx\core", "$stage\BepInEx\plugins\StudioSpaceMouse" | Out-Null

Copy-Item (Join-Path $bep "winhttp.dll"), (Join-Path $bep "doorstop_config.ini"), (Join-Path $bep ".doorstop_version") $stage
Copy-Item (Join-Path $bep "BepInEx\core\*") "$stage\BepInEx\core"
Copy-Item (Join-Path $out "StudioSpaceMouse.dll"), (Join-Path $out "TDx.SpaceMouse.Navigation3D.dll") "$stage\BepInEx\plugins\StudioSpaceMouse"
Copy-Item (Join-Path $root "install.ps1"), (Join-Path $root "uninstall.ps1"), (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "tools\disable-3dxware-emulation.ps1") $stage
Copy-Item (Join-Path $root "tools\3Dconnexion\LicenseAgreementSDK.txt") "$stage\LicenseAgreementSDK-3Dconnexion.txt"

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item -Recurse -Force (Join-Path $root "dist\stage")
Write-Host "Wrote $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
