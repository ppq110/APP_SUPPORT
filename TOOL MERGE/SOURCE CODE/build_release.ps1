param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$sourceRoot = $PSScriptRoot
$project = Join-Path $sourceRoot "GopCadApp/GopCadApp.csproj"
$releaseRoot = Join-Path (Split-Path $sourceRoot -Parent) "RELEASE"
$staging = Join-Path $sourceRoot "GopCadApp/obj/release-staging"

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -o $staging `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishReadyToRun=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "MERGE AUTOCAD publish failed."
}

$exe = Join-Path $staging "BIMLab DWG Merge.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Published EXE not found: $exe"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Get-ChildItem -LiteralPath $releaseRoot -Force -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
Copy-Item -LiteralPath $exe -Destination $releaseRoot -Force
Remove-Item -LiteralPath $staging -Recurse -Force

Write-Host "Created: $(Join-Path $releaseRoot 'BIMLab DWG Merge.exe')" -ForegroundColor Green
