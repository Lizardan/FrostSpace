# FrostSpace

Unity-проект на ранней стадии: есть играбельный вертикальный срез — снежный уровень
`Game`, двадцать роботов и 13 робо-животных из Blender, локомоция игрока, сканирование
на Q (сонар, подписи ближайшего объекта и цели в прицеле), диалоги с роботами на E
(ветки ответов, остановка патруля), пауза с настройками, главное меню с 3D-диорамой
и sci-fi экран загрузки.

Сюжет зафиксирован как замысел: робот-курьер аккумуляторов едет в поезде, берёт квест
у напарника и прибывает в депо — [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md); мир,
холод и Тишина — [docs/WORLD.md](docs/WORLD.md). Поток сцен —
`Boot → MainMenu → CharacterSetup → Train → Frontier`. Выбор корпуса реализован
(`CharacterSetup`, три пресета), в `Train` есть интерьер вагона, переноска, квест
«Погрузка», диалоги R13 и финал с отъездом камеры и титулом; в `Frontier` поезд того же
масштаба въезжает в депо, открывает боковые створки и выпускает R13 с тележкой, затем
идут охрана, встреча с R7 и выдача карты со сканером (`DP-01`…`DP-04`). Прогресс квеста
сохраняется в один ключ `FrostSpace.Save`, в меню есть `ПРОДОЛЖИТЬ` (ADR 0031). Меню,
пауза, журнал и квест русифицированы; декоративные подписи остались латиницей. Внизу экрана — полоса опыта с текущим и следующим уровнем.

**Играбельная WebGL-сборка:** https://lizardan.github.io/FrostSpace/ — собирается и
публикуется автоматически после пуша в `main` (см. `docs/DEPLOY.md`).

| Параметр | Значение |
|---|---|
| Unity | 6000.6.8f1 |
| Render pipeline | URP 17.6.0 |
| Ввод | Input System |
| Сцены | Факт: `Boot` (0) → `MainMenu` (1) → `CharacterSetup` (2) → `Train` (3, интерьер, переноска, квест и финал) → `Frontier` (4, депо: прибытие поезда, выход R13, охрана, R7 и снаряжение) → `Game` (5, вне потока); целевой поток по ADR 0026: `Boot → MainMenu → CharacterSetup → Train → Frontier` |
| Роботы | 20 FBX (R1–R20), визуальные префабы `R*_Model`, движение kinematic |
| Робо-животные | 13 FBX (`RA_<Species>`), визуальные префабы `RA_<Species>_Model` |
| Мир Stage 3 | раскладка 4,4×4,4 км из 121 клетки по 400 м числами в `Tools/Blender/world_layout.py` (18 инвариантов), восемь руин 15–60 м и якоря 320/260 м, небо почти чёрное с редкими звёздами и одной аномалией — ADR 0036 |
| Код и тесты | `FrostSpace.Core` + `FrostSpace.Features` (17 фич, включая `Map` с раскладкой мира и `Inventory` с окном экипировки) + `FrostSpace.Editor`, 267 EditMode-тестов |

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

- Unity **6000.6.8f1** (редактор установлен в `F:\Unity\Editor`).
- Blender **5.2.3 LTS** — только если работаешь с 3D-ассетами; для Unity-разработки
  он не обязателен. Все ассеты собираются единым входом
  `python Tools/Blender/build_all.py` (см. [docs/BLENDER.md](docs/BLENDER.md)).
- Python 3.11+ — для утилит в `Tools/` (запускаются вне Unity).
