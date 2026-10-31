namespace LocalVoice.Core;

/// <summary>
/// Implemented by UI-independent providers. Remote providers must be registered explicitly,
/// default to disconnected, and export only after a direct user action. OAuth belongs in ConnectAsync,
/// never in a constructor or UI event. Store provider secrets with OS-protected storage, not settings.json.
/// </summary>
public interface IExportProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool SendsOverNetwork { get; }
    bool IsConfigured { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task ExportAsync(string text, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
public sealed class ExportProviderRegistry
{
    private readonly List<IExportProvider> _providers = [];
    public IReadOnlyList<IExportProvider> Providers => _providers;
    public IEnumerable<IExportProvider> ConnectedRemote => _providers.Where(x => x.SendsOverNetwork && x.IsConfigured);
    public void Register(IExportProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(x => x.Id == provider.Id)) throw new InvalidOperationException("Duplicate export provider.");
        _providers.Add(provider);
    }
}
