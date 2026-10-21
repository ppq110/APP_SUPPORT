# BIMLab Suite

Gộp 3 công cụ BIMLab vào một thư mục. Mỗi tool là một repo git độc lập (đẩy lên OneDev nội bộ).
Ngoài ra toàn bộ source được lưu trữ thêm trên GitHub riêng:
[ppq110/APP_SUPPORT](https://github.com/ppq110/APP_SUPPORT) (**private** — repo chứa master key mã hóa
và secret admin nên không được để public).

## Cấu trúc

| Thư mục | Mô tả | Git |
|---------|-------|-----|
| `TOOL CONVERT` | App Electron chuyển TXT → Excel (khối lượng Revit) | repo riêng (remote `origin` cũ `APP_CONVERT-TXT-to-EXCEL-` trên GitHub hiện không còn truy cập được) |
| `TOOL MERGE` | Công cụ gộp bản vẽ AutoCAD (WinForms, .NET 8), build được cả bản có và không đăng nhập | repo riêng |
| `REVIT ADDIN` | Add-in Revit (BIMLab Bách Khoa, member) + BIMLab Studio (leader) + DynLock auth server | repo riêng |
| `Lastest/` | Bản phát hành mới nhất, chia 2 bộ (có / không cần tài khoản — xem mục Build), sinh bởi `build_lastest.ps1` | không theo dõi |
| `GITHUB/` | Bản sao sạch để đẩy lên GitHub, sinh bởi `push_github.ps1` | repo APP_SUPPORT |

## Xác thực (đăng nhập) — DÙNG CHUNG

TOOL CONVERT, TOOL MERGE, REVIT ADDIN (member) và BIMLab Studio (leader) đều xác thực qua **cùng một auth server**
(riêng TOOL CONVERT / TOOL MERGE chỉ khi build bản "có đăng nhập", bản "không cần tài khoản" không gọi máy chủ):

- URL: `https://tools.bimlab.com.vn` (hardcode trong bản build, chạy được cả ngoài mạng nội bộ, chỉ cần có internet)
- API: `GET /api/auth/check?email=<email>` → 200 nếu email có trong bảng `authorized_leaders` và `is_active`,
  404 nếu chưa được cấp quyền (app báo "Email chưa được cấp quyền").
- Quản lý danh sách leader (BIMLab Studio): `/api/leaders*`. Qua nginx chỉ cho phép khi có header
  `X-Bimlab-Admin` (hằng `AdminHeaderValue` trong `AuthServerService.cs`, phải trùng với
  `location ^~ /api/leaders` trong `nginx.146.conf` trên server). Server vẫn kiểm tra thêm `CanManage(managerEmail)`.
- Server: máy `192.168.110.146`, container `bimlab-auth-server` (ASP.NET) + Postgres `bimlab_auth`
  (container `bimlab_pg`); reverse proxy công khai là container `dt_nginx`.
  Sửa config nginx xong phải `docker restart dt_nginx` (reload không ăn vì bind-mount theo inode).

## Build

Cần: .NET SDK 8, .NET Framework 4.8 Developer Pack, Node.js LTS (cài qua `winget`).

Một lệnh build tất cả rồi gom vào `Lastest\`:

```powershell
.\build_all.ps1        # thêm -SkipAddin / -SkipConvert / -SkipMerge để bỏ qua phần không đổi
```

Có **2 bộ phát hành** trong `Lastest\` (zip có hậu tố `CO_ACC` = cần tài khoản, `KHONG_ACC` = không cần):

| Bộ | Add-in Revit (member + Studio) | TOOL CONVERT | TOOL MERGE |
|----|:---:|:---:|:---:|
| `1_NHU_CU_chi_Addin_co_acc` (như cũ) | cần tài khoản | không cần | không cần |
| `2_TOAN_BO_co_acc` | cần tài khoản | cần | cần |

Build lẻ từng tool:

- `TOOL CONVERT`: `npm install`; `npm run dist` (có đăng nhập) → `release/`; `npm run dist:nologin` (không cần tài khoản) → `release-nologin/`
- `TOOL MERGE`: `SOURCE CODE/build_release.ps1` (có đăng nhập) → `RELEASE/`; `build_release.ps1 -NoLogin` → `RELEASE_NOLOGIN/`
- `REVIT ADDIN`: `SOURCE CODE/build_employee_package.ps1` → `dist/` + `deliverables/` (member và Studio, luôn có đăng nhập)
- Gom bản phát hành: `.\build_lastest.ps1` → `Lastest/`

Bản "không cần tài khoản" bỏ hẳn code đăng nhập: MERGE không biên dịch `AuthGate.cs`/`LoginForm.cs`
(`/p:RequireLogin=false`); CONVERT build với `VITE_REQUIRE_LOGIN=false` và `requireLogin=false` trong
`package.json` đóng gói. Mỗi bản dùng hướng dẫn riêng (bản không đăng nhập nằm ở `docs\` của từng tool).

## Đẩy source lên GitHub

Quy trình: sửa code → commit/đẩy lên OneDev nội bộ → chạy:

```powershell
.\push_github.ps1 -Message "Sửa lỗi đăng nhập ngoài mạng nội bộ"
```

- Script đồng bộ source vào `GITHUB/` (bỏ file build, `*.exe`, `*.zip`, `*.dyn`/`*.dynx`, thư mục mẫu CAD),
  quét không để lọt file > 50 MB hay khóa/mật khẩu lạ, commit **bằng tiếng Việt** rồi push lên `main`.
- Không truyền `-Message` thì mỗi tool một commit mặc định (`<tool>: cập nhật source mới nhất ngày ...`).
- `-NoPush`: chỉ đồng bộ + commit, chưa push.
- Lần đầu push cần đăng nhập GitHub (Git Credential Manager mở trình duyệt).
