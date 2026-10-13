# AreaShot

English | [日本語](README.md)

AreaShot is a lightweight screenshot tool for Windows 11. It stays in the notification area (system tray); press a shortcut (default `Ctrl+Alt+S`) to select an area of the screen, then quickly copy it to the clipboard, save it to a file, or annotate it in a simple editor.

This repository contains the source code of the publicly released AreaShot.

## Features

- Area capture from a shortcut or the tray menu (supports multiple monitors and monitors with different scaling)
- After capture, a dialog to copy to the clipboard / save to the save folder / save as / open in the image editor / capture again
- Hold a modifier key while finishing the selection to copy, save or open the editor without the dialog (key assignments can be changed in Settings)
- Simple editor: lines, rectangles, arrows, text and rectangular mosaic, with undo / redo and non-destructive editing (the original image and annotations are kept separately and combined on output)
- PNG and JPEG output (JPEG quality is adjustable); file names come from a date-time template (e.g. `{yy}{MM}{dd}{HH}{mm}{ss}_capture`), sequential numbers (`001.png`), or sequential numbers with a description entered when saving (`001_Open settings.png`), which keeps images for manuals and guides in the order they were taken
- Optional start at Windows sign-in
- English and Japanese UI (follows the Windows display language, or can be fixed in Settings)

Features outside its scope (full-screen, window or scrolling capture, OCR, uploading, etc.) are not provided. Images and settings stay on your PC; AreaShot never sends them over the internet (see the privacy policy below).

## Supported OS and runtime

- Windows 11 (64-bit, x64)
- The Microsoft Store version includes .NET, so nothing else needs to be installed.
- To build and run from the source in this repository, the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) is required.

## Installation

The official distribution channel is the Microsoft Store. Please get AreaShot there (free).

- Microsoft Store: https://apps.microsoft.com/detail/9PBS7QBWJDD2

This GitHub repository does not distribute executables (binaries).

## Basic usage

1. When started, AreaShot stays in the notification area (no window is shown).
2. Press `Ctrl+Alt+S` (or right-click the tray icon → "Start capture") to start, and drag to select an area. Press `Esc` to cancel.
3. Choose copy, save, editor, etc. in the dialog that appears.
4. The shortcut, save folder, file name, image format, display language and more can be changed from "Settings..." in the tray menu.
5. To quit, choose "Exit" in the tray menu.

See the user guide for details.

- User guide: https://www.iwanaga.net/areashot/en/manual/

## Building

### Requirements

- Windows 11 x64
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (verified: 8.0.425)
- Only the `dotnet` CLI is needed (Visual Studio is not required).
- The app itself has no NuGet package dependencies. Only the tests use the NuGet packages `Microsoft.NET.Test.Sdk`, `xunit` and `xunit.runner.visualstudio` (restored from nuget.org on the first build).

### Commands

Run from the repository root.

```powershell
dotnet build AreaShot.csproj
dotnet test Tests/AreaShot.Tests.csproj
dotnet run --project AreaShot.csproj
```

`dotnet run` starts AreaShot in the notification area. To quit, choose "Exit" in the tray menu.

To create the distributable folder (framework-dependent, win-x64), run the following. The output goes to `publish\FrameworkDependent\` and requires the .NET 8 Desktop Runtime (x64) to run.

```powershell
dotnet publish AreaShot.csproj -c Release -p:PublishProfile=FrameworkDependent
```

The output also includes `LICENSE` and `THIRD-PARTY-NOTICES.md` (v1.2.0 and later; shown from "MIT License" and "Third-party notices" in the app's Settings window).

### Layout

| Path | Contents |
|---|---|
| `AreaShot.csproj` | The app (WPF, `net8.0-windows`). Only the tray icon uses WinForms `NotifyIcon` |
| `Core/` | Logic independent of UI and Win32, and UI strings (English `Strings.resx`, Japanese `Strings.ja.resx`) |
| `Imaging/` | Annotation drawing, image composition and encoding |
| `Tests/` | Unit tests for `Core` and `Imaging` (xUnit) |
| `Capture/`, `Editor/`, `Services/`, `Views/`, `Interop/` | Area selection, editor, tray/shortcut services, windows, Win32 API declarations |
| `icon.png`, `Resources/icon.ico` | App icon (`pwsh -File tools/make-icon.ps1` creates `icon.ico` from `icon.png`) |

The tooling that builds the Microsoft Store package (MSIX) is not included in this repository. The `AreaShotMsix` condition in `AreaShot.csproj` and `Services/AutoStartService.Packaged.cs` are for the Store version.

Source comments are mostly written in Japanese. References such as "仕様書 §N" in comments point to sections of the internal development specification, which is not included in this repository.

## License

Released under the [MIT License](LICENSE).

Copyright (c) 2026 ja1bus (© ja1bus)

For third-party software, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## No warranty

This software is provided "as is", without warranty of any kind, express or implied. In no event shall the author be liable for any damages arising from the use of this software (see [LICENSE](LICENSE) for details).

## Links

- Web page: https://www.iwanaga.net/areashot/en/
- User guide: https://www.iwanaga.net/areashot/en/manual/
- Changelog: https://www.iwanaga.net/areashot/en/changelog/
- Privacy policy: https://www.iwanaga.net/areashot/en/privacy/
- Microsoft Store: https://apps.microsoft.com/detail/9PBS7QBWJDD2
- Contact: support@iwanaga.net
