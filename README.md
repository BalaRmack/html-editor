# LocalVoice

**Локальная диктовка для Windows 10/11 x64.** Нажмите глобальную горячую клавишу, говорите — текст появляется в плавающем оверлее и вставляется в активное поле. Распознавание выполняется на вашем процессоре через [Whisper.net](https://github.com/sandrohanea/whisper.net) / [whisper.cpp](https://github.com/ggerganov/whisper.cpp). Аудио не записывается на диск и не отправляется в сеть.

[![Windows build](https://github.com/BalaRmack/html-editor/actions/workflows/windows.yml/badge.svg?branch=arena%2Fe21a0934-html-editor)](https://github.com/BalaRmack/html-editor/actions/workflows/windows.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> Репозиторий исторически называется `html-editor`, но старое содержимое полностью удалено отдельным коммитом. Сейчас это самостоятельное C#/.NET 8/WPF-приложение **LocalVoice**.

## Скачать

Готовые файлы публикуются в [GitHub Releases](https://github.com/BalaRmack/html-editor/releases/tag/v1.1.0):

- `LocalVoice-1.1.0-win-x64-setup.exe` — per-user установщик, без обязательных прав администратора.
- `LocalVoice-1.1.0-win-x64-portable.zip` — self-contained папка, распакуйте и запустите `LocalVoice.exe`.
- `*.sha256` — контрольные суммы.

Модели **не входят** в установщик. При первом запуске откройте раздел **Модели** и скачайте рекомендуемую `Whisper Small · Q5_1` (~181 МБ). Это единственная сетевая операция по умолчанию.

## Возможности

- Глобальная горячая клавиша `Ctrl+Alt+Space` (переназначается), режимы **переключения** и **удержания**.
- Always-on-top оверлей без кражи фокуса: индикатор микрофона, «Слушаю…», живой текст, копирование и остановка.
- Трей: левый клик открывает центр управления, правый — меню. Закрытие окна прячет приложение в трей.
- Центр управления: диктовка, тест микрофона, история с поиском/удалением, менеджер моделей, настройки, интеграции, справка.
- Русский, английский и автоопределение языка; дополнительные языки Whisper.
- Копирование в буфер и опциональная вставка в исходное поле через `SendInput`.
- Тёмная, светлая и системная темы; настройка положения, размера и прозрачности оверлея.
- Автозапуск через `HKCU\...\Run`, статистика, логи, проверка обновлений-заглушка.
- Локальное распознавание, VAD на тишину, ограничение длины сессии и очереди, graceful-ошибки микрофона/модели.
- Расширяемый реестр экспорта. По умолчанию удалённых провайдеров нет; сетевой экспорт возможен только после явного подключения и действия пользователя.

## Быстрый старт

1. Установите или распакуйте portable-версию.
2. Запустите **LocalVoice**. Откроется центр управления.
3. Перейдите в **Модели** → скачайте **Whisper Small · Q5_1** → выберите её.
4. Поставьте курсор в любое текстовое поле другого приложения.
5. Нажмите `Ctrl+Alt+Space`, говорите, нажмите ещё раз или `Esc`.
6. Скопируйте результат или включите **Автовставку** в настройках.

### Горячие клавиши

| Действие | По умолчанию |
|---|---|
| Начать/остановить диктовку | `Ctrl+Alt+Space` |
| Остановить диктовку | `Esc` во время сессии |
| Push-to-talk | Удерживать настроенное сочетание |
| Открыть центр управления | Левый клик по трею |

Комбинацию можно записать в **Настройки → Горячая клавиша → Записать**. Недопустимые и занятые комбинации отклоняются с понятной ошибкой.

## Модели

| Модель | Размер | Когда выбирать |
|---|---:|---|
| Whisper Base · Q5_1 | ~57 МБ | Слабый CPU, короткие чёткие фразы |
| **Whisper Small · Q5_1** | **~181 МБ** | **Рекомендуемый баланс RU/EN на CPU** |
| Whisper Medium · Q5_0 | ~514 МБ | Больше точности, выше нагрузка |
| Large v3 Turbo · Q5_0 | ~547 МБ | Быстрый многоядерный CPU, больше памяти |

Файлы скачиваются с закреплённой ревизии `ggerganov/whisper.cpp` на Hugging Face. Перед использованием проверяются размер и SHA-256; неполные файлы удаляются. Загрузку можно отменить. Модели хранятся в `%LOCALAPPDATA%\LocalVoice\models` и удаляются из интерфейса.

## Приватность

- Аудио живёт только в оперативной памяти во время сессии.
- По умолчанию нет облачного распознавания, телеметрии и фоновых сетевых запросов.
- История текста хранится локально и можно отключить или очистить.
- Логи не содержат распознанный текст и аудио; ротация — 7 дней.
- Удалённый экспорт скрыт/неактивен, пока провайдер не подключён пользователем. Отправка происходит только по явному действию **Экспорт**.

Подробнее: [docs/privacy.md](docs/privacy.md).

## Сборка из исходников

Требования:

- Windows 10/11 x64 для запуска и installer.
- [.NET SDK 8.0.4xx](https://dotnet.microsoft.com/download/dotnet/8.0).
- Для установщика: [Inno Setup 6](https://jrsoftware.org/isinfo.php). CI использует 6.7.4 с проверкой SHA-256.
- Кросс-сборка publish возможна на Linux/macOS с `EnableWindowsTargeting=true`, но WPF-запуск, hotkey, tray, SendInput и installer требуют Windows.

```powershell
git clone https://github.com/BalaRmack/html-editor.git
cd html-editor
git checkout arena/e21a0934-html-editor

# Полный цикл: restore → build → test → publish → installer
./scripts/build.ps1 -Task all -Version 1.1.0

# Без Inno Setup
./scripts/build.ps1 -Task all -SkipInstaller

# Отдельные задачи
./scripts/build.ps1 -Task restore
./scripts/build.ps1 -Task build
./scripts/build.ps1 -Task test
./scripts/build.ps1 -Task publish
./scripts/build.ps1 -Task installer -InnoCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Результаты:

- `artifacts/publish/win-x64/LocalVoice.exe`
- `artifacts/installer/LocalVoice-1.1.0-win-x64-setup.exe`
- `artifacts/tests/*.trx`
- `artifacts/publish/win-x64/licenses/` — собранные лицензии NuGet-пакетов.

Команды `dotnet` вручную:

```powershell
dotnet restore LocalVoice.sln -p:EnableWindowsTargeting=true --use-lock-file
dotnet build LocalVoice.sln -c Release --no-restore -p:Version=1.1.0
dotnet test tests/LocalVoice.Core.Tests -c Release --no-build
dotnet publish src/LocalVoice.App -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/win-x64 -p:Version=1.1.0 -p:PublishSingleFile=false -p:DebugType=none
```

### CI

[`.github/workflows/windows.yml`](.github/workflows/windows.yml) на `windows-latest`:

1. Restore/build/test.
2. Self-contained publish и сбор лицензий.
3. Headless speech smoke: скачивание модели, SHA-256, распознавание RU/EN/auto на закреплённых WAV-сэмплах.
4. Inno Setup installer.
5. Проверка native/.NET runtime layout.
6. Артефакты и GitHub Release assets.

Speech smoke **не** проверяет живой микрофон, глобальные hotkey, трей, оверлей и SendInput. Это ручные Windows-проверки: [docs/manual-test-plan.md](docs/manual-test-plan.md). Фактические результаты CI: [docs/verification.md](docs/verification.md).

## Архитектура

```text
LocalVoice.Core
  Configuration   настройки, hotkey parser, atomic JSON + .bak
  Storage         пути, история, статистика, privacy-safe лог
  Models          каталог, безопасная загрузка, SHA-256, удаление
  Audio           VAD, transcript buffer, очистка галлюцинаций
  Export          интерфейс провайдеров и реестр

LocalVoice.App
  Platform        Win32: hotkey, hooks, SendInput, no-activate, DWM
  Services        микрофон, Whisper, очередь, сессия, трей, темы, автозапуск
  ViewModels      MVVM без тяжёлого фреймворка
  Views/Themes    WPF центр управления и оверлей
```

Подробнее: [docs/architecture.md](docs/architecture.md), [docs/integrations.md](docs/integrations.md).

## Ограничения и честные цели

- Целевая платформа: Windows 10 1809+ / Windows 11 x64. ARM64 не заявлен.
- Cold start <2–3 с, hotkey <200 мс, overlay <300 мс, главное окно <500 мс, partial <1 с — **цели, а не измеренные гарантии**. Они зависят от CPU, модели, микрофона и антивируса.
- Первая загрузка модели занимает секунды; последующие сессии используют кэш фабрики.
- Energy-VAD проще нейросетевого: шумная музыка/речь на фоне может давать ложные срабатывания. Порог настраивается.
- Сессия ограничена 2 минутами, финальная обработка — 30 с; при перегрузке предлагается более лёгкая модель.
- Вставка не работает в приложения с более высоким integrity level (например, запущенными от администратора) и может блокироваться политиками безопасности.
- Автоопределение языка на коротких фразах может ошибаться; для стабильности выберите RU или EN явно.
- Автоматического обновления пока нет: кнопка объясняет, где взять новый установщик.
- Облачные провайдеры и OAuth намеренно не включены в базовую поставку.

## Структура репозитория

```text
src/LocalVoice.Core/       переносимое ядро
src/LocalVoice.App/        WPF + Win32 + tray
tests/LocalVoice.Core.Tests/
tests/LocalVoice.SpeechSmoke/
installer/LocalVoice.iss
scripts/build.ps1
scripts/collect-licenses.ps1
docs/
```

## Лицензии

Код LocalVoice — [MIT](LICENSE). Зависимости и модели имеют собственные лицензии; см. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). При publish скрипт собирает файлы лицензий NuGet-пакетов в `licenses/`.
