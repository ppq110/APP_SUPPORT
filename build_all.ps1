<#
Build lại TOÀN BỘ rồi gom vào Lastest\ (2 bộ: "như cũ" và "toàn bộ có tài khoản").

  REVIT ADDIN  : build_employee_package.ps1            (member + Studio, luôn có đăng nhập)
  TOOL CONVERT : npm run dist + npm run dist:nologin   (có / không đăng nhập)
  TOOL MERGE   : build_release.ps1 + -NoLogin           (có / không đăng nhập)
  rồi build_lastest.ps1

Cần: .NET SDK 8, .NET Framework 4.8 Developer Pack, Node.js LTS.
Bỏ qua phần nào đó cho nhanh: -SkipAddin, -SkipConvert, -SkipMerge.
#>

param(
    [switch]$SkipAddin,
    [switch]$SkipConvert,
    [switch]$SkipMerge
)

$root = $PSScriptRoot

# Nạp lại PATH (phòng khi terminal mở trước lúc cài Node/.NET)
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Assert-Ok {
    param([string]$What)
    if ($LASTEXITCODE -ne 0) { throw "$What lỗi (mã $LASTEXITCODE)." }
}

if (-not $SkipAddin) {
    Write-Host "##### REVIT ADDIN #####" -ForegroundColor Yellow
    & (Join-Path $root "REVIT ADDIN\SOURCE CODE\build_employee_package.ps1")
}

if (-not $SkipConvert) {
    Write-Host "##### TOOL CONVERT #####" -ForegroundColor Yellow
    Push-Location (Join-Path $root "TOOL CONVERT")
    try {
        if (-not (Test-Path "node_modules")) { npm install --no-fund --no-audit; Assert-Ok "npm install" }
        Write-Host "--- bản CÓ đăng nhập ---" -ForegroundColor Cyan
        npm run dist;          Assert-Ok "npm run dist"
        Write-Host "--- bản KHÔNG cần tài khoản ---" -ForegroundColor Cyan
        npm run dist:nologin;  Assert-Ok "npm run dist:nologin"
    } finally { Pop-Location }
}

if (-not $SkipMerge) {
    Write-Host "##### TOOL MERGE #####" -ForegroundColor Yellow
    $mergeBuild = Join-Path $root "TOOL MERGE\SOURCE CODE\build_release.ps1"
    Write-Host "--- bản CÓ đăng nhập ---" -ForegroundColor Cyan
    & $mergeBuild
    Write-Host "--- bản KHÔNG cần tài khoản ---" -ForegroundColor Cyan
    & $mergeBuild -NoLogin
}

Write-Host "##### GOM VÀO Lastest\ #####" -ForegroundColor Yellow
& (Join-Path $root "build_lastest.ps1")
