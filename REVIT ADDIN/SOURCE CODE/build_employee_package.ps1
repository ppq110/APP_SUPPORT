param(
    [string]$ReleaseDate = (Get-Date -Format "dd-MM-yyyy")
)

$ErrorActionPreference = "Stop"
$base = $PSScriptRoot

if ($ReleaseDate -notmatch '^\d{2}-\d{2}-\d{4}$') {
    throw "ReleaseDate must use dd-MM-yyyy format."
}

function Copy-PackageFile {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "NOT FOUND: $SourcePath"
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
}

function Assert-CleanArchive {
    param([string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $forbidden = @($archive.Entries | Where-Object {
            $parts = @($_.FullName -split '[/\\]' | Where-Object { $_ })
            $leaf = if ($parts.Count -gt 0) { $parts[-1] } else { "" }
            $leaf -match '(?i)\.(dll|json|pdb|xml)$' -or
                $parts -contains 'bin' -or $parts -contains 'obj'
        })

        if ($forbidden.Count -gt 0) {
            throw "Forbidden files found in archive $Path`n$($forbidden.FullName -join [Environment]::NewLine)"
        }
    } finally {
        $archive.Dispose()
    }
}

Write-Host "=== 1. Build DynLock.Core ===" -ForegroundColor Cyan
dotnet build (Join-Path $base "src/DynLock.Core/DynLock.Core.csproj") -c Release `
    --source https://api.nuget.org/v3/index.json `
    /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "DynLock.Core build failed." }

Write-Host ""
Write-Host "=== 2. Build DynLock.Addin (net48 + net8.0-windows) ===" -ForegroundColor Cyan
dotnet build (Join-Path $base "src/DynLock.Addin/DynLock.Addin.csproj") -c Release `
    --source https://api.nuget.org/v3/index.json `
    /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "DynLock.Addin build failed." }

Write-Host ""
Write-Host "=== 3. Build DynLock.EncryptorGui (BIMLab Studio) ===" -ForegroundColor Cyan
dotnet build (Join-Path $base "src/DynLock.EncryptorGui/DynLock.EncryptorGui.csproj") -c Release `
    --source https://api.nuget.org/v3/index.json `
    /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "DynLock.EncryptorGui build failed." }

Write-Host ""
Write-Host "=== 4. Build DynLock.Installer (BIMLab Bach Khoa - embeds Addin) ===" -ForegroundColor Cyan
dotnet build (Join-Path $base "src/DynLock.Installer/DynLock.Installer.csproj") -c Release `
    --source https://api.nuget.org/v3/index.json `
    /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "DynLock.Installer build failed." }

Write-Host ""
Write-Host "=== 5. Update dist folders ===" -ForegroundColor Cyan

$distRoot = Join-Path $base "dist"
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Get-ChildItem -LiteralPath $distRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^BIMLab_(BachKhoa_Member|Studio_Leader)(_.+)?$' } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
Get-ChildItem -LiteralPath $distRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^BIMLab_(BachKhoa_Member|Studio_Leader)(_.+)?\.zip$' } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

$memberFolderName = "BIMLab_BachKhoa_Member_$ReleaseDate"
$leaderFolderName = "BIMLab_Studio_Leader_$ReleaseDate"
$distMember = Join-Path $distRoot $memberFolderName
$distLeader = Join-Path $distRoot $leaderFolderName
New-Item -ItemType Directory -Path $distMember -Force | Out-Null
New-Item -ItemType Directory -Path $distLeader -Force | Out-Null

$memberExeFolder = Join-Path $base "src/DynLock.Installer/bin/Release/net48"
$memberExe = (Get-ChildItem -LiteralPath $memberExeFolder -File -Filter '*.exe' | Select-Object -First 1).FullName
if (-not $memberExe) { throw "No exe found in $memberExeFolder" }
$memberExeName = "BIMLab Bach Khoa - $ReleaseDate.exe"
$memberGuideName = "Hướng dẫn - BIMLab Bách Khoa - $ReleaseDate.txt"
Copy-PackageFile -SourcePath $memberExe -DestinationPath (Join-Path $distMember $memberExeName)
Copy-PackageFile -SourcePath (Join-Path $base "HƯỚNG DẪN NHÂN VIÊN.txt") -DestinationPath (Join-Path $distMember $memberGuideName)
Write-Host "  Created clean Bach Khoa package folder: $memberFolderName"

$leaderExeFolder = Join-Path $base "src/DynLock.EncryptorGui/bin/Release/net48"
$leaderExe = (Get-ChildItem -LiteralPath $leaderExeFolder -File -Filter '*.exe' | Select-Object -First 1).FullName
if (-not $leaderExe) { throw "No exe found in $leaderExeFolder" }
$leaderExeName = "BIMLab Studio - $ReleaseDate.exe"
$leaderGuideName = "Hướng dẫn - BIMLab Studio - $ReleaseDate.txt"
Copy-PackageFile -SourcePath $leaderExe -DestinationPath (Join-Path $distLeader $leaderExeName)
Copy-PackageFile -SourcePath (Join-Path $base "HƯỚNG DẪN TEAM LEAD.txt") -DestinationPath (Join-Path $distLeader $leaderGuideName)
Write-Host "  Created clean Studio package folder: $leaderFolderName"

Write-Host ""
Write-Host "=== 6. Rebuild zip files ===" -ForegroundColor Cyan

$zipMember = Join-Path $distRoot "$memberFolderName.zip"
$zipLeader = Join-Path $distRoot "$leaderFolderName.zip"

if (Test-Path -LiteralPath $zipMember) { Remove-Item -LiteralPath $zipMember -Force }
if (Test-Path -LiteralPath $zipLeader) { Remove-Item -LiteralPath $zipLeader -Force }

Compress-Archive -Path $distMember -DestinationPath $zipMember -Force
Compress-Archive -Path $distLeader -DestinationPath $zipLeader -Force

Assert-CleanArchive -Path $zipMember
Assert-CleanArchive -Path $zipLeader

Write-Host "  Created and verified: $memberFolderName.zip"
Write-Host "  Created and verified: $leaderFolderName.zip"

Write-Host ""
Write-Host "=== 7. Sync deliverables ===" -ForegroundColor Cyan

$deliverablesRoot = Join-Path (Split-Path $base -Parent) "deliverables"
New-Item -ItemType Directory -Path $deliverablesRoot -Force | Out-Null

$deliverableMemberZip = Join-Path $deliverablesRoot "BIMLab_BachKhoa_Member.zip"
$deliverableLeaderZip = Join-Path $deliverablesRoot "BIMLab_Lead.zip"
Copy-PackageFile -SourcePath $zipMember -DestinationPath $deliverableMemberZip
Copy-PackageFile -SourcePath $zipLeader -DestinationPath $deliverableLeaderZip

Write-Host "  Synced deliverables:"
Write-Host "    $deliverableMemberZip"
Write-Host "    $deliverableLeaderZip"

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "dist/ contains 2 clean folders + 2 verified zip files."
Write-Host "deliverables/ contains the final 2 zip files."
