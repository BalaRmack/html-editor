# Проверка сборки 1.2.1

Дата: 2026-11-01. Branch: `arena/e21a0934-html-editor`.

## Windows CI

Успешный запуск: https://github.com/BalaRmack/html-editor/actions/runs/41276492938

Проверено на `windows-latest`:

- Restore/build self-contained WPF `win-x64`, .NET 8, `TreatWarningsAsErrors=true`.
- Core unit tests.
- Новые тесты живого конвейера диктовки:
  - кадры микрофона → VAD → финальный текст;
  - промежуточный текст до остановки;
  - медленное распознавание не создаёт очередь тяжёлых partial-задач;
  - остановка без паузы дожатиает последний буфер;
  - тишина не отправляется в Whisper.
- UI smoke: тёмная/светлая темы, ComboBox popup, обучение, компактный оверлей, автораскрытие по распознанному тексту, устойчивая ошибка.
- Speech smoke на реальных WAV:
  - RU: `Cpu`, 10,814 ms, текст распознан.
  - EN: `Cpu`, 10,532 ms, текст распознан.
  - RU auto language: `Cpu`, 20,627 ms, определён русский.
  - RU forced `CpuNoAvx`: 103,875 ms, текст распознан.
- Publish содержит `runtimes/noavx/win-x64` и все обязательные native DLL.
- Inno Setup per-user installer собран.
- Silent install, upgrade-in-place и uninstall smoke прошли.
- Portable ZIP и SHA-256 файлы опубликованы.

## Релиз

https://github.com/BalaRmack/html-editor/releases/tag/v1.2.1

- `LocalVoice-1.2.1-win-x64-setup.exe` — 51,008,703 B
- `LocalVoice-1.2.1-win-x64-portable.zip` — 70,842,213 B
- Рядом опубликованы `.sha256`.

## Что не проверено автоматически

- Реальный физический микрофон, горячие клавиши, tray, no-focus overlay и SendInput на пользовательском рабочем столе.
- Вставка именно в ZeroScript: если текст виден в LocalVoice, но не появляется в поле, используйте кнопку «Вставить» или Ctrl+V; некоторые приложения блокируют синтетический ввод.
- Полный цикл автообновления с уже установленной пользовательской копии. В 1.2.1 проверены сборка, установщик поверх существующей копии и наличие updater/NoAvx в поставке.
- Тесты живого конвейера используют подставляемый источник кадров и распознаватель; они проверяют очередь/VAD/остановку, но не заменяют проверку конкретного микрофона и CPU.
