"""Разбор PNG-кадра: что на нём на самом деле.

Запуск:
    blender --background --python inspect_image.py -- <путь.png> [<путь.png> ...]

Зачем нужен инструмент, а не глаз: размер файла — плохой признак. Пустой кадр
(один цвет) и кадр с плотным туманом весят одинаково мало, а кадр с моделями
— много. Разложить это на числа можно только чтением пикселей.

Что печатается:

- **Доля уникальных цветов.** Меньше процента — кадр почти одноцветный.
- **Яркость центра против углов.** Виньетка обязана затемнять углы: если
  углы не темнее центра, эффект не применился, и «виньетка настроена» — неправда.
- **Разброс яркости.** Нулевой разброс означает, что в кадре нет геометрии
  или нет света.

Этот скрипт — диагностика, а не рендерер: он не требует GUI и работает headless.
"""

from __future__ import annotations

import os
import sys

import bpy


def configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass


def analyse(path: str) -> dict:
    image = bpy.data.images.load(os.path.abspath(path))
    width, height = image.size
    pixels = list(image.pixels)  # RGBA, снизу вверх, построчно
    channels = 4

    def luminance_at(px: int, py: int) -> float:
        i = (py * width + px) * channels
        r, g, b = pixels[i], pixels[i + 1], pixels[i + 2]
        return 0.2126 * r + 0.7152 * g + 0.0722 * b

    def region(x0, y0, x1, y1) -> float:
        total = 0.0
        count = 0
        step = max(1, (x1 - x0) // 24)
        for py in range(y0, y1, step):
            for px in range(x0, x1, step):
                total += luminance_at(px, py)
                count += 1
        return total / max(count, 1)

    # Доля уникальных цветов: считаем по подвыборке, полный перебор на
    # 1600x900 в Python слишком медленный.
    step = max(1, (width * height) // 40000)
    seen = set()
    samples = 0
    for index in range(0, width * height, step):
        i = index * channels
        seen.add((
            round(pixels[i] * 24), round(pixels[i + 1] * 24),
            round(pixels[i + 2] * 24),
        ))
        samples += 1

    third_w = width // 4
    third_h = height // 4
    corners = (
        region(0, 0, third_w, third_h),
        region(width - third_w, 0, width, third_h),
        region(0, height - third_h, third_w, height),
        region(width - third_w, height - third_h, width, height),
    )
    centre = region(width // 2 - third_w // 2, height // 2 - third_h // 2,
                    width // 2 + third_w // 2, height // 2 + third_h // 2)

    bpy.data.images.remove(image)
    return {
        "path": path,
        "size": (width, height),
        "unique_ratio": len(seen) / float(max(samples, 1)),
        "corner_mean": sum(corners) / 4.0,
        "centre_mean": centre,
        "spread": max(corners + (centre,)) - min(corners + (centre,)),
    }


def main() -> int:
    configure_stdout()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("укажи путь к PNG после --")
        return 2
    for path in argv:
        if not os.path.exists(path):
            print("НЕТ ФАЙЛА", path)
            continue
        row = analyse(path)
        vignette = "углы темнее центра" if row["corner_mean"] < row["centre_mean"] \
            else "углы НЕ темнее центра"
        print("%-46s %dx%d цветов=%.1f%% центр=%.3f углы=%.3f разброс=%.3f | %s"
              % (os.path.basename(row["path"]), row["size"][0], row["size"][1],
                 row["unique_ratio"] * 100.0, row["centre_mean"],
                 row["corner_mean"], row["spread"], vignette))
    return 0


if __name__ == "__main__":
    if bpy.app.background:
        sys.exit(main())
    else:
        main()
