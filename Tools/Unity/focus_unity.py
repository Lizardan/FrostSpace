"""Вернуть окну Unity фокус: без него Play Mode не тикает (docs/UNITY_EDITOR.md §5).

    python Tools/Unity/focus_unity.py            # вернуть фокус
    python Tools/Unity/focus_unity.py --check    # только сказать, кто сейчас в фокусе

Приём из документации: Windows блокирует `SetForegroundWindow` из фонового
процесса, поэтому сначала посылаем ALT (`keybd_event`), потом вызываем
`SetForegroundWindow`, и проверяем результат через `GetForegroundWindow`.
"""
from __future__ import annotations

import ctypes
import subprocess
import sys
from ctypes import wintypes

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32
VK_MENU = 0x12
KEYEVENTF_EXTENDEDKEY = 0x0001
KEYEVENTF_KEYUP = 0x0002

EnumWindowsProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)


def pids_of(image: str) -> set[int]:
    """PID запущенных процессов с таким именем образа (без `tasklist`-парсинга)."""
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {image}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, check=False).stdout
    pids = set()
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and parts[0].lower() == image.lower():
            try:
                pids.add(int(parts[1]))
            except ValueError:
                pass
    return pids


def windows_of(pids: set[int]) -> list[tuple[int, str]]:
    found: list[tuple[int, str]] = []

    def cb(hwnd, _lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value not in pids:
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        if buf.value:
            found.append((int(hwnd), buf.value))
        return True

    proc = EnumWindowsProc(cb)        # держим ссылку: на стек-переменной enum не полагается
    user32.EnumWindows(proc, 0)
    return found


def foreground_title() -> str:
    hwnd = user32.GetForegroundWindow()
    n = user32.GetWindowTextLengthW(hwnd)
    buf = ctypes.create_unicode_buffer(n + 1)
    user32.GetWindowTextW(hwnd, buf, n + 1)
    return "%d %s" % (hwnd, buf.value)


def main(argv: list[str]) -> int:
    pids = pids_of("Unity.exe")
    if not pids:
        print("Unity.exe не запущен")
        return 1
    wins = [(h, t) for (h, t) in windows_of(pids) if t]
    if not wins:
        print("видимых окон Unity нет; PID: %s" % sorted(pids))
        return 1
    if "--check" in argv:
        print("в фокусе: " + foreground_title())
        for (h, t) in wins:
            print("  окно %d: %s" % (h, t))
        return 0
    # Главное окно — самое длинное с «Unity» в заголовке, остальные — панели.
    wins.sort(key=lambda w: (0 if "Unity" in w[1] else 1, -len(w[1])))
    hwnd = wins[0][0]
    user32.ShowWindow(hwnd, 9)          # SW_RESTORE: убрать свёрнутость, если была
    user32.keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, 0)
    user32.keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0)
    user32.SetForegroundWindow(hwnd)
    user32.SwitchToThisWindow(hwnd, True)
    now = foreground_title()
    print("цель %d (%s)" % (hwnd, wins[0][1]))
    print("в фокусе: " + now)
    return 0 if now.startswith(str(hwnd)) else 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
