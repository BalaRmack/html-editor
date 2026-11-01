# Проверка сборки 1.1.0

## Автоматически проверено в Windows CI

Прогон: https://github.com/BalaRmack/html-editor/actions/runs/41272336980  
Коммит: `9e2fffa00442613947a40196df9a30414932c43d`  
Результат: **success**, ~3 мин 19 с.

Проверено:

- restore/build solution на `windows-latest`, .NET SDK 8.0.4xx;
- Core xUnit-тесты, включая миграцию настроек schema 2 и обучение;
- self-contained publish `win-x64`;
- **UI smoke (WPF)**: загрузка тем/ресурсов, контраст текста в Dark/Light, открытие ComboBox popup, обучение, компактный оверлей и видимая ошибка запуска;
- сбор лицензий NuGet-пакетов в `licenses/`;
- скачивание модели `small-q5_1` с проверкой размера и SHA-256;
- headless распознавание Whisper.net CPU на закреплённых WAV-сэмплах;
- компиляция Inno Setup 6.7.4;
- тихая per-user установка и uninstall;
- наличие `LocalVoice.exe`, `coreclr.dll`, `whisper.dll` в установленной/publish-раскладке;
- публикация release-артефактов и SHA-256.

### Speech smoke

Модель: `ggml-small-q5_1.bin`, SHA-256 подтверждён.

| Сэмпл | Язык | Результат | Время CI |
|---|---|---|---:|
| `ru_sample.wav` | `ru` | «Привет, это проверка распознавания русского текста с пунктуацией.» | 10.9 с |
| `en_sample.wav` | `en` | “Let's try something else. This is a test of English dictation speed and accuracy.” | 10.5 с |
| `ru_sample.wav` | `auto` | «Привет, это проверка распознавания русского текста с пунктуацией.» | 21.2 с |

Это **функциональная** проверка на CI-runner, а не замер пользовательской задержки. Время включает инференс полного сэмпла на виртуальной машине; интерактивные partial-цели требуют замера на целевом ПК.

## Release-артефакты

https://github.com/BalaRmack/html-editor/releases/tag/v1.1.0

- `LocalVoice-1.1.0-win-x64-setup.exe` — ~47.7 МиБ
- `LocalVoice-1.1.0-win-x64-portable.zip` — ~65.9 МиБ
- соответствующие `.sha256`

## Что ещё не проверено автоматически

Живой микрофон, глобальные hotkey, tray, no-focus overlay, SendInput, визуальное восприятие анимаций, автозапуск и пользовательская производительность требуют ручной проверки по [manual-test-plan.md](manual-test-plan.md).

Историческая проверка 1.0.0: прогон `41252507646`, коммит `8e8fd04`.
