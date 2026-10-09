<#
Gom file .exe + hướng dẫn mới nhất của cả 3 tool vào D:\DATA\BIMLab_Suite\Lastest,
mỗi tool 1 folder (exe + hướng dẫn) kèm 1 file .zip tương ứng để gửi cho người khác.

Chạy sau khi đã build lại tool nào đó (build_employee_package.ps1 cho REVIT ADDIN,
npm run dist cho TOOL CONVERT, build_release.ps1 cho TOOL MERGE).
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
        [string]$Name,
        [string[]]$SourceFiles
    )

    $missing = $SourceFiles | Where-Object { -not (Test-Path -LiteralPath $_) }
    if ($missing) {
        Write-Warning "Bỏ qua '$Name' - thiếu file: $($missing -join ', ')"
        return
    }

    $folder = Join-Path $lastest $Name
    New-CleanFolder -Path $folder
    foreach ($file in $SourceFiles) {
        Copy-Item -LiteralPath $file -Destination $folder -Force
    }

    # Xoa cac zip cu cua cung tool (timestamp khac) de Lastest khong tich tu
    # ban cu, tranh nham lan khi sau nay co sua.
    Get-ChildItem -LiteralPath $lastest -Filter "$Name`_*.zip" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $zip = Join-Path $lastest "$Name`_$timestamp.zip"
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -Force

    Write-Host "  OK: $Name (folder + $(Split-Path $zip -Leaf))" -ForegroundColor Green
}

New-Item -ItemType Directory -Path $lastest -Force | Out-Null

Write-Host "=== REVIT ADDIN ===" -ForegroundColor Cyan
$addinDist = Join-Path $root "REVIT ADDIN\SOURCE CODE\dist"
$memberFolder = Get-ChildItem -LiteralPath $addinDist -Directory -Filter "BIMLab_BachKhoa_Member_*" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$leaderFolder = Get-ChildItem -LiteralPath $addinDist -Directory -Filter "BIMLab_Studio_Leader_*" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($memberFolder) {
    Publish-ToolPackage -Name "REVIT_ADDIN_BachKhoa_Member" `
        -SourceFiles (Get-ChildItem -LiteralPath $memberFolder.FullName -File | ForEach-Object { $_.FullName })
} else {
    Write-Warning "Không tìm thấy bản build BIMLab_BachKhoa_Member_* trong dist/. Chạy build_employee_package.ps1 trước."
}

if ($leaderFolder) {
    Publish-ToolPackage -Name "REVIT_ADDIN_Studio_Leader" `
        -SourceFiles (Get-ChildItem -LiteralPath $leaderFolder.FullName -File | ForEach-Object { $_.FullName })
} else {
    Write-Warning "Không tìm thấy bản build BIMLab_Studio_Leader_* trong dist/. Chạy build_employee_package.ps1 trước."
}

# Cac file huong dan co dau tieng Viet trong ten - tim bang wildcard *.txt
# thay vi go thang ten co dau, de tranh loi encoding khi PowerShell 5.1 doc
# file .ps1 khong co BOM (dau tieng Viet trong literal se bi doc sai byte).
function Get-GuideFile {
    param([string]$ToolRoot)
    $guide = Get-ChildItem -LiteralPath $ToolRoot -Filter "*.txt" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    return $guide.FullName
}

Write-Host ""
Write-Host "=== TOOL CONVERT ===" -ForegroundColor Cyan
$convertRoot = Join-Path $root "TOOL CONVERT"
Publish-ToolPackage -Name "TOOL_CONVERT" -SourceFiles @(
    (Join-Path $convertRoot "release\BIMLab_Convert_TXT_To_Excel.exe"),
    (Get-GuideFile -ToolRoot $convertRoot)
)

Write-Host ""
Write-Host "=== TOOL MERGE ===" -ForegroundColor Cyan
$mergeRoot = Join-Path $root "TOOL MERGE"
Publish-ToolPackage -Name "TOOL_MERGE" -SourceFiles @(
    (Join-Path $mergeRoot "RELEASE\BIMLab DWG Merge.exe"),
    (Get-GuideFile -ToolRoot $mergeRoot)
)

Write-Host ""
Write-Host "=== Xong ===" -ForegroundColor Green
Write-Host "Kết quả nằm trong: $lastest"
