using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace LocalVoice.App.ViewModels;

public sealed record TutorialStep(string Title, string Body, string Hint, string ActionLabel, string TargetTab);

public sealed partial class TutorialViewModel : ObservableObject
{
    public IReadOnlyList<TutorialStep> Steps { get; } =
    [
        new("Добро пожаловать в LocalVoice",
            "Диктуйте голосом в любом приложении. Распознавание идёт на вашем процессоре, а аудио не записывается на диск и не отправляется в сеть.",
            "Для работы не нужны аккаунт, подписка или постоянное подключение к интернету.",
            "", ""),
        new("Шаг 1 · Скачайте модель",
            "Откройте раздел «Модели» и скачайте Whisper Small. Это единственная сетевая загрузка. Файл проверяется по SHA-256 и сохраняется на вашем компьютере.",
            "Для слабого ПК подойдёт Base. Medium и Turbo точнее, но заметно тяжелее.",
            "Открыть модели", "Models"),
        new("Шаг 2 · Проверьте микрофон",
            "На главном экране нажмите «Тест микрофона» и скажите пару слов. Волна должна двигаться. Если нет — выберите другое устройство в настройках.",
            "Тест длится 10 секунд и не сохраняет звук.",
            "Открыть диктовку", "Dashboard"),
        new("Шаг 3 · Нажмите горячую клавишу",
            "Поставьте курсор в текстовое поле другого приложения и нажмите Ctrl+Alt+Space. Появится мини-оверлей: микрофон слева, живая волна по центру. Повторное нажатие или Esc останавливает диктовку.",
            "В режиме Push-to-talk говорите, пока удерживаете сочетание. Клавишу можно переназначить.",
            "Настроить клавишу", "Settings"),
        new("Шаг 4 · Заберите текст",
            "Скопируйте результат или включите автовставку в настройках. Финальные фразы можно найти в истории. Обучение всегда доступно кнопкой «Обучение» слева.",
            "Автовставка выключена по умолчанию и работает только с исходным окном.",
            "Открыть настройки", "Settings")
    ];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _stepIndex;
    public TutorialStep Current => Steps[Math.Clamp(StepIndex, 0, Steps.Count - 1)];
    public int StepNumber => StepIndex + 1;
    public string StepLabel => $"{StepNumber} / {Steps.Count}";
    public bool CanGoBack => StepIndex > 0;
    public bool IsLastStep => StepIndex >= Steps.Count - 1;
    public string NextLabel => IsLastStep ? "Готово" : "Дальше";
    public bool HasAction => Current.ActionLabel.Length > 0;
    public event EventHandler? Completed;
    public event EventHandler<string>? NavigateRequested;

    public void Start() { StepIndex = 0; IsOpen = true; }
    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Current)); OnPropertyChanged(nameof(StepNumber)); OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(CanGoBack)); OnPropertyChanged(nameof(IsLastStep)); OnPropertyChanged(nameof(NextLabel)); OnPropertyChanged(nameof(HasAction));
    }
    [RelayCommand] private void Next()
    {
        if (IsLastStep) { IsOpen = false; Completed?.Invoke(this, EventArgs.Empty); }
        else StepIndex++;
    }
    [RelayCommand] private void Back() { if (CanGoBack) StepIndex--; }
    [RelayCommand] private void Skip() { IsOpen = false; Completed?.Invoke(this, EventArgs.Empty); }
    [RelayCommand] private void GoToAction()
    {
        if (!HasAction) return;
        var target = Current.TargetTab;
        IsOpen = false;
        Completed?.Invoke(this, EventArgs.Empty);
        NavigateRequested?.Invoke(this, target);
    }
}
