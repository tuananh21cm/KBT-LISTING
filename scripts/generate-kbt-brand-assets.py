"""Generate transparent KBT brand assets from source PNG (black bg removed)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

SRC = Path(
    r"C:\Users\Admin\.cursor\projects\g-Software-development-drop-kbtglobal-app\assets"
    r"\c__Users_Admin_AppData_Roaming_Cursor_User_workspaceStorage_"
    r"60f8873863c27e9de54461774456479a_images_KBT_LOGO-079f074b-48a6-4eb3-910b-73f6699e5702.png"
)
ROOT = Path(__file__).resolve().parents[1]


def remove_black_background(im: Image.Image) -> Image.Image:
    arr = np.array(im.convert("RGBA"))
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    is_bg = (r < 35) & (g < 35) & (b < 35)
    soft = (r < 55) & (g < 55) & (b < 55) & ~is_bg
    arr[is_bg, 3] = 0
    if soft.any():
        intensity = np.maximum(np.maximum(r[soft], g[soft]), b[soft]).astype(np.float32)
        arr[soft, 3] = np.clip(((intensity - 35) / 20.0) * 255, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr, "RGBA")
    alpha = arr[:, :, 3]
    ys, xs = np.where(alpha > 10)
    pad = 8
    x0, x1 = max(0, int(xs.min()) - pad), min(arr.shape[1], int(xs.max()) + 1 + pad)
    y0, y1 = max(0, int(ys.min()) - pad), min(arr.shape[0], int(ys.max()) + 1 + pad)
    return out.crop((x0, y0, x1, y1))


def fit_square(img: Image.Image, size: int, margin_ratio: float = 0.08) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    inner = int(size * (1 - 2 * margin_ratio))
    w, h = img.size
    scale = min(inner / w, inner / h)
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    resized = img.resize((nw, nh), Image.Resampling.LANCZOS)
    x = (size - nw) // 2
    y = (size - nh) // 2
    canvas.paste(resized, (x, y), resized)
    return canvas


def main() -> None:
    logo = remove_black_background(Image.open(SRC))
    print("logo crop", logo.size)

    brand_dir = ROOT / "frontend" / "packages" / "ui" / "assets"
    brand_dir.mkdir(parents=True, exist_ok=True)
    logo.save(brand_dir / "kbt-logo.png", "PNG", optimize=True)
    fit_square(logo, 512, margin_ratio=0.06).save(brand_dir / "kbt-favicon.png", "PNG", optimize=True)

    for app in ("admin-web", "seller-web"):
        pub = ROOT / "frontend" / app / "public"
        logo.save(pub / "brand-logo.png", "PNG", optimize=True)
        fav = fit_square(logo, 128, margin_ratio=0.06)
        fav.save(pub / "brand-favicon.png", "PNG", optimize=True)
        fav.save(pub / "favicon.png", "PNG", optimize=True)
        print("web", app, "ok")

    sizes = {
        "favicon.png": 32,
        "icon-16.png": 16,
        "icon-32.png": 32,
        "icon-48.png": 48,
        "icon-128.png": 128,
        "icon-source.png": 150,
    }
    for ext in ("browser-extension", "fulfil-extension", "tiktok-shop-extension"):
        icons = ROOT / "frontend" / ext / "public" / "icons"
        for name, size in sizes.items():
            margin = 0.08 if size >= 48 else 0.1
            fit_square(logo, size, margin_ratio=margin).save(icons / name, "PNG", optimize=True)
        print("ext", ext, "ok")

    print("done")


if __name__ == "__main__":
    main()
