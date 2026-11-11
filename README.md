# StudioSpaceMouse

3Dconnexion SpaceMouse support for BrickLink Studio 2.0, injected as a BepInEx plugin.

Studio is a Unity Mono build with no SpaceMouse code of its own. This plugin drives Studio's
own orbit camera (`OrbitControl`) the same way the mouse handlers do, so the orientation
cube, the rotation lock, per-viewport preferences and zoom-to-fit keep working. It has two
input backends:

- **Driver (navlib)**, the default when 3DxWare is running. Studio registers with the driver
  as "BrickLink Studio"; speed, axis inversion, navigation mode, auto pivot and button
  assignments are all configured in the 3Dconnexion settings app, and the driver does the
  camera maths. A command set (fit, fit selection, the six views, free camera, rotation lock)
  is exported so those actions can be assigned to buttons there.
- **Raw HID**, used when the driver is not available (or forced with `Backend = RawHid`).
  The plugin reads the device through the Windows Raw Input API and the `[Sensitivity]`,
  `[Axes]` and `[Buttons]` config sections apply.

3D input device development tools and related technology are provided under license from
3Dconnexion. (c) 3Dconnexion 1992 - 2021. All rights reserved. The managed wrapper
`TDx.SpaceMouse.Navigation3D.dll` is redistributed under the SDK licence in `tools/3Dconnexion/`.

Tested against Studio 2.26.10_1 (Unity 2022.3.62, Early Access) with a SpaceMouse
Enterprise and 3DxWare 10 running. 3DxWare does not need to be running; the plugin
reads the HID device directly.

## Layout

| Path | What |
| --- | --- |
| `src/StudioSpaceMouse/` | The plugin (net472 class library). |
| `src/StudioSpaceMouse/RawInputReader.cs` | Hidden window plus message loop on a background thread, registered for HID multi-axis controllers. |
| `src/StudioSpaceMouse/HidReportParser.cs` | Decodes report ids 1, 2 and 3 (translation, rotation, buttons). |
| `src/StudioSpaceMouse/CameraDriver.cs` | Talks to Studio's `OrbitControl` and `Viewport`; turns a driver camera pose into orbit angles. |
| `src/StudioSpaceMouse/NavlibBridge.cs` | Answers the driver's questions about the scene (camera, extents, pivot, hit test) and applies its camera. |
| `tests/StudioSpaceMouse.NavlibHarness/` | Console check that 3DxWare accepts a navlib connection outside Studio. |
| `src/StudioSpaceMouse/Plugin.cs` | BepInEx entry point, config, per-frame update. |
| `tests/` | xunit tests for the pure parts (parser, axis shaping, bindings). |
| `tools/` | BepInEx 5.4.23.6 x64 release files used by the installer and as build references. |
| `install.ps1` / `uninstall.ps1` | Copy BepInEx and the plugin into the Studio folder (self-elevating). |

## Build

Needs the .NET 8 SDK. The plugin references Studio's own assemblies from the install folder;
pass `-p:StudioDir="..."` if Studio lives somewhere other than the default.

```powershell
dotnet build src\StudioSpaceMouse -c Release
dotnet test tests\StudioSpaceMouse.Tests
```

## Install

```powershell
.\install.ps1
```

This copies `winhttp.dll`, the doorstop config and `BepInEx\core` next to `Studio.exe`, and the
plugin to `BepInEx\plugins\StudioSpaceMouse\`. It also grants your account modify rights on the
`BepInEx` folder, because BepInEx writes its config, cache and log there and Program Files is
read-only for normal users; without that grant the loader starts and then silently gives up.
Files that Studio holds open (`winhttp.dll` while it is running) are skipped when they are already
identical. Restart Studio afterwards. BepInEx writes `BepInEx\LogOutput.log`; the plugin logs the
device it found there.

`uninstall.ps1` removes everything again (`-KeepBepInEx` leaves the loader in place).

### Stop 3DxWare from emulating the mouse wheel

If 3DxWare is installed, it treats Studio as an application without SpaceMouse support and
applies its default keyboard/mouse profile, which maps the tilt axis to the mouse wheel. Studio
then zooms 10% per wheel tick on top of the plugin's own motion, which feels like stutter
whenever you tilt. Run this once per user:

```powershell
.\tools\disable-3dxware-emulation.ps1
```

It writes `%APPDATA%\3Dconnexion\3DxWare\Cfg\Studio-KMJ.xml` with all six axes disabled
(buttons stay untouched; the plugin reads them directly). The same thing can be done in the
3Dconnexion settings app with Studio in focus. As a safety net the plugin also drops wheel
events while the cap is deflected (`SuppressWheelWhileActive`).

## Configure

After the first run Studio writes `BepInEx\config\studiospacemouse.cfg`. It is read at startup,
so edit it with Studio closed and restart to apply.

Axis directions differ between models and between people's preferences, so calibrate once:

1. Set `VerboseLog = true`, start Studio, push the cap in one direction and read the raw
   `T=(x,y,z) R=(x,y,z)` values in the log to see which axis moved and its sign.
2. Set the `[Axes]` entries. Each takes an axis name (`Tx Ty Tz Rx Ry Rz`) with an optional
   leading `-` to flip it. Defaults, checked against a SpaceMouse Enterprise: pan `-Tx`/`Tz`,
   zoom `-Ty`, yaw `Rz`, pitch `Rx`.
3. `NavigationMode = Object` moves the model with the cap; `Camera` moves the camera.
4. Button numbers are HID bit positions and also appear in the log when pressed. Defaults
   follow the common 3Dconnexion numbering (Fit 1, Top 2, Left 3, Right 4, Front 5,
   Bottom 6, Back 7, ISO1 10 as Free, ISO2 11 as fit selection, Rotate 26 toggles the
   viewport rotation lock). `-1` disables a mapping. Holding a view button for
   `LongPressSeconds` gives the opposite view, as 3DxWare's secondary function does.
5. Turn `VerboseLog` back off.

Sensitivity lives under `[Sensitivity]`: orbit degrees per second, pan and zoom speed,
dead zone, full-scale raw value, response curve exponent, a low-pass smoothing time
constant and a dominant-axis switch.

## How it hooks in

Studio destroys BepInEx's plugin object shortly after startup, so the plugin's own Unity
`Update` never runs. The per-frame work therefore hangs off a Harmony postfix on
`OrbitControl.LateUpdate`, which Studio calls for every live viewport camera. The raw-input
reader is static and stops on application quit, not on component destroy. The destroyed
component still serves as the config and log holder; nothing on it touches Unity after startup.

## Scope and known limits

- Build-mode viewports (managed by `ViewportsManager`) and the instruction maker's step
  editor (`Ins_StepEditor_Viewport`) are driven; the step editor wins while it is open. The
  page-design previews and the render window have their own cameras and are not wired up.
- Input is ignored while Studio is not the foreground window or while a text field has focus.
- A Studio update that renames `OrbitControl.f0Dir` or `setTransform` disables camera
  control; the plugin logs this at startup instead of crashing.
- Roll (tilting the cap sideways) has no equivalent in Studio's orbit camera and is unused.
