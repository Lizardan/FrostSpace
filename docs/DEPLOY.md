# Публикация WebGL на GitHub Pages

Документ отвечает на вопрос «как выложить игру в веб». Сборка и тесты — `DEVELOPMENT.md`;
контракт ассетов — `ASSETS.md`.

**Страница:** https://lizardan.github.io/FrostSpace/
**Репозиторий:** https://github.com/Lizardan/FrostSpace (публичный)

## Как это устроено

```
git push в main
   └─ GitHub Actions (.github/workflows/webgl.yml)
        ├─ job build  → self-hosted раннер этого ПК → Unity 6000.6.6f1 (batch) → Builds/WebGL
        └─ job deploy → actions/deploy-pages → https://lizardan.github.io/FrostSpace/
```

- **Сборка идёт на этой машине**, а не в облаке: Unity 6 не пишет `.ulf`, который
  требует game-ci для Personal-лицензии, а локальный редактор уже активирован.
  Побочное следствие: **пока раннер не запущен, пуш ставит job в очередь и ничего
  не собирается**.
- **Раннер не держим постоянно.** Поднимаем перед деплоем, гасим после — чтобы не
  висел фоновый процесс. Если раннер не нужен, его можно вообще не запускать.
- Cloudflare Pages не подходит: он не умеет собирать Unity, только хостить готовое.

## Рабочий цикл деплоя (делать каждый раз)

### 1. Поднять раннер (без окна)

```bash
powershell -NoProfile -Command "Start-Process -FilePath 'F:\actions-runner-frostspace\bin\Runner.Listener.exe' -ArgumentList 'run' -WorkingDirectory 'F:\actions-runner-frostspace' -WindowStyle Hidden"
```

Проверка, что он вышел на связь (10–20 секунд):

```bash
gh api repos/Lizardan/FrostSpace/actions/runners --jq '.runners[] | "\(.name) \(.status)"'
# frostspace-pc online
```

### 2. Закоммитить и запушить

```bash
git add -A && git commit -m "..." && git push origin main
```

Пуш в `main` сам запускает workflow. Отдельная команда `gh workflow run webgl.yml`
нужна, только если надо пересобрать без изменений.

### 3. Дождаться сборки

```bash
gh run list --limit 1
gh run watch <run-id> --exit-status      # ждёт конца, код 1 при падении
```

Полная сборка с нуля — ~12 минут, инкрементальная — 2–3 минуты.

### 4. Проверить страницу

```bash
curl -s https://lizardan.github.io/FrostSpace/ | grep -o "<title>[^<]*</title>"
# <title>FrostSpace</title>
```

HTML кешируется GitHub Pages до 10 минут — в браузере может понадобиться Ctrl+F5.

### 5. Погасить раннер (обязательно)

```bash
powershell -NoProfile -Command "Get-Process Runner.Listener -ErrorAction SilentlyContinue | Stop-Process -Force"
```

Проверка, что процесса нет:

```bash
tasklist /fi "imagename eq Runner.Listener.exe"     # «Задачи… отсутствуют»
```

Через минуту GitHub тоже покажет `offline` — это нормально.

## Однократная настройка (новая машина или новый репозиторий)

1. **Unity** с модулем WebGL (`Data/PlaybackEngines/WebGLSupport`), путь к редактору —
   `F:\Unity\Editor\6000.6.6f1\Editor\Unity.exe` (см. `AGENTS.md`).
2. **`gh` авторизован** в аккаунте владельца репозитория (`gh auth status`).
3. **Раннер:**

   ```bash
   mkdir -p /f/actions-runner-frostspace && cd /f/actions-runner-frostspace
   curl -sL -o runner.zip https://github.com/actions/runner/releases/download/v2.338.0/actions-runner-win-x64-2.338.0.zip
   python -c "import zipfile; zipfile.ZipFile('runner.zip').extractall('.')" && rm runner.zip
   TOKEN=$(gh api -X POST repos/Lizardan/FrostSpace/actions/runners/registration-token --jq .token)
   cmd //c "config.cmd --unattended --replace --url https://github.com/Lizardan/FrostSpace --token $TOKEN --name frostspace-pc --work _work --labels unity"
   ```

   Метки по умолчанию (`self-hosted`, `Windows`, `X64`) добавляются сами; `unity` —
   наша метка, её ждёт workflow.

4. **Pages включить на сборку из workflow:**

   ```bash
   gh api -X POST repos/Lizardan/FrostSpace/pages -f build_type=workflow
   ```

5. **Файлы в репозитории:** `.github/workflows/webgl.yml`,
   `Assets/_Project/Editor/BuildWebGL.cs`, `Assets/WebGLTemplates/FrostSpace/index.html`.

## Ловушки, на которых уже спотыкались

| Симптом | Причина | Что делать |
|---|---|---|
| Job висит в очереди, сборки нет | раннер не запущен | поднять раннер (шаг 1) |
| `Runner connect error: Conflict` | старая сессия раннера ещё жива на GitHub | убить процесс, подождать ~1 мин, запустить снова |
| Сборка «упала» сразу, лог обрывается на старте | `Unity.exe` — лончер, выходит с кодом 1 | workflow ждёт `Builds/webgl-result.json`, а не код возврата |
| Каждая сборка 11–12 минут | `actions/checkout` вычистил `Library/` | в workflow стоит `clean: false` — не убирать |
| Ошибки `com.unity.collab-proxy` | битый `Library/PackageCache` после отмены прогона | удалить `Library` и `Temp` в `F:\actions-runner-frostspace\_work\FrostSpace\FrostSpace` |
| Страница старая | кеш GitHub Pages | Ctrl+F5, подождать до 10 минут |
| Канвас маленький, внизу кнопки Unity | штатный шаблон Default | свой шаблон `Assets/WebGLTemplates/FrostSpace` (канвас на всё окно) |
| Локальная сборка падает на `UnityLockfile` | открыт редактор | закрыть Unity или собирать в отдельном клоне (как делает раннер) |

**Не отменяй прогон вручную** — отмена убивает Unity посреди очистки и ломает кэш
пакетов (см. строку про `collab-proxy`).

## Если нужна сборка на любой пуш без агента

Тогда раннер должен работать постоянно. Автозапуск без окна — файл
`FrostSpace WebGL Runner.vbs` в папке
`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`:

```vbs
Set shell = CreateObject("WScript.Shell")
shell.CurrentDirectory = "F:\actions-runner-frostspace"
shell.Run "cmd /c run.cmd > runner.log 2>&1", 0, False
```

Минус: процесс висит в фоне всегда. По умолчанию так **не** делаем — поднимаем и
гасим вручную (см. рабочий цикл).
