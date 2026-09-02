# HDGraph (rewrite)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A fresh, DPI-aware take on [HDGraph](https://www.hdgraph.com): where did the disk space go, drawn as
concentric rings. Windows-first, built on .NET 10 and Avalonia with the FluentAvalonia theme, so it looks
like a Windows 11 app (Mica backdrop included) and renders crisply at any scaling.

The original 2006–2015 sources (CeCILL v2) are kept on the orphan branch `legacy/hdgraph-1.5.1-svn-r383`
for reference only; nothing is reused. This rewrite is MIT-licensed, see [LICENSE](LICENSE).

## Layout

| Project | What it is |
|---|---|
| `src/HDGraph.Core` | Scanner and tree model. No UI dependencies. One directory enumeration per folder, parallel near the root, throttled progress, cancellation. |
| `src/HDGraph.Geometry` | Sunburst maths: sectors (angles, radii) for a tree, and the hit test. Pure, testable. |
| `src/HDGraph.App` | Avalonia shell: `SunburstControl` (custom-drawn rings, labels, pointer handling), MVVM with CommunityToolkit, Mica window. |
| `tests/HDGraph.Tests` | xUnit tests for Core and Geometry, run against a temp directory tree. |

## Build and run

```
dotnet build HDGraphCore.slnx
dotnet test HDGraphCore.slnx
dotnet run --project src/HDGraph.App -- "C:\Program Files"
```

Passing a path scans it on start; without one, pick a folder or click a drive button.

## Publishing

Two profiles live in `src/HDGraph.App/Properties/PublishProfiles`.

```
dotnet publish src/HDGraph.App -p:PublishProfile=aot-win-x64
```

Native AOT: one self-contained `hdgraph.exe` (about 39 MB) in `src/HDGraph.App/bin/publish/aot-win-x64`, no .NET
runtime and no DLLs next to it. Skia, HarfBuzz and ANGLE are linked in statically from the community packages
`CoreUtils.SkiaSharp.Static` / `CoreUtils.ANGLE.Static`. The `hdgraph.pdb` beside the exe is for symbolicating
crash stacks; it is not needed to run. Requires the Visual Studio "Desktop development with C++" workload, because
the AOT compiler links with `link.exe`. Takes under a minute on a desktop CPU.

```
dotnet publish src/HDGraph.App -p:PublishProfile=singlefile-win-x64
```

Fallback for machines without the C++ toolchain: the JIT runtime bundled into a self-extracting single exe. Bigger
and slower to start, otherwise the same program.

Things to know:

- Every publish *property* sits in the `.pubxml`. A `PropertyGroup` in the csproj conditioned on `PublishAot` never
  fires, because the profile is imported after the project body; items and targets with that condition do work.
- With `NoDefaultCurrentDirectoryInExePath` set in the environment, Visual Studio 18's `VsDevCmd.bat` prints
  `'vswhere.exe' is not recognized` during the publish. Harmless: the csproj target `HdgFixLinkerPathAfterVcVarsNoise`
  repairs the linker path that this message would otherwise corrupt.
- After an Avalonia upgrade, check that the SkiaSharp version it pulls in still matches `CoreUtils.SkiaSharp.Static`
  (3.119.x today) and re-check the short `NoWarn` list in the AOT profile.

## Using it

- Left-click a sector to make that folder the centre; click the centre disc (or **Up**) to go back up.
- Hover a sector for size, share and file counts in the right pane.
- Right-click a sector: open in Explorer, copy path, rescan from there.
- Rings and rotation sliders change the view without rescanning.

## Conventions

- `Directory.Build.props` turns warnings into errors for every project.
- Anything heavy runs off the UI thread; `FileSystemScanner.ScanAsync` already does that itself.
- Angles are degrees, 0 at 12 o'clock, clockwise (see `SunburstLayout`).
