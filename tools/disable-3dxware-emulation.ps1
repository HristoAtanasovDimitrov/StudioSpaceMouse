<#
.SYNOPSIS
  Stops 3DxWare's keyboard/mouse emulation for Studio.exe.

  Without an SDK integration, 3DxWare applies its default "KMJ" profile to Studio, which maps
  the tilt axis to the mouse wheel. Studio then zooms by 10% per wheel tick on top of what the
  plugin does, which shows up as stutter when tilting. This writes a per-user profile for
  Studio with all six axes disabled. Buttons are untouched; the plugin reads them itself.

  No elevation needed: the profile lives under %APPDATA%\3Dconnexion\3DxWare\Cfg.
#>
param(
    [string]$ExecutableName = "Studio.exe",
    [string]$AppName = "Studio",
    [string]$CfgDir = (Join-Path $env:APPDATA "3Dconnexion\3DxWare\Cfg")
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $CfgDir)) { throw "3DxWare user config folder not found at '$CfgDir'. Is 3DxWare installed?" }
$path = Join-Path $CfgDir "$AppName-KMJ.xml"

if (Test-Path $path) {
    [xml]$x = Get-Content $path -Raw
    Copy-Item -Force $path "$path.bak"
    Write-Host "Updating existing profile $path (backup at $path.bak)"
} else {
    [xml]$x = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<AppCfg xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" Default="false" CfgFormatVersion="1.3" ThisFileVersion="2.0">
  <AppInfo>
    <Signature>
      <Name>$AppName</Name>
      <ExecutableName>$ExecutableName</ExecutableName>
    </Signature>
    <Options />
  </AppInfo>
  <CfgProperties>
    <ID>ID_$AppName</ID>
    <Name>$AppName</Name>
    <InheritsFromID>ID_Default_KMJ_Cfg</InheritsFromID>
  </CfgProperties>
</AppCfg>
"@
    Write-Host "Creating profile $path"
}

$root = $x.DocumentElement
$devices = $root.SelectSingleNode("Devices")
if (-not $devices) { $devices = $x.CreateElement("Devices"); $root.AppendChild($devices) | Out-Null }

# Replace any existing entry for the generic device so the script is idempotent.
foreach ($old in @($devices.SelectNodes("Device[ID='ID_Standard_3D_Mouse']"))) { $devices.RemoveChild($old) | Out-Null }

$dev = $x.CreateElement("Device")
$dev.AppendChild($x.CreateElement("ID")).InnerText = "ID_Standard_3D_Mouse"
$dev.AppendChild($x.CreateElement("CurrentAxisBank")).InnerText = "Default"
$bank = $x.CreateElement("AxisBank"); $bank.SetAttribute("Default", "true")
$bank.AppendChild($x.CreateElement("ID")).InnerText = "Default"
$bank.AppendChild($x.CreateElement("Name")).InnerText = "STR_DEFAULT_BANK"
foreach ($a in @("X", "Y", "Z", "Rx", "Ry", "Rz")) {
    $axis = $x.CreateElement("Axis")
    $axis.AppendChild($x.CreateElement("Enabled")).InnerText = "false"
    $in = $x.CreateElement("Input")
    $in.AppendChild($x.CreateElement("ActionID")).InnerText = "HIDMultiAxis_$a"
    $in.AppendChild($x.CreateElement("Min")).InnerText = "-512"
    $in.AppendChild($x.CreateElement("Max")).InnerText = "511"
    $axis.AppendChild($in) | Out-Null
    $out = $x.CreateElement("Output")
    $out.AppendChild($x.CreateElement("ActionID")).InnerText = "HIDMultiAxis_$a"
    $axis.AppendChild($out) | Out-Null
    $bank.AppendChild($axis) | Out-Null
}
$dev.AppendChild($bank) | Out-Null
$devices.AppendChild($dev) | Out-Null

$ws = New-Object System.Xml.XmlWriterSettings
$ws.Indent = $true; $ws.IndentChars = "  "; $ws.Encoding = New-Object System.Text.UTF8Encoding($false)
$w = [System.Xml.XmlWriter]::Create($path, $ws); $x.Save($w); $w.Close()
Write-Host "Done. 3DxWare applies the profile the next time $ExecutableName gets focus; if not, restart the 3DxWare service or sign out and in."
