# HDGraph (rewrite)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![CI](https://github.com/Phoenix-/HDGraph/actions/workflows/ci.yml/badge.svg)](https://github.com/Phoenix-/HDGraph/actions/workflows/ci.yml)

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

## CI

Every push runs [`.github/workflows/ci.yml`](.github/workflows/ci.yml) on a GitHub-hosted Windows runner: Release
build, tests, then the Native AOT publish. The resulting `hdgraph.exe` is attached to the run as the artifact
`hdgraph-win-x64` (Actions tab, open the run, "Artifacts"; kept for 14 days).

Runs are serialised per branch. A run in progress is never interrupted, at most one run waits behind it, and a newer
push replaces the waiting one, so a burst of commits costs one extra build that covers all of them. Pushes that touch
only Markdown, `LICENSE` or the git dot-files are skipped. There is no scheduled build; the workflow can also be started
by hand from the Actions tab.

## Versions and releases

The version comes from git tags through [MinVer](https://github.com/adamralph/minver); nothing in the tree is edited
to bump it. A tag `vX.Y.Z` makes that commit build as `X.Y.Z`. Every later commit builds as `X.Y.(Z+1)-alpha.0.N`,
N being the number of commits since the tag, so a CI artifact always says how far past the last release it is. The
window title shows this version; the exe properties also carry the commit hash. Semver as usual: MAJOR for breaking
changes, MINOR for features, PATCH for fixes; before 1.0 the rules are relaxed.

To cut a release, tag and push the tag:

```
git tag -a v0.2.0 -m "HDGraph 0.2.0"
git push origin v0.2.0
```

[`.github/workflows/release.yml`](.github/workflows/release.yml) builds that commit, runs the tests, publishes the AOT
exe, checks that the exe reports the tag's version, and creates a **draft** GitHub Release with `hdgraph-win-x64.exe`,
its SHA-256, `hdgraph-win-x64-symbols.zip` and generated notes. Read the notes, edit if needed, press Publish. From
then on <https://github.com/Phoenix-/HDGraph/releases/latest/download/hdgraph-win-x64.exe> points at that build. A tag
with a pre-release suffix (`v0.2.0-beta.1`) is marked pre-release and never becomes `latest`.

The symbols zip is the `hdgraph.pdb` of that exact build. Users never need it: to read a crash dump later, take the
version from the window title (or the exe properties), fetch the zip from that release, and the dump resolves.

## Using it

- Left-click a sector to make that folder the centre; click the centre disc (or **Up**) to go back up.
- Hover a sector for size, share and file counts in the right pane.
- Right-click a sector: open in Explorer, copy path, rescan from there.
- Rings and rotation sliders change the view without rescanning.

## Conventions

- `Directory.Build.props` turns warnings into errors for every project.
- Anything heavy runs off the UI thread; `FileSystemScanner.ScanAsync` already does that itself.
- Angles are degrees, 0 at 12 o'clock, clockwise (see `SunburstLayout`).
