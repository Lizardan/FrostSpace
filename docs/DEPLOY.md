# Публикация WebGL на GitHub Pages

Документ отвечает на вопрос «как выложить игру в веб». Сборка и тесты — `DEVELOPMENT.md`;
контракт ассетов — `ASSETS.md`.

**Страница:** https://lizardan.github.io/FrostSpace/
**Репозиторий:** https://github.com/Lizardan/FrostSpace (публичный)

## Как это устроено

```
git push в main
   └─ GitHub Actions (.github/workflows/webgl.yml)
        ├─ job build  → ubuntu-24.04 → game-ci/unity-builder@v6 (CLI v0.1.72)
        │                 └─ контейнер unityci/editor + BuildWebGL.PerformBuild → Builds/WebGL
        └─ job deploy → actions/deploy-pages → https://lizardan.github.io/FrostSpace/
```

- **Сборка идёт в облаке GitHub**, на `ubuntu-24.04` (не `ubuntu-latest`: с 2026-10-19
  эта метка переезжает на Ubuntu 26, а переезд под `unityci/editor` не проверен).
  Unity ставится в контейнер `unityci/editor` и активируется Personal-лицензией
  по email/паролю из секретов.
- **Self-hosted раннер больше не используется.** Раньше сборка шла на этом ПК, и
  пуш в `main` без запущенного раннера просто вставал в очередь. Папка
  `F:\actions-runner-frostspace` удалена, раннер `frostspace-pc` снят с GitHub.
- **Свой `buildMethod` обязателен:** `FrostSpace.Editor.BuildWebGL.PerformBuild`
  выставляет `PlayerSettings.WebGL` — Brotli + `decompressionFallback` и шаблон
  `PROJECT:FrostSpace`. Без `decompressionFallback` браузер не распакует `.unityweb`:
  GitHub Pages не отдаёт `Content-Encoding`.
- **Контракт вывода — часть контракта с game-ci.** CLI проверяет stdout Unity и
  считает сборку успешной только при строке `Build succeeded!` или блоке
  `# Build results #` с `Errors: N`. Свой buildMethod заменяет штатный скрипт
  билдера, поэтому маркеры печатает сам `BuildWebGL` (`ReportSummary`,
  `ReportResult`). Убрать их — значит получить зелёную сборку, которую CI
  объявит упавшей.
- Cloudflare Pages не подходит: он не умеет собирать Unity, только хостить готовое.

## Секреты репозитория (Personal-лицензия)

Два секрета, оба в `Settings → Secrets and variables → Actions`:

| Секрет | Что лежит |
|---|---|
| `UNITY_EMAIL` | почта Unity ID |
| `UNITY_PASSWORD` | пароль Unity ID |

`UNITY_LICENSE` (содержимое `.ulf`) в облаке **не нужен и не работает**: файл
привязан к машине, на которой выпущен, и в Linux-контейнере даёт
`Machine bindings don't match` — game-ci откатывается на активацию по паре
email/пароль. Держать секрет стоит только если сборка когда-нибудь вернётся на
Windows-раннер.

Как выпустить `.ulf`, если он всё-таки понадобится:

1. **Unity Hub → Preferences (шестерёнка) → Licenses → Add → «Get a free personal license».**
   Именно кнопка `Add` создаёт файл. Обычный вход в Hub активирует редактор, но
   `.ulf` **не** пишет — это и была причина, по которой раньше казалось, что
   «Unity 6 не пишет `.ulf`».
2. Файл появляется в `C:\ProgramData\Unity\Unity_lic.ulf`.
3. Залить, не печатая значение в терминал:

```bash
gh secret set UNITY_LICENSE -R Lizardan/FrostSpace < /c/ProgramData/Unity/Unity_lic.ulf
gh secret set UNITY_EMAIL   -R Lizardan/FrostSpace      # спросит интерактивно
gh secret set UNITY_PASSWORD -R Lizardan/FrostSpace     # спросит интерактивно
gh secret list -R Lizardan/FrostSpace                   # покажет только имена
```

Правила:

- Значение `.ulf` — секрет. Не вставлять в чат, коммит, issue, лог. Если засветилось —
  отозвать на `id.unity.com` и выпустить заново.
- Пароль Unity с спецсимволами может не пройти активацию (известная проблема game-ci):
  лучше пароль из букв разных регистров и цифр.
- GitHub не отдаёт значение секрета ни через UI, ни через API — только перезапись.
  Скопировать секрет «из соседнего репозитория» нельзя.

## Рабочий цикл деплоя

```bash
git add -A && git commit -m "..." && git push origin main
gh run list --limit 1
gh run watch <run-id> --exit-status      # ждёт конца, код 1 при падении
```

Проверить страницу:

```bash
curl -s https://lizardan.github.io/FrostSpace/ | grep -o "<title>[^<]*</title>"
# <title>FrostSpace</title>
```

HTML кешируется GitHub Pages до 10 минут — в браузере может понадобиться Ctrl+F5.

Пересобрать без изменений: `gh workflow run webgl.yml`.

## Однократная настройка (новый репозиторий)

1. **Pages включить на сборку из workflow:**

   ```bash
   gh api -X POST repos/Lizardan/FrostSpace/pages -f build_type=workflow
   ```

2. **Два секрета** из раздела выше.
3. **Файлы в репозитории:** `.github/workflows/webgl.yml`,
   `Assets/_Project/Editor/BuildWebGL.cs`, `Assets/WebGLTemplates/FrostSpace/index.html`.

Локальный Unity для деплоя больше не нужен — он нужен только для разработки.

## Ловушки

| Симптом | Причина | Что делать |
|---|---|---|
| Сборка падает на «pull image» | для `6000.6.6f1` ещё нет образа `unityci/editor` | проверить тег образа; при необходимости задать `customImage` или вернуть self-hosted |
| `Build succeeded`, но job красный: «There was an error building the project» | CLI game-ci не нашёл в stdout маркеров успеха | печатать `Build succeeded!` и блок `# Build results #` из `BuildWebGL` (уже делает); сверить формат с `StdOutReporter.cs` в game-ci/cli |
| Exit code 137 через пару секунд после старта билда | OOM: `Library` восстановлен из кэша другого коммита | кэш `Library` не подключать (сейчас его нет намеренно) |
| Предупреждение «Machine bindings don't match» | в секретах лежит `.ulf` от другого компьютера | убрать `UNITY_LICENSE`, активация идёт по email/паролю |
| `Working tree is dirty` | проверка билдера | в workflow уже стоит `allowDirtyBuild: true` |
| Артефакт не читается, права root | файлы из контейнера принадлежат root | добавить в билдер `chownFilesTo` / `runAsHostUser` |
| Каждая сборка долгая (~16 минут) | кэша импорта нет | так и задумано: холодный импорт воспроизводим; кэш только с точным ключом и без `restore-keys` |
| Страница отдаёт битый `.unityweb` | нет `decompressionFallback` | он выставляется в `BuildWebGL.PerformBuild` — не убирать |
| Канвас маленький, внизу кнопки Unity | штатный шаблон Default | свой шаблон `Assets/WebGLTemplates/FrostSpace` |
| Страница старая | кеш GitHub Pages | Ctrl+F5, подождать до 10 минут |
| Локальная сборка падает на `UnityLockfile` | открыт редактор | закрыть Unity или собирать в отдельном клоне |

## Если придётся вернуть self-hosted

Только если облако не заведётся. Порядок:

```bash
mkdir -p /f/actions-runner-frostspace && cd /f/actions-runner-frostspace
curl -sL -o runner.zip https://github.com/actions/runner/releases/download/v2.338.0/actions-runner-win-x64-2.338.0.zip
python -c "import zipfile; zipfile.ZipFile('runner.zip').extractall('.')" && rm runner.zip
TOKEN=$(gh api -X POST repos/Lizardan/FrostSpace/actions/runners/registration-token --jq .token)
cmd //c "config.cmd --unattended --url https://github.com/Lizardan/FrostSpace --token $TOKEN --name frostspace-pc --work _work --labels unity"
```

Метки `self-hosted`, `Windows`, `X64` добавляются сами; `unity` — наша. В workflow
нужно вернуть `runs-on: [self-hosted, Windows, X64, unity]` и PowerShell-ожидание
`Builds/webgl-result.json` (`Unity.exe` на Windows — лончер, он выходит с кодом 1,
а редактор работает дальше).

Снять регистрацию:

```bash
gh api repos/Lizardan/FrostSpace/actions/runners --jq '.runners[] | "\(.id) \(.name)"'
gh api -X DELETE repos/Lizardan/FrostSpace/actions/runners/<id>
```
