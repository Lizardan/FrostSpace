# FrostSpace

Unity-проект на ранней стадии: есть играбельный вертикальный срез — снежный уровень
`Game`, двадцать роботов и 13 робо-животных из Blender, локомоция игрока, сканирование
на Q (сонар, подписи ближайшего объекта и цели в прицеле), пауза с настройками, главное
меню с 3D-диорамой и sci-fi экран загрузки. Назначение игры, жанр и целевые
платформы пока не зафиксированы — см. [docs/PROJECT_OVERVIEW.md](docs/PROJECT_OVERVIEW.md).

**Играбельная WebGL-сборка:** https://lizardan.github.io/FrostSpace/ — собирается и
публикуется автоматически после пуша в `main` (см. `docs/DEPLOY.md`).

| Параметр | Значение |
|---|---|
| Unity | 6000.6.7f1 |
| Render pipeline | URP 17.6.0 |
| Ввод | Input System |
| Сцены | `Boot` (build index 0) → `MainMenu` (1) → `Game` (2) |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model`, движение kinematic |
| Робо-животные | 13 FBX (`RA_<Species>`), визуальные префабы `RA_<Species>_Model` |
| Код и тесты | `FrostSpace.Core` + `FrostSpace.Features` + `FrostSpace.Editor`, 47 EditMode-тестов |

## С чего начать

1. [AGENTS.md](AGENTS.md) — обязательные правила для человека и AI-агента.
2. [docs/README.md](docs/README.md) — индекс документации и порядок чтения.
3. [docs/PROJECT_MAP.md](docs/PROJECT_MAP.md) — где что лежит.
4. [Assets/.Aura/plans/README.md](Assets/.Aura/plans/README.md) — как ведётся текущий план работ.

## Инструменты

- [docs/BLENDER.md](docs/BLENDER.md) — Blender 5.2: headless-сборка, GUI-MCP, грабли.
- [docs/UNITY_MCP.md](docs/UNITY_MCP.md) — проверенный порядок работы с Unity MCP.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — окружение, сборка, тесты.
- [docs/DEPLOY.md](docs/DEPLOY.md) — публикация WebGL на GitHub Pages.

## Скиллы

Инструкции по темам лежат в `.Aura/Skills/` (34 скилла Unity + проектный `plans`) и
подключены ко всем агентам: opencode — через `opencode.json`, остальные — через
`AGENTS.md`, MiniMax Code — через junction'и в глобальном каталоге, `.agents/skills/` —
мост открытого стандарта Agent Skills. Настройка мостов —
`Tools/Agent/LinkAgentSkills.ps1`, список применимых скиллов — в [AGENTS.md](AGENTS.md).

## Требования

- Unity **6000.6.7f1** (редактор установлен в `F:\Unity\Editor`).
- Blender **5.2.3 LTS** — только если работаешь с 3D-ассетами; для Unity-разработки
  он не обязателен. Все ассеты собираются единым входом
  `python Tools/Blender/build_all.py` (см. [docs/BLENDER.md](docs/BLENDER.md)).
- Python 3.11+ — для утилит в `Tools/` (запускаются вне Unity).
