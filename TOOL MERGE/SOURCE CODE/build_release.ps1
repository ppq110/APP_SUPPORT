param(
    [string]$Runtime = "win-x64",
    # Bản "không cần tài khoản": bỏ hẳn code đăng nhập, xuất ra RELEASE_NOLOGIN\ (mặc định: bản CÓ đăng nhập -> RELEASE\)
    [switch]$NoLogin
)

$ErrorActionPreference = "Stop"
$sourceRoot = $PSScriptRoot
$project = Join-Path $sourceRoot "GopCadApp/GopCadApp.csproj"
$releaseName = if ($NoLogin) { "RELEASE_NOLOGIN" } else { "RELEASE" }
$releaseRoot = Join-Path (Split-Path $sourceRoot -Parent) $releaseName
$stagingName = if ($NoLogin) { "release-staging-nologin" } else { "release-staging" }
$staging = Join-Path $sourceRoot "GopCadApp/obj/$stagingName"

# Đổi giữa 2 bản (có/không đăng nhập) phải biên dịch lại từ đầu, không dùng nhầm obj\Release cũ.
$stale = Join-Path $sourceRoot "GopCadApp/obj/Release"
if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }

$publishArgs = @(
    "publish", $project, "-c", "Release", "-r", $Runtime, "--self-contained", "true",
    "-o", $staging,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:PublishReadyToRun=true",
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)
if ($NoLogin) { $publishArgs += "/p:RequireLogin=false" }

& dotnet @publishArgs

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

$label = if ($NoLogin) { "KHÔNG cần tài khoản" } else { "CÓ đăng nhập" }
Write-Host "Created ($label): $(Join-Path $releaseRoot 'BIMLab DWG Merge.exe')" -ForegroundColor Green
