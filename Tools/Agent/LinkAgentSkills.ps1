<#
.SYNOPSIS
    Настраивает доступ к скиллам FrostSpace для всех агентов, не копируя файлы.

.DESCRIPTION
    Единый источник скиллов проекта — `.Aura/Skills` (его читает Aura).
    Чтобы другие рантаймы нашли те же файлы, скрипт создаёт Windows-junction'ы
    (это ссылка на каталог, а не копия: файлы физически хранятся в одном месте).

    Создаёт junction в репозитории:
      .agents\skills -> .Aura\Skills   (стандарт Agent Skills: Codex, Gemini CLI,
                                        Cursor, Kiro, opencode, Antigravity)

    OpenCode дополнительно получает `opencode.json` в корне репозитория:
    там прописан `skills.paths: ["./.Aura/Skills"]` — путь указывает прямо на
    канон, поэтому opencode работает без junction'ов (например, на машине
    коллеги, где скрипт не запускали). Файл в git, junction'ы — нет.

    Опционально (-Claude) — junction для Claude Code:
      .claude\skills -> .Aura\Skills
    Claude Code читает только свой каталог, другие рантаймы — нет.

    Опционально (-Global) — junction'ы в каталоге скиллов MiniMax Code:
      %USERPROFILE%\.minimax\skills\<имя> -> .Aura\Skills\<имя>
      (у MiniMax Code нет project-scope: только глобальный и agent-private каталог)

    Каталоги-мосты перечислены в .gitignore, поэтому git не видит файлы дважды.

.PARAMETER Global
    Также создать junction'ы в каталоге скиллов MiniMax Code (виден во всех
    проектах этого компьютера).

.PARAMETER Claude
    Также создать junction `.claude\skills` — только если используется Claude Code.

.PARAMETER RemoveClaude
    Убрать junction `.claude\skills` (и пустую папку `.claude`), если он есть.
    Цель `.Aura\Skills` при этом не затрагивается.

.PARAMETER DataDir
    Каталог данных MiniMax Code. По умолчанию — %USERPROFILE%\.minimax.

.PARAMETER Check
    Только проверить текущее состояние, ничего не создавать.

.EXAMPLE
    pwsh -File Tools/Agent/LinkAgentSkills.ps1 -Global
#>

[CmdletBinding()]
param(
    [switch]$Global,
    [switch]$Claude,
    [switch]$RemoveClaude,
    [string]$DataDir = (Join-Path $env:USERPROFILE '.minimax'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$skillsRoot = Join-Path $repoRoot '.Aura\Skills'

if (-not (Test-Path $skillsRoot)) {
    Write-Error "Каталог скиллов не найден: $skillsRoot"
    exit 1
}

$skillDirs = @(Get-ChildItem $skillsRoot -Directory | Sort-Object Name)
Write-Host "Источник скиллов: $skillsRoot ($($skillDirs.Count) шт.)" -ForegroundColor Cyan

function Test-Junction {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    $item = Get-Item $Path -Force
    if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
        return $item.Target
    }
    return $null
}

function Resolve-Target {
    param([string]$Target)
    # Junction хранит абсолютный путь; сравниваем по полному пути без слетча.
    return [System.IO.Path]::GetFullPath($Target).TrimEnd('\')
}

function Ensure-Junction {
    param(
        [string]$Link,
        [string]$Target
    )

    $targetFull = Resolve-Target $Target
    $existing = Test-Junction -Path $Link

    if ($existing) {
        if ((Resolve-Target $existing) -eq $targetFull) {
            Write-Host "  ок       $Link" -ForegroundColor DarkGray
            return $true
        }
        Write-Host "  обновляю $Link (цель изменилась)" -ForegroundColor Yellow
        Remove-Item -LiteralPath $Link -Force
    }
    elseif (Test-Path $Link) {
        Write-Host "  пропуск  $Link — обычный каталог, не трогаю" -ForegroundColor Yellow
        return $false
    }

    $parent = Split-Path -Parent $Link
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    New-Item -ItemType Junction -Path $Link -Target $targetFull | Out-Null
    Write-Host "  создано  $Link -> $targetFull" -ForegroundColor Green
    return $true
}

function Remove-Junction {
    param([string]$Link)

    if (-not (Test-Path $Link)) {
        Write-Host "  нет      $Link" -ForegroundColor DarkGray
        return
    }

    # Junction снимаем через fsutil: обычное удаление каталога заходит внутрь
    # ссылки и снесло бы .Aura\Skills вместе с ним.
    $existing = Test-Junction -Path $Link
    if ($existing) {
        fsutil reparsepoint delete $Link | Out-Null
        Write-Host "  снят reparse-point $Link" -ForegroundColor Yellow
    }

    if (@(Get-ChildItem $Link -Force -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-Host "  пропуск  $Link — каталог не пуст, не трогаю" -ForegroundColor Yellow
        return
    }

    Remove-Item -LiteralPath $Link -Force
    Write-Host "  удалён   $Link" -ForegroundColor Green

    $parent = Split-Path -Parent $Link
    if ((Test-Path $parent) -and
        @(Get-ChildItem $parent -Force -ErrorAction SilentlyContinue).Count -eq 0) {
        Remove-Item -LiteralPath $parent -Force
        Write-Host "  удалён   $parent (пустая папка)" -ForegroundColor Green
    }
}

if ($RemoveClaude) {
    Write-Host "`nУборка моста Claude Code:" -ForegroundColor Cyan
    Remove-Junction -Link (Join-Path $repoRoot '.claude\skills')
}

if (-not $Check) {
    Write-Host "`nМост в репозитории (стандарт Agent Skills):" -ForegroundColor Cyan
    $agentsParent = Join-Path $repoRoot '.agents'
    if (-not (Test-Path $agentsParent)) {
        New-Item -ItemType Directory -Path $agentsParent -Force | Out-Null
    }
    Ensure-Junction -Link (Join-Path $agentsParent 'skills') -Target $skillsRoot | Out-Null

    if ($Claude) {
        Write-Host "`nМост Claude Code (по запросу -Claude):" -ForegroundColor Cyan
        $claudeParent = Join-Path $repoRoot '.claude'
        if (-not (Test-Path $claudeParent)) {
            New-Item -ItemType Directory -Path $claudeParent -Force | Out-Null
        }
        Ensure-Junction -Link (Join-Path $claudeParent 'skills') -Target $skillsRoot | Out-Null
    }
}

if ($Global) {
    $globalRoot = Join-Path $DataDir 'skills'
    Write-Host "`nКаталог скиллов MiniMax Code ($globalRoot):" -ForegroundColor Cyan
    if (-not (Test-Path $globalRoot)) {
        New-Item -ItemType Directory -Path $globalRoot -Force | Out-Null
    }
    foreach ($skill in $skillDirs) {
        Ensure-Junction -Link (Join-Path $globalRoot $skill.Name) -Target $skill.FullName | Out-Null
    }
}

# Проверка состава: у каждого SKILL.md должны быть name и description.
Write-Host "`nСостав скиллов:" -ForegroundColor Cyan
$broken = @()
foreach ($skill in $skillDirs) {
    $file = Join-Path $skill.FullName 'SKILL.md'
    if (-not (Test-Path $file)) {
        $broken += "$($skill.Name): нет SKILL.md"
        continue
    }
    $head = (Get-Content $file -TotalCount 12) -join "`n"
    if ($head -notmatch '(?m)^---\s*$') {
        $broken += "$($skill.Name): нет YAML-frontmatter"
        continue
    }
    if ($head -notmatch '(?m)^name:\s*\S') {
        $broken += "$($skill.Name): нет поля name"
    }
    if ($head -notmatch '(?m)^description:\s*\S') {
        $broken += "$($skill.Name): нет поля description"
    }
}

if ($broken.Count -gt 0) {
    Write-Host "Проблемы с frontmatter:" -ForegroundColor Red
    $broken | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 2
}

$vendor = (Get-Content (Join-Path $skillsRoot '.aura-unity-skills.json') -Raw | ConvertFrom-Json).skills
$local = $skillDirs | Where-Object { $_.Name -notin $vendor }

Write-Host ("Все {0} скиллов: SKILL.md + name + description на месте." -f $skillDirs.Count) -ForegroundColor Green
if ($local) {
    Write-Host ("Проектных (не вендорных) скиллов: {0}" -f (($local | ForEach-Object Name) -join ', ')) -ForegroundColor Green
}

Write-Host "`nКак скиллы видят рантаймы:" -ForegroundColor Cyan
Write-Host "  AuraForUnity  — .Aura\Skills напрямую" -ForegroundColor DarkGray
Write-Host "  OpenCode      — opencode.json -> skills.paths" -ForegroundColor DarkGray
Write-Host "  MiniMax Code  — {{DATA_DIR}}\skills\<имя> (создаётся с -Global)" -ForegroundColor DarkGray
Write-Host "  WorkBuddy     — AGENTS.md -> .Aura\Skills\<имя>\SKILL.md" -ForegroundColor DarkGray
Write-Host "  Freebuff      — AGENTS.md -> .Aura\Skills\<имя>\SKILL.md" -ForegroundColor DarkGray
Write-Host "  Claude Code   — .claude\skills (нужен флаг -Claude)" -ForegroundColor DarkGray
