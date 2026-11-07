"""Генератор звуков прибытия в депо.

Воспроизводимый источник клипов: WAV собираются из шума и простых
осцилляторов, поэтому в репозитории нет чужих лицензий, а результат можно
пересобрать одной командой.

    python Tools/Audio/build_sfx.py --all
    python Tools/Audio/build_sfx.py --only wind_loop brakes --force
    python Tools/Audio/build_sfx.py --check
    python Tools/Audio/build_sfx.py --list

Выход: Assets/_Project/Content/Audio/Arrival/*.wav (16-bit mono PCM, 44.1 кГц).
Импорт для WebGL настраивает Assets/_Project/Editor/AudioArrivalImportPostprocessor.cs.
"""

from __future__ import annotations

import argparse
import os
import sys
import wave

import numpy as np

SR = 44100
PEAK_DB = -3.0
SEED = 20261101
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Content", "Audio", "Arrival")


# ------------------------------------------------------------------ утилиты


def seconds(value: float) -> int:
    return max(1, int(round(value * SR)))


def noise(rng: np.random.Generator, n: int) -> np.ndarray:
    return rng.standard_normal(n)


def band_mask(freqs: np.ndarray, lo: float, hi: float) -> np.ndarray:
    """Мягкая полоса: спады четвёртого порядка, без звона на краях."""
    safe = np.maximum(freqs, 1.0)
    high_pass = 1.0 / (1.0 + (lo / safe) ** 4)
    low_pass = 1.0 / (1.0 + (safe / max(hi, lo + 1.0)) ** 4)
    return high_pass * low_pass


def band_noise(
    rng: np.random.Generator, n: int, lo: float, hi: float
) -> np.ndarray:
    white = rng.standard_normal(n)
    spectrum = np.fft.rfft(white)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    return np.fft.irfft(spectrum * band_mask(freqs, lo, hi), n)


def envelope(n: int, attack: float, decay: float, curve: float = 2.0) -> np.ndarray:
    a = max(1, int(attack * SR))
    d = max(1, int(decay * SR))
    env = np.zeros(n)

    a = min(a, n)
    env[:a] = np.linspace(0.0, 1.0, a, endpoint=False)
    d = min(d, n - a)
    if d > 0:
        env[a : a + d] = (1.0 - np.linspace(0.0, 1.0, d)) ** curve
    return env


def sweep(n: int, start_hz: float, end_hz: float) -> np.ndarray:
    freq = np.linspace(start_hz, end_hz, n)
    phase = 2.0 * np.pi * np.cumsum(freq) / SR
    return np.sin(phase)


def remove_dc(x: np.ndarray) -> np.ndarray:
    return x - float(np.mean(x))


def fade_edges(x: np.ndarray, duration: float = 0.005) -> np.ndarray:
    n = min(seconds(duration), len(x) // 2)
    if n <= 0:
        return x

    ramp = np.linspace(0.0, 1.0, n)
    x[:n] *= ramp
    x[-n:] *= ramp[::-1]
    return x


def normalize_peak(x: np.ndarray, db: float = PEAK_DB) -> np.ndarray:
    peak = float(np.max(np.abs(x)))
    if peak <= 1e-9:
        return x
    return x * (10.0 ** (db / 20.0) / peak)


def make_loop(x: np.ndarray, duration: float) -> np.ndarray:
    """Кроссфейд хвоста в начало: стык петли непрерывный по значению и ходу."""
    length = seconds(duration)
    seam = len(x) - length
    if seam <= 0:
        return x[:length]

    out = x[:length].copy()
    ramp = np.linspace(0.0, 1.0, seam, endpoint=False)
    out[:seam] = x[:seam] * ramp + x[length : length + seam] * (1.0 - ramp)
    return out


def write_wav(path: str, x: np.ndarray) -> None:
    data = np.clip(x, -1.0, 1.0)
    pcm = (data * 32767.0).astype("<i2")
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SR)
        handle.writeframes(pcm.tobytes())


# ------------------------------------------------------------------ клипы


def build_wind(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration + 0.4)
    time = np.arange(n) / SR

    low = band_noise(rng, n, 55.0, 420.0)
    high = band_noise(rng, n, 500.0, 1800.0)

    # Медленная модуляция и редкие порывы: ровный шум звучит как пылесос.
    swell = 0.55 + 0.45 * np.sin(2.0 * np.pi * 0.08 * time + 0.7)
    gust = np.clip(
        np.sin(2.0 * np.pi * 0.021 * time + 2.1) * 0.5 + 0.5, 0.0, 1.0
    ) ** 3
    gain = swell + 0.55 * gust

    mix = 0.85 * low + 0.35 * high
    return make_loop(normalize_peak(remove_dc(mix * gain)), duration)


def build_rail(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration + 0.4)
    time = np.arange(n) / SR

    rumble = band_noise(rng, n, 28.0, 180.0)
    low = np.sin(2.0 * np.pi * 38.0 * time) + 0.6 * np.sin(2.0 * np.pi * 76.5 * time)
    low *= 0.35 + 0.65 * np.clip(np.sin(2.0 * np.pi * 1.7 * time) ** 2, 0.0, 1.0)

    mix = 0.9 * rumble + 0.55 * low
    return make_loop(normalize_peak(remove_dc(mix)), duration)


def build_brakes(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration)
    time = np.arange(n) / SR

    screech = sweep(n, 2900.0, 1500.0) * envelope(n, 0.06, duration - 0.06, 1.6)
    hiss = band_noise(rng, n, 1800.0, 7000.0) * envelope(n, 0.1, duration - 0.1, 2.4)
    low = np.sin(2.0 * np.pi * 92.0 * time) * envelope(n, 0.2, duration - 0.2, 2.0)

    mix = 0.55 * screech + 0.8 * hiss + 0.35 * low
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_doors(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration)
    time = np.arange(n) / SR

    hiss = band_noise(rng, n, 900.0, 5200.0) * envelope(n, 0.12, 0.55, 2.2)
    click_at = seconds(0.86)
    click = np.zeros(n)
    ring = np.sin(2.0 * np.pi * 1750.0 * time) + 0.6 * np.sin(2.0 * np.pi * 2630.0 * time)
    click[click_at:] = ring[click_at:] * np.exp(
        -np.arange(n - click_at) / (0.035 * SR)
    )
    thud = np.sin(2.0 * np.pi * 68.0 * time) * envelope(n, 0.02, 0.3, 3.0)

    mix = 0.9 * hiss + 0.55 * click + 0.45 * thud
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_step_metal(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration)
    time = np.arange(n) / SR

    hit = band_noise(rng, n, 700.0, 5200.0) * envelope(n, 0.004, 0.16, 3.5)
    ring = (
        np.sin(2.0 * np.pi * 1180.0 * time)
        + 0.5 * np.sin(2.0 * np.pi * 2090.0 * time)
    ) * envelope(n, 0.004, 0.22, 4.0)
    thump = np.sin(2.0 * np.pi * 96.0 * time) * envelope(n, 0.006, 0.12, 3.0)

    mix = 0.55 * hit + 0.4 * ring + 0.5 * thump
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_step_snow(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration)
    time = np.arange(n) / SR

    crunch = band_noise(rng, n, 260.0, 2200.0) * envelope(n, 0.01, 0.2, 2.6)
    thump = np.sin(2.0 * np.pi * 74.0 * time) * envelope(n, 0.01, 0.16, 2.4)

    mix = 0.75 * crunch + 0.45 * thump
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_thud(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration)
    time = np.arange(n) / SR

    body = np.sin(2.0 * np.pi * 46.0 * time) * envelope(n, 0.01, 0.75, 2.6)
    body += 0.6 * np.sin(2.0 * np.pi * 71.0 * time) * envelope(n, 0.01, 0.5, 3.0)
    ring = band_noise(rng, n, 400.0, 2600.0) * envelope(n, 0.01, 0.9, 3.2)

    mix = 0.9 * body + 0.3 * ring
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_cart_hum(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration + 0.4)
    time = np.arange(n) / SR

    hum = 0.5 * np.sin(2.0 * np.pi * 82.0 * time)
    hum += 0.25 * np.sin(2.0 * np.pi * 123.0 * time + 0.4)
    whir = band_noise(rng, n, 180.0, 1400.0) * 0.35
    wobble = 0.85 + 0.15 * np.sin(2.0 * np.pi * 2.3 * time)

    return make_loop(normalize_peak(remove_dc((hum + whir) * wobble)), duration)


def build_drone(rng: np.random.Generator, duration: float) -> np.ndarray:
    n = seconds(duration + 0.4)
    time = np.arange(n) / SR

    low = np.sin(2.0 * np.pi * 36.0 * time)
    low += 0.7 * np.sin(2.0 * np.pi * 54.0 * time + 0.6)
    low += 0.25 * np.sin(2.0 * np.pi * 36.35 * time + 1.7)
    air = band_noise(rng, n, 120.0, 900.0) * 0.25
    breathe = 0.75 + 0.25 * np.sin(2.0 * np.pi * 0.09 * time + 1.2)

    return make_loop(normalize_peak(remove_dc((low + air) * breathe)), duration)


def build_crash_click(rng: np.random.Generator, duration: float) -> np.ndarray:
    """Короткое замыкание: треск, щелчок и гудение оставшегося тока.

    Клик нужен короткий и резкий — он ставит точку в момент вспышки. Атака
    короче 4 мс иначе «щелчок» превращается в «шлепок».
    """
    n = seconds(duration)
    time = np.arange(n) / SR

    crack = band_noise(rng, n, 1500.0, 8000.0) * envelope(n, 0.002, 0.09, 4.0)
    snap = (
        np.sin(2.0 * np.pi * 2400.0 * time)
        + 0.7 * np.sin(2.0 * np.pi * 3700.0 * time + 0.9)
    ) * envelope(n, 0.002, 0.05, 5.0)
    buzz = np.sin(2.0 * np.pi * 118.0 * time) * envelope(n, 0.004, 0.22, 2.2)
    body = band_noise(rng, n, 90.0, 620.0) * envelope(n, 0.003, 0.14, 3.0)

    mix = 0.85 * crack + 0.5 * snap + 0.35 * buzz + 0.4 * body
    return fade_edges(normalize_peak(remove_dc(mix)))


def build_crash_steam(rng: np.random.Generator, duration: float) -> np.ndarray:
    """Шипение пара из разбитой магистрали: средняя полоса и шорох клапана.

    Петля, потому что дым идёт всё оседание и тишину: обрывистый клип читался бы
    как «шипение закончилось», хотя дым в кадре остаётся.
    """
    n = seconds(duration + 0.4)
    time = np.arange(n) / SR

    hiss = band_noise(rng, n, 800.0, 3200.0)
    valve = band_noise(rng, n, 1900.0, 2700.0)
    # Медленное дыхание: пар то сильнее, то слабее — ровный шум звучит как фен.
    breathe = 0.72 + 0.28 * np.sin(2.0 * np.pi * 0.31 * time + 0.5)
    puff = np.clip(np.sin(2.0 * np.pi * 0.13 * time) * 0.5 + 0.5, 0.0, 1.0) ** 2

    # Свист — полосовой шум, а не тон: детерминированный синус на 2,1 кГц даёт
    # большой шаг между соседними отсчётами и рвёт стык петли по проверке.
    mix = 0.9 * hiss * breathe + 0.35 * valve * (0.6 + puff)
    return make_loop(normalize_peak(remove_dc(mix)), duration)


def build_crash_thud(rng: np.random.Generator, duration: float) -> np.ndarray:
    """Тяжёлый удар металла о металл: корпус вагона принимает удар на себя.

    Отличается от stop_thud длинным лязгом: stop_thud — остановка состава,
    этот — удар, и после него корпус должен «звенеть».
    """
    n = seconds(duration)
    time = np.arange(n) / SR

    body = np.sin(2.0 * np.pi * 38.0 * time) * envelope(n, 0.008, 0.9, 2.4)
    body += 0.7 * np.sin(2.0 * np.pi * 57.0 * time + 0.4) * envelope(n, 0.008, 0.6, 2.8)
    clang = band_noise(rng, n, 320.0, 3400.0) * envelope(n, 0.006, 1.1, 3.0)
    ring = (
        np.sin(2.0 * np.pi * 214.0 * time)
        + 0.5 * np.sin(2.0 * np.pi * 486.0 * time + 1.3)
    ) * envelope(n, 0.01, 1.3, 3.4)

    mix = 0.95 * body + 0.55 * clang + 0.3 * ring
    return fade_edges(normalize_peak(remove_dc(mix)))


LIBRARY = {
    "wind_loop": (12.0, True, build_wind),
    "rail_rumble_loop": (10.0, True, build_rail),
    "brakes": (2.2, False, build_brakes),
    "doors": (1.4, False, build_doors),
    "step_metal": (0.5, False, build_step_metal),
    "step_snow": (0.5, False, build_step_snow),
    "stop_thud": (1.6, False, build_thud),
    "cart_hum_loop": (6.0, True, build_cart_hum),
    "title_drone_loop": (8.0, True, build_drone),
    "crash_click": (0.35, False, build_crash_click),
    "crash_steam_loop": (4.0, True, build_crash_steam),
    "crash_thud": (1.8, False, build_crash_thud),
}


# ------------------------------------------------------------------ сборка и проверка


def build(name: str, force: bool) -> bool:
    duration, is_loop, make = LIBRARY[name]
    path = os.path.join(OUT, name + ".wav")

    if os.path.exists(path) and not force:
        print(f"  {name}: уже есть (--force для пересборки)")
        return True

    # Своё зерно на каждый клип: пересборка одного не меняет остальные.
    rng = np.random.default_rng(SEED + sum(ord(c) for c in name))
    samples = make(rng, duration)
    if len(samples) > seconds(duration):
        samples = samples[: seconds(duration)]

    write_wav(path, samples)
    print(f"  {name}: {os.path.getsize(path) / 1024.0:7.1f} КБ")
    return True


def read_wav(path: str):
    with wave.open(path, "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        frames = handle.getnframes()
        raw = handle.readframes(frames)

    data = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    return channels, width, rate, frames, data


def check() -> int:
    failures = 0
    print(f"{'клип':<20}{'длит':>7}{'пик dB':>9}{'rms':>8}{'dc':>9}{'петля':>8}{'КБ':>8}")
    print("-" * 69)

    for name, (duration, is_loop, _) in LIBRARY.items():
        path = os.path.join(OUT, name + ".wav")
        if not os.path.exists(path):
            print(f"{name:<20}  НЕТ ФАЙЛА")
            failures += 1
            continue

        channels, width, rate, frames, data = read_wav(path)
        actual = frames / float(rate) if rate else 0.0
        peak = float(np.max(np.abs(data))) if len(data) else 0.0
        peak_db = 20.0 * np.log10(max(peak, 1e-9))
        rms = float(np.sqrt(np.mean(data ** 2))) if len(data) else 0.0
        dc = float(np.mean(data)) if len(data) else 0.0

        seam = 0.0
        if is_loop and len(data) > 2:
            seam = abs(float(data[0] - data[-1]))

        problems = []
        if channels != 1:
            problems.append("не моно")
        if width != 2:
            problems.append("не 16-bit")
        if rate != SR:
            problems.append(f"частота {rate}")
        if abs(actual - duration) > max(0.02, duration * 0.01):
            problems.append(f"длительность {actual:0.2f}")
        if peak_db > -0.5:
            problems.append(f"клиппинг {peak_db:0.1f} dB")
        if abs(dc) > 0.001:
            problems.append(f"dc {dc:0.4f}")
        if is_loop and seam > 0.05:
            problems.append(f"стык {seam:0.3f}")

        if problems:
            failures += 1

        loop_mark = f"{seam:0.3f}" if is_loop else "-"
        size_kb = os.path.getsize(path) / 1024.0
        print(
            f"{name:<20}{actual:7.2f}{peak_db:9.1f}{rms:8.3f}{dc:9.4f}"
            f"{loop_mark:>8}{size_kb:8.1f}"
        )
        if problems:
            print(f"  !! {name}: {', '.join(problems)}")

    print("-" * 69)
    if failures:
        print(f"ОШИБОК: {failures}")
        return 1
    print("Все клипы прошли проверку.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Сборка звуков прибытия в депо")
    parser.add_argument("--all", action="store_true", help="собрать все клипы")
    parser.add_argument(
        "--only", nargs="+", default=None, help="собрать только перечисленные"
    )
    parser.add_argument("--force", action="store_true", help="перезаписать готовые")
    parser.add_argument("--check", action="store_true", help="проверить WAV-файлы")
    parser.add_argument("--list", action="store_true", help="показать список клипов")
    args = parser.parse_args()

    if args.list:
        for name, (duration, is_loop, _) in LIBRARY.items():
            kind = "петля" if is_loop else "один раз"
            print(f"  {name:<20}{duration:5.1f} с   {kind}")
        return 0

    if args.check:
        return check()

    names = list(LIBRARY) if args.all else args.only
    if not names:
        parser.print_help()
        return 1

    unknown = [name for name in names if name not in LIBRARY]
    if unknown:
        print("Неизвестные клипы: " + ", ".join(unknown))
        return 1

    os.makedirs(OUT, exist_ok=True)
    for name in names:
        build(name, args.force)

    print(f"\nГотово: {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
