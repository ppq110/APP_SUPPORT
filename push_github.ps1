<#
Đồng bộ source mới nhất của 3 tool (REVIT ADDIN, TOOL CONVERT, TOOL MERGE) lên
GitHub riêng: https://github.com/ppq110/APP_SUPPORT (repo private).

Cách hoạt động:
  1. Sao chép source sang thư mục GITHUB\ (bản sao sạch của repo APP_SUPPORT),
     bỏ file build, exe/zip, .dyn/.dynx và thư mục mẫu CAD.
  2. Quét kiểm tra: không file nào > 50 MB, không có khóa/mật khẩu lạ lọt vào.
  3. Commit bằng tiếng Việt (không ghi nguồn công cụ) rồi push lên nhánh main.

Cách dùng (chạy sau khi đã sửa code và đẩy lên OneDev nội bộ):
  .\push_github.ps1 -Message "Sửa lỗi đăng nhập ngoài mạng nội bộ"
  .\push_github.ps1               # mỗi tool một commit với nội dung mặc định
  .\push_github.ps1 -NoPush       # chỉ đồng bộ + commit, chưa push

Lần đầu push cần đăng nhập GitHub (Git Credential Manager sẽ mở trình duyệt).
#>

param(
    [string]$Message = "",
    [switch]$NoPush
)

$root   = $PSScriptRoot
$repo   = Join-Path $root "GITHUB"
$remote = "https://github.com/ppq110/APP_SUPPORT.git"
$tools  = @("REVIT ADDIN", "TOOL CONVERT", "TOOL MERGE")
$today  = Get-Date -Format "dd/MM/yyyy"

function Invoke-Git {
    & git -C $repo @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') lỗi (mã $LASTEXITCODE)" }
}

function Commit-Staged {
    param([string]$Text)
    & git -C $repo diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { return $false }   # không có gì để commit

    # Ghi message ra file UTF-8 không BOM rồi dùng -F để tiếng Việt không bị lỗi mã hóa.
    $tmp = Join-Path $env:TEMP ("commitmsg_" + [guid]::NewGuid().ToString("N") + ".txt")
    [System.IO.File]::WriteAllText($tmp, $Text, (New-Object System.Text.UTF8Encoding $false))
    try { Invoke-Git commit -F $tmp | Out-Null } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    Write-Host "  Đã commit: $Text" -ForegroundColor Green
    return $true
}

# --- 1. Khởi tạo repo (lần đầu) --------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $repo ".git"))) {
    New-Item -ItemType Directory -Path $repo -Force -ErrorAction Stop | Out-Null
    Invoke-Git init -b main | Out-Null
    Invoke-Git remote add origin $remote
}
Invoke-Git remote set-url origin $remote
Invoke-Git config user.name  "ppq110"
Invoke-Git config user.email "ppquy110@gmail.com"
Invoke-Git config core.quotepath false
Invoke-Git config core.autocrlf false

# --- 2. Đồng bộ source ------------------------------------------------------
Write-Host "=== Đồng bộ source sang GITHUB\ ===" -ForegroundColor Cyan

# Thư mục/tệp KHÔNG đưa lên GitHub: file build, bản phát hành, mẫu CAD, script Dynamo gốc,
# workflow Supabase cũ (chứa khóa cũ).
$excludeDirs  = @(".git", "bin", "obj", ".vs", "node_modules", "dist", "release*",
                  "deliverables", ".vite", "SAMPLE CAD FILES", "bimlab-keepalive")
$excludeFiles = @("*.exe", "*.zip", "*.pdb", "*.user", "*.log", "*.tmp", "*.dyn", "*.dynx")

foreach ($t in $tools) {
    $src = Join-Path $root $t
    $dst = Join-Path $repo $t
    if (-not (Test-Path -LiteralPath $src)) { Write-Warning "Bỏ qua '$t' - không thấy thư mục."; continue }

    robocopy $src $dst /MIR /XD $excludeDirs /XF $excludeFiles /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy '$t' lỗi (mã $LASTEXITCODE)" }
    Write-Host "  OK: $t"
}

foreach ($f in @("README.md", "build_all.ps1", "build_lastest.ps1", "push_github.ps1")) {
    Copy-Item -LiteralPath (Join-Path $root $f) -Destination $repo -Force -ErrorAction Stop
}

$gitignore = @"
bin/
obj/
.vs/
node_modules/
dist/
release/
release-nologin/
RELEASE/
RELEASE_NOLOGIN/
deliverables/
.vite/
*.exe
*.zip
*.pdb
*.user
*.dyn
*.dynx
Thumbs.db
desktop.ini
"@
[System.IO.File]::WriteAllText((Join-Path $repo ".gitignore"), $gitignore, (New-Object System.Text.UTF8Encoding $false))

# --- 3. Quét an toàn trước khi commit ---------------------------------------
Write-Host "=== Quét an toàn ===" -ForegroundColor Cyan

$all = Get-ChildItem -LiteralPath $repo -Recurse -File -Force |
       Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' }

$big = @($all | Where-Object { $_.Length -gt 50MB })
if ($big.Count -gt 0) {
    throw "Có file > 50 MB, GitHub không nhận:`n$($big.FullName -join "`n")"
}

# Các khóa/mật khẩu KHÔNG được lọt lên (master key và secret admin là chủ ý giữ lại nên không nằm trong danh sách).
$patterns = @(
    'eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.',        # JWT (vd. anon key Supabase)
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',                  # private key
    'ghp_[A-Za-z0-9]{30,}',                                # GitHub token
    'github_pat_[A-Za-z0-9_]{30,}',                        # GitHub fine-grained token
    'AKIA[0-9A-Z]{16}',                                    # AWS access key
    '(?i)password\s*=\s*[^*;\s"''<]{4,}'                   # mật khẩu nằm trong chuỗi kết nối DB
)
$textExt = @(".cs",".cjs",".js",".jsx",".json",".ps1",".md",".txt",".yml",".yaml",
             ".config",".csproj",".sln",".props",".targets",".html",".css",".sh",".xml")
$hits = @()
foreach ($f in ($all | Where-Object { $textExt -contains $_.Extension.ToLowerInvariant() })) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $text) { continue }
    foreach ($p in $patterns) {
        if ($text -match $p) { $hits += ("{0}  <- {1}" -f $f.FullName.Substring($repo.Length + 1), $p) }
    }
}
if ($hits.Count -gt 0) {
    throw "Phát hiện nội dung nhạy cảm, dừng lại (chưa commit):`n$($hits -join "`n")"
}
Write-Host ("  OK: {0} file, không có file lớn hay khóa lạ." -f $all.Count)

# --- 4. Commit (tiếng Việt) -------------------------------------------------
Write-Host "=== Commit ===" -ForegroundColor Cyan
$committed = $false

if ($Message.Trim().Length -gt 0) {
    Invoke-Git add -A
    $committed = Commit-Staged $Message.Trim()
} else {
    foreach ($t in $tools) {
        $isNew = -not (& git -C $repo ls-files -- $t)
        Invoke-Git add -A -- $t
        $verb = if ($isNew) { "khởi tạo source" } else { "cập nhật source mới nhất" }
        if (Commit-Staged "${t}: $verb ngày $today") { $committed = $true }
    }
    Invoke-Git add -A
    if (Commit-Staged "Cập nhật README, script build và script đồng bộ GitHub ngày $today") { $committed = $true }
}

if (-not $committed) {
    Write-Host "Không có thay đổi mới so với lần trước." -ForegroundColor Yellow
}

# --- 5. Push ----------------------------------------------------------------
if ($NoPush) {
    Write-Host "Đã bỏ qua push (-NoPush). Bản commit nằm ở: $repo" -ForegroundColor Yellow
    return
}

Write-Host "=== Push lên GitHub ===" -ForegroundColor Cyan
& git -C $repo push -u origin main
if ($LASTEXITCODE -ne 0) {
    throw "Push thất bại (mã $LASTEXITCODE). Nếu chưa đăng nhập GitHub, hãy chạy lại trong terminal của bạn để đăng nhập."
}
Write-Host "Xong: https://github.com/ppq110/APP_SUPPORT" -ForegroundColor Green
