# drop.kinhbac.ai


## Chạy local

Yêu cầu: .NET 8 SDK, Node 24, Postgres 16 trên `localhost:5432` (DB `db_kbt_drop`, user `user_kbt`). Secret (mật khẩu DB, JWT, Telegram, OpenAI, xAI) **không** nằm trong `appsettings.json` đã commit: chép `backend/src/Api/appsettings.Development.example.json` → `appsettings.Development.json` (gitignored) và điền vào đó.

```sh
# API: http://localhost:5000 (migration + seed tự chạy khi khởi động)
cd backend/src/Api && dotnet run --launch-profile http

# Frontend (cài 1 lần: cd frontend && npm install; nếu lỗi "Cannot find native binding" thì chạy npm install lần nữa)
cd frontend && npm run dev:seller   # http://localhost:5173
cd frontend && npm run dev:admin    # http://localhost:5174
```

Tài khoản seed: `admin` / `Admin@123`, `seller` / `Seller@123`.

## Production (Cloudflare Tunnel từ máy này)

| App | URL |
|---|---|
| seller-web | https://4seller.kbt.global |
| admin-web | https://4seller-admin.kbt.global |
| API | https://4seller-api.kbt.global |

Tunnel `drop-4seller`, config `%USERPROFILE%\.cloudflared\drop-4seller.yml`. Tự chạy khi đăng nhập Windows qua `Startup\start-drop-4seller.vbs` (API + 2 web + tunnel). Extension dùng API host ở trên.
