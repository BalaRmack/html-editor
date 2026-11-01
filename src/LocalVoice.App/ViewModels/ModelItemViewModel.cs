using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalVoice.Core;
namespace LocalVoice.App.ViewModels;

public sealed partial class ModelItemViewModel : ObservableObject
{
    public ModelDescriptor Descriptor { get; }
    public MainViewModel Main { get; }
    public string Id => Descriptor.Id;
    public string Name => Descriptor.Name;
    public string Description => Descriptor.Description;
    public string Size => Descriptor.SizeText;
    [ObservableProperty] private string _status = "Не скачана";
    [ObservableProperty] private bool _installed;
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    public bool CanModify => !Busy && !Main.ControllerBusy;
    public bool CanDelete => Installed && CanModify;
    public bool CanDownload => !Installed && CanModify;
    public bool CanSelect => Installed && !Selected && CanModify;
    public ModelItemViewModel(ModelDescriptor descriptor, MainViewModel main) { Descriptor = descriptor; Main = main; }
    [RelayCommand] private Task DownloadAsync() => Main.DownloadModelAsync(this);
    [RelayCommand] private Task SelectAsync() => Main.SelectModelAsync(this);
    [RelayCommand] private Task DeleteAsync() => Main.DeleteModelAsync(this);
    [RelayCommand] private void Cancel() => Main.Models.CancelDownload();
    public void Refresh()
    {
        Installed = Main.Models.LooksInstalled(Descriptor);
        Selected = Main.SettingsStore.Current.ModelId == Id;
        if (!Busy) Status = Installed ? (Selected ? "Выбрана · проверена при загрузке" : "На диске · SHA-256 при загрузке") : "Не скачана";
        OnPropertyChanged(nameof(CanModify)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CanSelect));
    }
}
