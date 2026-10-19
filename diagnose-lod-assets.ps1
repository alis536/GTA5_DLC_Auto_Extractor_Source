param(
    [Parameter(Mandatory = $true)][string]$OutputFolder,
    [string]$Manifest = "",
    [int]$Top = 30
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $OutputFolder).Path
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Output directory does not exist: $OutputFolder"
}
if ([string]::IsNullOrWhiteSpace($Manifest)) {
    $latest = Get-ChildItem -LiteralPath $root -Recurse -File -Filter '_extract_manifest_*.csv' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw "No _extract_manifest_*.csv found. Pass -Manifest explicitly." }
    $Manifest = $latest.FullName
}
$manifestRows = @(Import-Csv -LiteralPath $Manifest)
$okRows = @($manifestRows | Where-Object { $_.status -eq 'OK' })
$report = New-Object 'System.Collections.Generic.List[object]'
function Add-Finding([string]$Severity, [string]$Code, [string]$File, [string]$Related, [string]$Detail) {
    $report.Add([pscustomobject]@{
        Severity=$Severity; Code=$Code; File=$File; Related=$Related; Detail=$Detail
    })
}
$tracked = @('.ytd','.ydr','.ydd','.ymap','.ytyp','.ybn','.ymf','.ymt','.meta')
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $tracked -contains $_.Extension.ToLowerInvariant() })
$ytd = @($files | Where-Object { $_.Extension -ieq '.ytd' })
$models = @($files | Where-Object { $_.Extension -ieq '.ydr' -or $_.Extension -ieq '.ydd' })
$byStem = @{}
foreach ($tex in $ytd) {
    $key = $tex.BaseName.ToLowerInvariant()
    if (-not $byStem.ContainsKey($key)) { $byStem[$key] = New-Object 'System.Collections.Generic.List[object]' }
    $byStem[$key].Add($tex)
}
$duplicateNames = 0
$conflictingNames = 0
foreach ($group in ($ytd | Group-Object { $_.Name.ToLowerInvariant() })) {
    if ($group.Count -lt 2) { continue }
    $duplicateNames++
    $hashes = @($group.Group | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Sort-Object -Unique)
    if ($hashes.Count -gt 1) {
        $conflictingNames++
        Add-Finding 'CHECK' 'DUPLICATE_YTD_DIFFERENT_BYTES' $group.Group[0].FullName ($group.Group[-1].FullName) "Same texture dictionary filename appears in $($group.Count) locations with different contents. Resolution/load order matters."
    } else {
        Add-Finding 'INFO' 'DUPLICATE_YTD_IDENTICAL' $group.Group[0].FullName ($group.Group[-1].FullName) "Same dictionary name in $($group.Count) locations with identical bytes."
    }
}
# A same-stem YTD is NOT a guaranteed requirement: shaders often use shared dictionaries
# specified by YTYP/GTXD relationships. Treat these as hints, never as proof of damage.
$possibleMissing = 0
foreach ($model in $models) {
    $name = $model.BaseName.ToLowerInvariant()
    if ($name -notmatch '(^|_)(slod|lod|hd|road|terrain|ground|land|grass)') { continue }
    if ($byStem.ContainsKey($name)) { continue }
    $possibleMissing++
    Add-Finding 'HINT' 'NO_SAME_STEM_YTD' $model.FullName '' 'No YTD with identical base filename was found. This is common for shared TXDs; inspect YTYP/GTXD and manual load order.'
}
# Non-RSC headers may be supported alternate resource layouts; never label as corrupted.
foreach ($tex in $ytd) {
    if ($tex.Length -lt 4) {
        Add-Finding 'CHECK' 'YTD_TOO_SHORT' $tex.FullName '' "Texture dictionary is shorter than 4 bytes."
        continue
    }
    $stream = [System.IO.File]::Open($tex.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $hdr = New-Object byte[] 4
        [void]$stream.Read($hdr,0,4)
    } finally { $stream.Dispose() }
    $magic = [System.Text.Encoding]::ASCII.GetString($hdr)
    if ($magic -ne 'RSC7' -and $magic -ne 'RSC8') {
        Add-Finding 'CHECK' 'YTD_UNEXPECTED_HEADER' $tex.FullName '' ("Header 0x" + [BitConverter]::ToString($hdr).Replace('-','') + "; compare with a manual CodeWalker export.")
    }
}
foreach ($item in $manifestRows) {
    if ($item.status -eq 'ERROR') {
        Add-Finding 'ERROR' 'EXTRACTION_ERROR' $item.archive_entry $item.output_file $item.message
    } elseif ($item.status -eq 'WARNING' -and $item.method -eq 'FALLBACK') {
        Add-Finding 'CHECK' 'RESOURCE_FALLBACK' $item.archive_entry $item.output_file $item.message
    } elseif ($item.status -eq 'SKIP') {
        Add-Finding 'CHECK' 'SKIPPED_RESOURCE' $item.archive_entry $item.output_file $item.message
    } elseif ($item.status -eq 'OK' -and -not (Test-Path -LiteralPath $item.output_file -PathType Leaf)) {
        Add-Finding 'ERROR' 'OUTPUT_MISSING' $item.archive_entry $item.output_file 'Manifest says OK but file is missing from output.'
    }
}
# Different original archive entries may normalize to the same Windows output file.
foreach ($group in ($okRows | Group-Object { $_.output_file.ToLowerInvariant() })) {
    if ($group.Count -gt 1) {
        $sources = @($group.Group | Select-Object -ExpandProperty archive_entry -Unique)
        if ($sources.Count -gt 1) {
            Add-Finding 'CHECK' 'OUTPUT_PATH_COLLISION' $group.Name ($sources -join '; ') 'Distinct RPF entries have the same output path.'
        }
    }
}
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$csv = Join-Path $root ("_lod_asset_audit_" + $stamp + ".csv")
$report | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Write-Host "Manifest: $Manifest"
Write-Host ("Files: {0} | YTD: {1} | YDR/YDD: {2} | Duplicate YTD names: {3} | Different-content duplicates: {4}" -f $files.Count,$ytd.Count,$models.Count,$duplicateNames,$conflictingNames)
Write-Host ("LOD-related models without same-name YTD (HEURISTIC ONLY): {0}" -f $possibleMissing)
Write-Host ("Report: {0}" -f $csv) -ForegroundColor Cyan
$report | Where-Object { $_.Severity -in @('ERROR','CHECK') } | Select-Object -First $Top |
    Format-Table -AutoSize Severity,Code,File,Related
Write-Host 'Reminder: byte-identical extracted files can still render without textures if CodeWalker does not load their dependent dictionaries or GTXD/manifest relationships.' -ForegroundColor Yellow
