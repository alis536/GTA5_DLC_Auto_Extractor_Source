param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is not installed. Install Git for Windows.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0' }
if (-not (Test-Path (Join-Path $PSScriptRoot 'codewalker-upstream/CodeWalker.Core/CodeWalker.Core.csproj'))) {
    Write-Host 'Downloading CodeWalker source...' -ForegroundColor Cyan
    git clone --depth 1 https://github.com/dexyfex/CodeWalker.git codewalker-upstream
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone CodeWalker.' }
    # Reproducible upstream revision whose APIs this prototype targets.
    $upstreamCommit = '485d56bec00262ed7fa472261cce7bbc6202b96e'
    git -C codewalker-upstream fetch --depth 1 origin $upstreamCommit
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch pinned CodeWalker commit.' }
    git -C codewalker-upstream checkout --detach $upstreamCommit
    if ($LASTEXITCODE -ne 0) { throw 'Could not checkout pinned CodeWalker commit.' }
}
if (-not $SkipTests) {
    Write-Host 'Running path-safety tests...' -ForegroundColor Cyan
    dotnet run --project .\PathTests\PathTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Host 'Building Windows x64 app...' -ForegroundColor Cyan
dotnet publish .\DlcAutoExtractor\DlcAutoExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\publish\win-x64
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed; review the errors above.' }
Write-Host 'App: publish\win-x64\GTA5DlcAutoExtractor.exe' -ForegroundColor Green
