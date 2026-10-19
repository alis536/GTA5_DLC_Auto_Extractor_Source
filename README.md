# GTA V DLC Auto Extractor (v0.1, source / unverified build)

A Windows desktop tool for **recursive, read-only GTA V RPF extraction**, built around `CodeWalker.Core`.

## What it does

- Browse a directory containing many physical `.rpf` files (such as many DLCs).
- Detect nested `.rpf` files inside each archive with CodeWalker's `ScanStructure`.
- Convert each archive name into a directory name: `mj_map_east/dlc.rpf/x64/.../props/part.rpf/road.ymap` becomes `<selected-output>/CustomMaps/mj_map_east/dlc/x64/.../props/part/road.ymap` when the selected source directory is named `CustomMaps`. The selected source folder name is deliberately preserved as the outermost output folder.
- Extract either supported map/model formats only (default), or **all** non-RPF files.
- Preserve normal RPF internal folders and each top-level DLC's path.
- **Extract Raw, not XML:** ordinary binary files use `ExtractFile`; resource files additionally use `ResourceBuilder.Compress` and `ResourceBuilder.AddResourceHeader` (as in CodeWalker Explorer's resource-compressed raw export).
- Write to separate output directory; never modify input files. Existing outputs are *skipped* by default (resume).
- Save separate timestamped `_extract_log_*.txt` and `_extract_manifest_*.csv` files.
- Cancel between entries (an in-progress CodeWalker archive scan may need to finish first).

## Requirements

- Windows 10 or Windows 11, x64.
- **.NET 8 SDK** and **Git for Windows** to build from source.
- Network connectivity on first build to clone [dexyfex/CodeWalker](https://github.com/dexyfex/CodeWalker) (pinned to `485d56bec00262ed7fa472261cce7bbc6202b96e`) and restore NuGet packages.
- For some encrypted archives, an installed and supported GTA V Legacy/Enhanced game folder with its original `GTA5.exe` or `GTA5_Enhanced.exe`. OPEN-encrypted archives, like those in the screenshot, normally don't need a game folder.
- Disk space for the extracted files. Some resource files are large and are processed in memory one at a time.

## Build (Windows PowerShell)

1. Extract this ZIP to a **short, writable path**, such as `D:\Dev\GTA5DlcExtractor`.
2. Open PowerShell in that folder.
3. Run: `powershell -ExecutionPolicy Bypass -File .\build-windows.ps1`.
4. The generated app will be at `publish\win-x64\GTA5DlcAutoExtractor.exe`.
5. Run it, select the source and a **separate, non-overlapping** output directory. For encrypted (non-OPEN) RPFs, select your real GTA V directory in the third field. Click **START EXTRACTION**.

To run only the path/filter tests (does not need CodeWalker):
`dotnet run --project .\PathTests\PathTests.csproj -c Release`

## Technical notes

The application directly references upstream `CodeWalker.Core`, whose source is downloaded on first build. **No CodeWalker code, GTA game files, encryption keys, or game assets are packaged in this ZIP.** Check upstream licensing, notices and dependencies before redistribution. See [CodeWalker](https://github.com/dexyfex/CodeWalker) and its `Notice.txt`.

**Limitations in v0.1:**
- This is an implementation-stage source release, **not a built, field-tested EXE**. Runtime behavior against the user's actual DLCs has not been verified. Build compatibility with the latest CodeWalker master also needs Windows verification.
- Archive paths are normalized to lowercase by the upstream parser. Windows filenames are case insensitive, so this is generally fine.
- No reverse packaging / repacking into DLCs; no YMAP XML conversion.
- Unknown / damaged / encrypted formats may fail and will be logged. GTA encryption support depends on upstream CodeWalker and available valid game files.
- Source and destination directories cannot overlap; that avoids recursing into output and accidental writes into the source.
- Cancellation is checked before each archive and file; the upstream `ScanStructure` itself is not cancellable.
- In all-files mode, nested archives are traversed as containers, **not duplicated** as `.rpf` output leaves. No guaranteed compatibility for every file type.

### CodeWalker API references

- `CodeWalker.Core/GameFiles/RpfFile.cs`: `ScanStructure`, `Children`, `ExtractFile`.
- `CodeWalker.Core/GameFiles/Resources/ResourceBuilder.cs`: `Compress`, `AddResourceHeader`.
- `CodeWalker.Core/GameFiles/Utils/GTAKeys.cs`: optional loading from a GTA V directory.
- `CodeWalker/Tools/ExtractRawForm.cs`: example raw extraction workflow.

## Before using it on a major map

Try a **copy of one small DLC** first, extract it, then open several `.ymap`, `.ydr`, `.ytd`, `.ytyp` files in CodeWalker and compare against the originals. Inspect `_extract_manifest_*.csv` for failures before a full batch.
