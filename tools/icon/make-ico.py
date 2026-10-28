"""把 1024×1024 的设计稿生成 Windows 多尺寸 ICO。

用法：python tools/icon/make-ico.py "D:/Downloads/启动器gui设计稿/桌面icon.png"

- 预乘 alpha 后再缩放，半透明边缘不会出黑边
- 256 用 PNG 内嵌，其余 32bpp BMP（NSIS 兼容性最稳）
"""
import io
import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / "src" / "MutsumiLauncher" / "Assets" / "app.ico"

SIZES = [16, 24, 32, 48, 64, 128, 256]


def resize(image: Image.Image, size: int) -> Image.Image:
    source = image.convert("RGBA")
    width, height = source.size

    pre = Image.new("RGBA", (width, height))
    src, dst = source.load(), pre.load()
    for y in range(height):
        for x in range(width):
            r, g, b, a = src[x, y]
            dst[x, y] = (r * a // 255, g * a // 255, b * a // 255, a)

    small = pre.resize((size, size), Image.LANCZOS)
    sp = small.load()

    result = Image.new("RGBA", (size, size))
    rp = result.load()
    for y in range(size):
        for x in range(size):
            r, g, b, a = sp[x, y]
            rp[x, y] = (0, 0, 0, 0) if a == 0 else (
                min(255, r * 255 // a), min(255, g * 255 // a), min(255, b * 255 // a), a)

    return result


def bmp_frame(image: Image.Image) -> bytes:
    width, height = image.size
    pixels = image.load()

    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0,
                         width * height * 4, 0, 0, 0, 0)

    xor = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            xor += bytes((b, g, r, a))

    mask = bytes(((width + 31) // 32) * 4 * height)
    return header + bytes(xor) + mask


def png_frame(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def build(source_path: Path) -> bytes:
    source = Image.open(source_path)
    frames = []

    for size in SIZES:
        scaled = resize(source, size)
        frames.append((size, png_frame(scaled) if size >= 256 else bmp_frame(scaled)))

    header = struct.pack("<HHH", 0, 1, len(frames))
    directory = b""
    payload = b""
    offset = 6 + 16 * len(frames)

    for size, data in frames:
        directory += struct.pack("<BBBBHHII", size if size < 256 else 0,
                                 size if size < 256 else 0, 0, 0, 1, 32,
                                 len(data), offset + len(payload))
        payload += data

    return header + directory + payload


def main() -> None:
    if len(sys.argv) < 2:
        print("用法：python tools/icon/make-ico.py <设计稿.png>")
        raise SystemExit(1)

    data = build(Path(sys.argv[1]))
    TARGET.write_bytes(data)
    print(f"{TARGET.name}: {len(data)} bytes, {len(SIZES)} 帧（{'/'.join(map(str, SIZES))}）")


if __name__ == "__main__":
    main()
