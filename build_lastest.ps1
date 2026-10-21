<#
Gom bản phát hành mới nhất của cả 3 tool vào <suite>\Lastest, thành 2 bộ để gửi:

  Lastest\1_NHU_CU_chi_Addin_co_acc\
      Như cũ: chỉ add-in Revit (BIMLab Bách Khoa + BIMLab Studio) bắt đăng nhập,
      TOOL CONVERT và TOOL MERGE KHÔNG cần tài khoản.
  Lastest\2_TOAN_BO_co_acc\
      Toàn bộ 4 app đều phải có tài khoản (Gmail được cấp quyền) mới vào được.

Mỗi tool 1 folder (exe + hướng dẫn) kèm 1 file .zip tương ứng để gửi cho người khác.
Tên zip có hậu tố CO_ACC (cần tài khoản) hoặc KHONG_ACC (không cần) để khỏi gửi nhầm.

Chạy sau khi đã build:
  REVIT ADDIN  : build_employee_package.ps1
  TOOL CONVERT : npm run dist (có đăng nhập) + npm run dist:nologin (không đăng nhập)
  TOOL MERGE   : build_release.ps1 (có đăng nhập) + build_release.ps1 -NoLogin (không đăng nhập)
Hoặc chạy build_all.ps1 để build tất cả rồi gom luôn.
#>

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$lastest = Join-Path $root "Lastest"
$timestamp = Get-Date -Format "dd-MM-yyyy_HHmm"

function New-CleanFolder {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Publish-ToolPackage {
    param(
        [string]$VariantRoot,
        [string]$Name,
        [string]$ZipTag,
        [string[]]$SourceFiles
    )

    $missing = $SourceFiles | Where-Object { -not $_ -or -not (Test-Path -LiteralPath $_) }
    if ($missing) {
        Write-Warning "Bỏ qua '$Name' - thiếu file: $($missing -join ', ')"
        return
    }

    $folder = Join-Path $VariantRoot $Name
    New-CleanFolder -Path $folder
    foreach ($file in $SourceFiles) {
        Copy-Item -LiteralPath $file -Destination $folder -Force
    }

    # Xóa zip cũ của cùng tool (timestamp khác) để Lastest không tích tụ bản cũ.
    Get-ChildItem -LiteralPath $VariantRoot -Filter "$Name`_*.zip" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $zip = Join-Path $VariantRoot "$Name`_$ZipTag`_$timestamp.zip"
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -Force

    Write-Host "  OK: $Name (folder + $(Split-Path $zip -Leaf))" -ForegroundColor Green
}

# File hướng dẫn có dấu tiếng Việt trong tên - tìm bằng wildcard *.txt thay vì gõ thẳng tên.
function Get-GuideFile {
    param([string]$Dir)
    $guide = Get-ChildItem -LiteralPath $Dir -Filter "*.txt" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($guide) { return $guide.FullName }
    return $null
}

New-Item -ItemType Directory -Path $lastest -Force | Out-Null

# Dọn các thư mục/zip kiểu cũ (một bộ phẳng) nằm thẳng trong Lastest\
foreach ($legacy in "REVIT_ADDIN_BachKhoa_Member", "REVIT_ADDIN_Studio_Leader", "TOOL_CONVERT", "TOOL_MERGE") {
    $p = Join-Path $lastest $legacy
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
}
Get-ChildItem -LiteralPath $lastest -Filter "*.zip" -File -ErrorAction SilentlyContinue | Remove-Item -Force

# --- Nguồn từng tool ---------------------------------------------------------
$addinDist = Join-Path $root "REVIT ADDIN\SOURCE CODE\dist"
$memberFolder = Get-ChildItem -LiteralPath $addinDist -Directory -Filter "BIMLab_BachKhoa_Member_*" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$leaderFolder = Get-ChildItem -LiteralPath $addinDist -Directory -Filter "BIMLab_Studio_Leader_*" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

$convertRoot = Join-Path $root "TOOL CONVERT"
$mergeRoot   = Join-Path $root "TOOL MERGE"

# Add-in Revit (member + Studio) luôn bắt đăng nhập -> giống nhau ở cả 2 bộ.
$variants = @(
    @{
        Folder       = "1_NHU_CU_chi_Addin_co_acc"
        Title        = "NHƯ CŨ: chỉ Add-in cần tài khoản"
        Tag          = "KHONG_ACC"
        ConvertExe   = Join-Path $convertRoot "release-nologin\BIMLab_Convert_TXT_To_Excel.exe"
        ConvertGuide = Get-GuideFile (Join-Path $convertRoot "docs")
        MergeExe     = Join-Path $mergeRoot "RELEASE_NOLOGIN\BIMLab DWG Merge.exe"
        MergeGuide   = Get-GuideFile (Join-Path $mergeRoot "docs")
    },
    @{
        Folder       = "2_TOAN_BO_co_acc"
        Title        = "TOÀN BỘ phải có tài khoản"
        Tag          = "CO_ACC"
        ConvertExe   = Join-Path $convertRoot "release\BIMLab_Convert_TXT_To_Excel.exe"
        ConvertGuide = Get-GuideFile $convertRoot
        MergeExe     = Join-Path $mergeRoot "RELEASE\BIMLab DWG Merge.exe"
        MergeGuide   = Get-GuideFile $mergeRoot
    }
)

foreach ($v in $variants) {
    $variantRoot = Join-Path $lastest $v.Folder
    New-Item -ItemType Directory -Path $variantRoot -Force | Out-Null

    Write-Host ""
    Write-Host "################ $($v.Title)  ->  Lastest\$($v.Folder)" -ForegroundColor Yellow

    Write-Host "=== REVIT ADDIN (có đăng nhập) ===" -ForegroundColor Cyan
    if ($memberFolder) {
        Publish-ToolPackage -VariantRoot $variantRoot -Name "REVIT_ADDIN_BachKhoa_Member" -ZipTag "CO_ACC" `
            -SourceFiles (Get-ChildItem -LiteralPath $memberFolder.FullName -File | ForEach-Object { $_.FullName })
    } else {
        Write-Warning "Không tìm thấy bản build BIMLab_BachKhoa_Member_* trong dist/. Chạy build_employee_package.ps1 trước."
    }
    if ($leaderFolder) {
        Publish-ToolPackage -VariantRoot $variantRoot -Name "REVIT_ADDIN_Studio_Leader" -ZipTag "CO_ACC" `
            -SourceFiles (Get-ChildItem -LiteralPath $leaderFolder.FullName -File | ForEach-Object { $_.FullName })
    } else {
        Write-Warning "Không tìm thấy bản build BIMLab_Studio_Leader_* trong dist/. Chạy build_employee_package.ps1 trước."
    }

    Write-Host "=== TOOL CONVERT ($($v.Tag)) ===" -ForegroundColor Cyan
    Publish-ToolPackage -VariantRoot $variantRoot -Name "TOOL_CONVERT" -ZipTag $v.Tag `
        -SourceFiles @($v.ConvertExe, $v.ConvertGuide)

    Write-Host "=== TOOL MERGE ($($v.Tag)) ===" -ForegroundColor Cyan
    Publish-ToolPackage -VariantRoot $variantRoot -Name "TOOL_MERGE" -ZipTag $v.Tag `
        -SourceFiles @($v.MergeExe, $v.MergeGuide)
}

Write-Host ""
Write-Host "=== Xong ===" -ForegroundColor Green
Write-Host "Kết quả nằm trong: $lastest"
