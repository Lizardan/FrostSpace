# FrostSpace

Unity-проект на ранней стадии: есть играбельный вертикальный срез — снежный уровень
`SnowLevel`, двадцать роботов из Blender и локомоция игрока. Назначение игры, жанр и целевые
платформы пока не зафиксированы — см. [docs/PROJECT_OVERVIEW.md](docs/PROJECT_OVERVIEW.md).

**Играбельная WebGL-сборка:** https://lizardan.github.io/FrostSpace/ — собирается и
публикуется автоматически после пуша в `main` (см. `docs/DEVELOPMENT.md`).

| Параметр | Значение |
|---|---|
| Unity | 6000.6.6f1 |
| Render pipeline | URP 17.6.0 |
| Ввод | Input System |
| Стартовая сцена | `Assets/Scenes/Boot.unity` (build index 0); игровой уровень — `SnowLevel` (1) |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model`, движение kinematic |
| Код и тесты | `FrostSpace.Features` + `FrostSpace.Editor`, 17 EditMode-тестов |

## С чего начать

1. [AGENTS.md](AGENTS.md) — обязательные правила для человека и AI-агента.
2. [docs/README.md](docs/README.md) — индекс документации и порядок чтения.
3. [docs/PROJECT_MAP.md](docs/PROJECT_MAP.md) — где что лежит.
4. [Assets/.Aura/plans/README.md](Assets/.Aura/plans/README.md) — как ведётся текущий план работ.

## Инструменты

- [docs/BLENDER.md](docs/BLENDER.md) — Blender 5.2: headless-сборка, GUI-MCP, грабли.
- [docs/UNITY_MCP.md](docs/UNITY_MCP.md) — проверенный порядок работы с Unity MCP.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — окружение, сборка, тесты.

## Требования

- Unity **6000.6.6f1** (редактор установлен в `F:\Unity\Editor`).
- Blender **5.2.2 LTS** — только если работаешь с 3D-ассетами; для Unity-разработки
  он не обязателен.
- Python 3.11+ — для утилит в `Tools/` (запускаются вне Unity).
