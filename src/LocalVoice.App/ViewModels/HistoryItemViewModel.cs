using CommunityToolkit.Mvvm.Input;
using LocalVoice.Core;
namespace LocalVoice.App.ViewModels;

public sealed class HistoryItemViewModel
{
    public TranscriptEntry Entry { get; }
    private readonly MainViewModel _main;
    public string Text => Entry.Text;
    public string Date => Entry.CreatedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm");
    public string Metadata => $"{Entry.DurationSeconds:F1} с · {Entry.Language.ToUpperInvariant()} · {ModelCatalog.ById.GetValueOrDefault(Entry.ModelId)?.Name ?? Entry.ModelId}";
    public IAsyncRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand UseCommand { get; }
    public HistoryItemViewModel(TranscriptEntry entry, MainViewModel main)
    {
        Entry = entry; _main = main;
        CopyCommand = new AsyncRelayCommand(() => main.CopyTextAsync(Text));
        DeleteCommand = new AsyncRelayCommand(() => main.DeleteHistoryAsync(Entry.Id));
        UseCommand = new AsyncRelayCommand(() => { main.LastText = Text; main.Navigate("Dashboard"); return Task.CompletedTask; });
    }
}
