# Ghi chú cho AI agent

## Môi trường dev cục bộ

- API chạy tại `http://localhost:5000` (prefix `/api/v1`), profile **`http`** trong `backend/src/Api/Properties/launchSettings.json` (profile này set `ASPNETCORE_ENVIRONMENT=Development`). Chạy bằng `dotnet run --launch-profile http` — sai tên profile hoặc `--no-launch-profile` thì `appsettings.Development.json` không được nạp và API chết vì sai mật khẩu Postgres.
- Migration tự áp dụng khi khởi động API (`DataSeeder.SeedAsync` gọi `MigrateAsync`), nên đổi schema chỉ cần restart API.
- **Chuỗi kết nối thật nằm ở `backend/src/Api/appsettings.Development.json`** (gitignored). File `appsettings.json` trong repo trỏ `localhost` và **không** phải DB đang chạy — đừng dùng nó để query.
- File gitignored không xuất hiện trong kết quả của công cụ search/glob; muốn biết chúng có tồn tại không thì liệt kê bằng shell.

## Thông tin đăng nhập

Nằm ở `dev-credentials.local` (gitignored qua `*.local`). Không chép nội dung file đó sang tài liệu được commit.

## Tài liệu phải cập nhật

Khi thay đổi có ảnh hưởng tới hợp đồng API hoặc kiến trúc, cập nhật `docs/api.md` và `docs/architecture.md` trong cùng lần sửa.
