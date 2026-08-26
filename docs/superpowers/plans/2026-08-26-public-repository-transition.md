# Public Repository Transition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Очистить устаревшие релизные материалы, синхронизировать публичную документацию и безопасно перевести репозиторий `jadieify-hub/esm-tspiot-multi-kkt-tool` в режим public.

**Architecture:** Работа разделена на обратимые изменения документации, проверяемый push с CI, осознанное удаление старого GitHub Release и финальную смену видимости. Теги и Git-история не переписываются; тег `v11.1` остаётся на `82c90b5`, а публичность включается только после всех проверок содержимого.

**Tech Stack:** Git, GitHub CLI (`gh`), GitHub Actions, PowerShell, Markdown.

**Spec:** `docs/superpowers/specs/2026-08-26-public-repository-transition-design.md`

## Global Constraints

- GitHub Release `v11` удаляется окончательно вместе с ассетами; тег `v11` сохраняется.
- Тег `v11.1` не перемещается и должен остаться на `82c90b5cda75113bd17944f2ec61eb96fbb697ed`.
- LICENSE и предоставленные пользователю права не меняются.
- Ассеты `v11.1`, их имена, размеры и SHA-256 не меняются.
- Репозиторий становится публичным только после успешного CI cleanup-коммита и всех предпубликационных проверок.
- Классическая защита ветки `main` не включается.

---

### Task 1: Синхронизировать публичную документацию

**Files:**
- Modify: `README.md:1-20`
- Modify: `RELEASE_NOTES_v11.1.md:19-25`

**Interfaces:**
- Consumes: имя workflow `CI` из `.github/workflows/ci.yml`; неизменяемый тег `v11.1`.
- Produces: README с CI-бейджем и точным описанием локальных/публичных артефактов; tracked release notes, пригодные для тела GitHub Release.

- [ ] **Step 1: Добавить CI-бейдж сразу после H1 README**

Добавить точную строку:

```markdown
[![CI](https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/actions/workflows/ci.yml/badge.svg)](https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/actions/workflows/ci.yml)
```

- [ ] **Step 2: Уточнить расположение артефактов**

Заменить утверждение `Все итоговые файлы находятся в artifacts/release` на:

```markdown
Локальная сборка складывает итоговые файлы в игнорируемую Git папку `artifacts/release`. Опубликованные версии доступны в [GitHub Releases](https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/releases/latest):
```

- [ ] **Step 3: Дополнить раздел проверки v11.1**

Раздел `## Проверка` должен содержать обе строки:

```markdown
- 72 shared-теста пройдены в .NET 8 через GitHub Actions.
- Те же 72 shared-теста пройдены системным компилятором и рантаймом .NET Framework.
```

- [ ] **Step 4: Добавить в tracked notes лицензионный раздел**

Добавить в конец `RELEASE_NOTES_v11.1.md`:

```markdown
## Лицензия

Выпуск распространяется на условиях [LICENSE](https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/blob/v11.1/LICENSE): программу можно бесплатно скачивать и использовать, в том числе в коммерческой деятельности; продажа, изменение и распространение изменённых версий — только с письменного разрешения KRS.
```

- [ ] **Step 5: Проверить документационный diff**

Run:

```powershell
git diff --check
rg -n "actions/workflows/ci.yml/badge.svg|Локальная сборка|\.NET 8 через GitHub Actions|## Лицензия" README.md RELEASE_NOTES_v11.1.md
git diff -- README.md RELEASE_NOTES_v11.1.md
```

Expected: `git diff --check` завершается с кодом 0; все четыре новые формулировки найдены; LICENSE отсутствует в diff.

- [ ] **Step 6: Зафиксировать документацию**

```powershell
git add -- README.md RELEASE_NOTES_v11.1.md
git commit -m "Подготовить публичную документацию проекта"
```

Expected: коммит содержит только два указанных файла.

### Task 2: Отправить документацию и получить зелёный CI

**Files:**
- Verify: `.github/workflows/ci.yml`
- Verify: Git refs `main`, `origin/main`, `refs/tags/v11.1`

**Interfaces:**
- Consumes: cleanup-коммит Task 1 и ранее закоммиченные spec/plan.
- Produces: синхронный `origin/main` и успешный GitHub Actions run для текущего HEAD.

- [ ] **Step 1: Проверить refs перед push**

```powershell
$expectedTag = '82c90b5cda75113bd17944f2ec61eb96fbb697ed'
if ((git rev-parse refs/tags/v11.1) -ne $expectedTag) { throw 'Тег v11.1 изменился' }
git status --short --branch
```

Expected: тег совпадает; рабочая копия чистая; `main` опережает `origin/main` только новыми документационными коммитами.

- [ ] **Step 2: Push main без изменения тегов**

```powershell
git push origin main
```

Expected: обновляется только `main`; команда не содержит `--tags`, `--force` или refspec тега.

- [ ] **Step 3: Дождаться CI текущего HEAD**

```powershell
$head = git rev-parse HEAD
$run = gh run list --repo jadieify-hub/esm-tspiot-multi-kkt-tool --commit $head --limit 1 --json databaseId,status,conclusion,headSha | ConvertFrom-Json
gh run watch $run.databaseId --repo jadieify-hub/esm-tspiot-multi-kkt-tool --exit-status
```

Expected: workflow завершается `success`; проходят тесты net8.0 и net48, сборки Legacy и Modern.

- [ ] **Step 4: Подтвердить синхронизацию refs**

```powershell
if ((git rev-parse HEAD) -ne (git ls-remote origin refs/heads/main | ForEach-Object { ($_ -split '\s+')[0] })) { throw 'origin/main не совпал с HEAD' }
if ((git ls-remote origin refs/tags/v11.1 | ForEach-Object { ($_ -split '\s+')[0] }) -ne '82c90b5cda75113bd17944f2ec61eb96fbb697ed') { throw 'Удалённый тег v11.1 изменился' }
```

Expected: `origin/main` совпадает с HEAD; удалённый `v11.1` остаётся на `82c90b5`.

### Task 3: Удалить устаревший релиз v11 и локальную ловушку

**Files:**
- Delete local ignored file: `artifacts/release/MultiKKT-ESM-TSPioT-v11.exe`
- Delete local ignored file: `artifacts/release/SHA256SUMS-v11.txt`
- Delete remote object: GitHub Release `v11`
- Preserve remote ref: `refs/tags/v11`

**Interfaces:**
- Consumes: явное решение владельца окончательно удалить старые небезопасные бинарники.
- Produces: отсутствие скачиваемого GitHub Release `v11`; сохранённый исторический тег; отсутствие неоднозначной локальной пары EXE/sums.

- [ ] **Step 1: Проверить точные цели удаления**

```powershell
$workspace = (Resolve-Path '.').Path
$targets = @(
  (Resolve-Path 'artifacts\release\MultiKKT-ESM-TSPioT-v11.exe').Path,
  (Resolve-Path 'artifacts\release\SHA256SUMS-v11.txt').Path
)
foreach ($target in $targets) {
  if (-not $target.StartsWith((Join-Path $workspace 'artifacts\release'), [StringComparison]::OrdinalIgnoreCase)) { throw "Небезопасная цель: $target" }
}
gh release view v11 --repo jadieify-hub/esm-tspiot-multi-kkt-tool --json tagName,assets
$v11TagBefore = git ls-remote origin refs/tags/v11
if (-not $v11TagBefore) { throw 'Тег v11 отсутствует' }
```

Expected: найдены ровно два локальных файла; релиз `v11` существует и содержит старые ассеты; тег `v11` существует.

- [ ] **Step 2: Удалить только два проверенных локальных файла**

```powershell
Remove-Item -LiteralPath $targets[0]
Remove-Item -LiteralPath $targets[1]
```

Expected: оба файла отсутствуют; соседние файлы и `artifacts/release/v11.1` сохранены.

- [ ] **Step 3: Окончательно удалить GitHub Release v11 без удаления тега**

```powershell
gh release delete v11 --repo jadieify-hub/esm-tspiot-multi-kkt-tool --yes
```

Expected: GitHub Release и все его ассеты удалены; ключ `--cleanup-tag` не используется.

- [ ] **Step 4: Проверить удаление и сохранность тега**

```powershell
gh release view v11 --repo jadieify-hub/esm-tspiot-multi-kkt-tool
if ($LASTEXITCODE -eq 0) { throw 'Релиз v11 всё ещё существует' }
if ((git ls-remote origin refs/tags/v11) -ne $v11TagBefore) { throw 'Тег v11 изменился' }
```

Expected: запрос релиза завершается `not found`; строка удалённого тега полностью совпадает с сохранённым значением.

### Task 4: Синхронизировать и перепроверить релиз v11.1

**Files:**
- Read: `RELEASE_NOTES_v11.1.md`
- Read local assets: `artifacts/release/v11.1/*`
- Modify remote object: GitHub Release `v11.1` body only

**Interfaces:**
- Consumes: tracked notes Task 1 и проверенные локальные релизные файлы.
- Produces: авторитетное тело GitHub Release, совпадающее с tracked notes; неизменённые четыре ассета.

- [ ] **Step 1: Сохранить удалённые метаданные ассетов до редактирования**

```powershell
$before = gh release view v11.1 --repo jadieify-hub/esm-tspiot-multi-kkt-tool --json assets,tagName,isDraft,isPrerelease | ConvertFrom-Json
if ($before.assets.Count -ne 4) { throw 'Ожидалось четыре ассета v11.1' }
```

- [ ] **Step 2: Обновить только тело релиза**

```powershell
gh release edit v11.1 --repo jadieify-hub/esm-tspiot-multi-kkt-tool --notes-file RELEASE_NOTES_v11.1.md
```

Expected: заголовок, тег, Latest-статус и ассеты не изменяются.

- [ ] **Step 3: Сверить тело и ассеты после редактирования**

```powershell
$after = gh release view v11.1 --repo jadieify-hub/esm-tspiot-multi-kkt-tool --json body,assets,tagName,isDraft,isPrerelease | ConvertFrom-Json
if ($after.body.Trim() -ne (Get-Content RELEASE_NOTES_v11.1.md -Raw).Trim()) { throw 'Тело релиза не совпало с tracked notes' }
if ((Compare-Object ($before.assets | Select-Object name,size,digest) ($after.assets | Select-Object name,size,digest) -Property name,size,digest)) { throw 'Ассеты v11.1 изменились' }
if ((git ls-remote origin refs/tags/v11.1 | ForEach-Object { ($_ -split '\s+')[0] }) -ne '82c90b5cda75113bd17944f2ec61eb96fbb697ed') { throw 'Тег v11.1 изменился' }
```

Expected: body идентичен файлу; четыре ассета и тег побайтово/метаданными неизменны.

- [ ] **Step 4: Перепроверить SHA-256 локального набора v11.1**

```powershell
$sumFile = 'artifacts\release\v11.1\SHA256SUMS-v11.1.txt'
foreach ($line in Get-Content $sumFile) {
  if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Неверная строка sums: $line" }
  $actual = (Get-FileHash -LiteralPath (Join-Path 'artifacts\release\v11.1' $matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $matches[1]) { throw "SHA-256 не совпал: $($matches[2])" }
}
```

Expected: три payload-файла совпадают с `SHA256SUMS-v11.1.txt`.

### Task 5: Настроить About и перевести репозиторий в public

**Files:**
- Modify remote settings: repository homepage, topics, visibility, secret scanning push protection.

**Interfaces:**
- Consumes: полностью проверенный private-репозиторий после Tasks 1-4.
- Produces: публичный репозиторий с заполненным About и включённой защитой секретов.

- [ ] **Step 1: Заполнить About до смены видимости**

```powershell
$repo = 'jadieify-hub/esm-tspiot-multi-kkt-tool'
$homepage = 'https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/releases/latest'
gh repo edit $repo --homepage $homepage --add-topic windows --add-topic winforms --add-topic dotnet --add-topic kkt --add-topic fiscal-register --add-topic atol --add-topic piot
```

Expected: описание остаётся `Мульти-ККТ в ЕСМ/ТС ПИоТ`; homepage и семь topics сохранены.

- [ ] **Step 2: Выполнить последний private-аудит**

```powershell
git status --porcelain --branch
gh release list --repo $repo --limit 10
gh repo view $repo --json visibility,description,homepageUrl,licenseInfo
```

Expected: рабочая копия чистая и синхронна; доступен только Latest Release `v11.1`; видимость всё ещё `PRIVATE`; license `Other`.

- [ ] **Step 3: Сделать репозиторий публичным**

```powershell
gh repo edit $repo --visibility public --accept-visibility-change-consequences
```

Expected: команда завершается успешно; никаких других репозиториев не затрагивается.

- [ ] **Step 4: Включить secret scanning и push protection**

```powershell
$security = @{
  security_and_analysis = @{
    secret_scanning = @{ status = 'enabled' }
    secret_scanning_push_protection = @{ status = 'enabled' }
  }
} | ConvertTo-Json -Depth 5
$security | gh api -X PATCH "repos/$repo" --input -
```

Expected: API возвращает `security_and_analysis.secret_scanning.status = enabled` и `secret_scanning_push_protection.status = enabled`.

### Task 6: Провести финальную публичную проверку

**Files:**
- Verify remote pages: repository root, README, Latest Release, LICENSE.
- Verify Git refs and local status.

**Interfaces:**
- Consumes: публичные настройки Task 5.
- Produces: доказательство публичной доступности, целостности релиза и завершённой очистки.

- [ ] **Step 1: Проверить настройки через GitHub API**

```powershell
$repoData = gh api "repos/$repo" | ConvertFrom-Json
if ($repoData.visibility -ne 'public' -or $repoData.private) { throw 'Репозиторий не public' }
if ($repoData.license.spdx_id -ne 'NOASSERTION') { throw "Ожидалась лицензия Other/NOASSERTION: $($repoData.license.spdx_id)" }
if ($repoData.security_and_analysis.secret_scanning.status -ne 'enabled') { throw 'Secret scanning не включён' }
if ($repoData.security_and_analysis.secret_scanning_push_protection.status -ne 'enabled') { throw 'Push protection не включена' }
```

- [ ] **Step 2: Проверить доступ без авторизации**

```powershell
$publicUrls = @(
  'https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool',
  'https://raw.githubusercontent.com/jadieify-hub/esm-tspiot-multi-kkt-tool/main/README.md',
  'https://raw.githubusercontent.com/jadieify-hub/esm-tspiot-multi-kkt-tool/main/LICENSE',
  'https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/releases/latest'
)
foreach ($url in $publicUrls) {
  $response = Invoke-WebRequest -Uri $url -UseBasicParsing -MaximumRedirection 5
  if ($response.StatusCode -ne 200) { throw "Публичный URL недоступен: $url" }
}
```

Expected: все четыре URL возвращают HTTP 200 без токена GitHub.

- [ ] **Step 3: Проверить README и релиз глазами через содержимое HTML/API**

```powershell
$readme = Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/jadieify-hub/esm-tspiot-multi-kkt-tool/main/README.md' -UseBasicParsing
if ($readme.Content -notmatch 'actions/workflows/ci.yml/badge.svg' -or $readme.Content -notmatch 'Локальная сборка') { throw 'README не содержит публичные исправления' }
$latest = gh api "repos/$repo/releases/latest" | ConvertFrom-Json
if ($latest.tag_name -ne 'v11.1' -or $latest.assets.Count -ne 4) { throw 'Latest Release повреждён' }
if ($latest.body -notmatch '\.NET 8 через GitHub Actions' -or $latest.body -notmatch '## Лицензия') { throw 'Тело Latest Release не синхронизировано' }
```

- [ ] **Step 4: Проверить финальные refs и рабочую копию**

```powershell
if ((git ls-remote origin refs/tags/v11.1 | ForEach-Object { ($_ -split '\s+')[0] }) -ne '82c90b5cda75113bd17944f2ec61eb96fbb697ed') { throw 'Тег v11.1 изменился' }
if ((git rev-parse HEAD) -ne (git ls-remote origin refs/heads/main | ForEach-Object { ($_ -split '\s+')[0] })) { throw 'main не синхронизирован' }
git status --short --branch
```

Expected: тег неизменен, `main` синхронизирован, рабочая копия чистая.
