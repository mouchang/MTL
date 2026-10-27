"""从 background.jpg 生成深色主题用的 background-dark.jpg。

压暗 + 轻微去饱和 + 往主题的深绿底上靠，保证浅色文字（#E7F0E9）压在上面还看得清。
改完配色重跑一次即可：python tools/backgrounds/make-dark.py
"""
from pathlib import Path

import numpy as np
from PIL import Image

ASSETS = Path(__file__).resolve().parents[2] / "src" / "MutsumiLauncher" / "Assets"
SOURCE = ASSETS / "background.jpg"
TARGET = ASSETS / "background-dark.jpg"

# 主题深色底（Palette 里的 WindowBase #111710）
BASE_TINT = np.array([0x11, 0x17, 0x10], dtype=np.float32) / 255.0

BRIGHTNESS = 0.30      # 原图保留多少
TINT_WEIGHT = 0.55     # 往深绿底上靠多少
SATURATION = 0.62      # 去饱和
MAX_LEVEL = 0.34       # 最亮不超过这个值，保证浅色文字对比度


def main() -> None:
    image = Image.open(SOURCE).convert("RGB")
    pixels = np.asarray(image).astype(np.float32) / 255.0

    gray = pixels.mean(axis=2, keepdims=True)
    pixels = gray * (1.0 - SATURATION) + pixels * SATURATION

    out = pixels * BRIGHTNESS
    out = out + BASE_TINT * TINT_WEIGHT * (1.0 - out)

    peak = out.max()
    if peak > MAX_LEVEL:
        out *= MAX_LEVEL / peak

    image = Image.fromarray(np.clip(out * 255.0, 0, 255).astype(np.uint8), mode="RGB")
    image.save(TARGET, "JPEG", quality=88, optimize=True, progressive=True)

    print(f"{TARGET.name}: {image.size[0]}x{image.size[1]}, {TARGET.stat().st_size} bytes")


if __name__ == "__main__":
    main()
