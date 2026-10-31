# Проверка сборки 1.0.0

## Автоматически проверено в Windows CI

Прогон: https://github.com/BalaRmack/html-editor/actions/runs/41252091150  
Коммит: `965a97e88c00cc143237b812cc2f27c37fcb77ad`  
Результат: **success**.

Проверено:

- restore/build solution на `windows-latest`, .NET SDK 8.0.4xx;
- Core xUnit-тесты;
- self-contained publish `win-x64`;
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
| `ru_sample.wav` | `ru` | «Привет, это проверка распознавания русского текста с пунктуацией.» | 10.6 с |
| `en_sample.wav` | `en` | “Let's try something else. This is a test of English dictation speed and accuracy.” | 10.5 с |
| `ru_sample.wav` | `auto` | «Привет, это проверка распознавания русского текста с пунктуацией.» | 20.6 с |

Это **функциональная** проверка на CI-runner, а не замер пользовательской задержки. Время включает инференс полного сэмпла на виртуальной машине; интерактивные partial-цели требуют замера на целевом ПК.

## Release-артефакты

https://github.com/BalaRmack/html-editor/releases/tag/v1.0.0

- `LocalVoice-1.0.0-win-x64-setup.exe` — ~47.7 МиБ
- `LocalVoice-1.0.0-win-x64-portable.zip` — ~65.9 МиБ
- соответствующие `.sha256`

## Что ещё не проверено автоматически

Живой микрофон, глобальные hotkey, tray, no-focus overlay, SendInput, темы, автозапуск и пользовательская производительность требуют ручной проверки по [manual-test-plan.md](manual-test-plan.md).
