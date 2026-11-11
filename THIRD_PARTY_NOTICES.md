# Third-party notices

StudioSpaceMouse itself is MIT licensed (see `LICENSE`). The repository and the release zip
also redistribute the following components, unmodified, under their own licences.

## BepInEx 5.4.23.6 (loader and core)

Files: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx/core/BepInEx.dll`,
`BepInEx.Preloader.dll`, `BepInEx.Harmony.dll`, `HarmonyXInterop.dll`.

Copyright (c) 2018 Bepis and BepInEx contributors. Licensed under the GNU Lesser General
Public License, version 2.1; the full text is in `licenses/LGPL-2.1.txt`. Source:
https://github.com/BepInEx/BepInEx. The doorstop loader (`winhttp.dll`) is UnityDoorstop,
https://github.com/NeighTools/UnityDoorstop, also LGPL 2.1.

## HarmonyX (`0Harmony.dll`, `0Harmony20.dll`)

Copyright (c) 2017 Andreas Pardeike (Harmony) and BepInEx contributors (HarmonyX). MIT
licence, text below. Source: https://github.com/BepInEx/HarmonyX.

## MonoMod (`MonoMod.RuntimeDetour.dll`, `MonoMod.Utils.dll`)

Copyright (c) 2019 0x0ade and MonoMod contributors. MIT licence, text below. Source:
https://github.com/MonoMod/MonoMod.

## Mono.Cecil (`Mono.Cecil.dll`, `Mono.Cecil.Mdb.dll`, `Mono.Cecil.Pdb.dll`, `Mono.Cecil.Rocks.dll`)

Copyright (c) 2008 - 2015 Jb Evain, Copyright (c) 2008 - 2011 Novell, Inc. MIT licence, text
below. Source: https://github.com/jbevain/cecil.

## 3Dconnexion 3DxWare SDK managed wrapper (`TDx.SpaceMouse.Navigation3D.dll`, `.xml`)

Redistributed in object code form under the 3Dconnexion Software Development Kit License
Agreement, whose text is in `tools/3Dconnexion/LicenseAgreementSDK.txt` (and
`LicenseAgreementSDK-3Dconnexion.txt` in the release zip). Required attribution:

3D input device development tools and related technology are provided under license from
3Dconnexion. (c) 3Dconnexion 1992 - 2021. All rights reserved.

The native navigation library (`TDxNavLib.dll`) is not redistributed; 3DxWare installs it.

## Not redistributed

BrickLink Studio's own assemblies are referenced at build time only and are never included.
Studio is a product of BrickLink; this project is not affiliated with or endorsed by BrickLink,
the LEGO Group or 3Dconnexion.

## MIT licence text (HarmonyX, MonoMod, Mono.Cecil)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
