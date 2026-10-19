param(
    [Parameter(Mandatory=$true)][string]$ManualFile,
    [Parameter(Mandatory=$true)][string]$AutoFile
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManualFile -PathType Leaf)) { throw "Manual file not found: $ManualFile" }
if (-not (Test-Path -LiteralPath $AutoFile -PathType Leaf)) { throw "Auto file not found: $AutoFile" }
function Show-Info([string]$File) {
    $item = Get-Item -LiteralPath $File
    $hash = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
    $stream = [System.IO.File]::OpenRead($File)
    $first = New-Object byte[] 4
    try { $read = $stream.Read($first, 0, 4) } finally { $stream.Dispose() }
    $signature = if ($read -eq 4) { [System.Text.Encoding]::ASCII.GetString($first) } else { '(short)' }
    [pscustomobject]@{
        File = $File
        Length = $item.Length
        Header = $signature
        SHA256 = $hash
    }
}
$manual = Show-Info $ManualFile
$auto = Show-Info $AutoFile
$manual, $auto | Format-List
if ($manual.SHA256 -eq $auto.SHA256) {
    Write-Host 'IDENTICAL: manual and automatic exports have exactly the same bytes.' -ForegroundColor Green
    Write-Host 'If CodeWalker still renders differently, check missing assets, load order, duplicate names and map project configuration.'
} else {
    Write-Warning 'DIFFERENT: exported files are not byte-identical.'
    Write-Host 'Note: different compressed resource files may still unpack to equivalent data.'
    Write-Host 'Test both files separately in CodeWalker and share both versions if the difference affects rendering.'
}
