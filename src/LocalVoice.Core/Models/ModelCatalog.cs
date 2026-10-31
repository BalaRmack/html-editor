namespace LocalVoice.Core;

public sealed record ModelDescriptor(string Id, string Name, string Description, string FileName,
    long ExpectedBytes, string Sha256, string Url, bool Recommended = false)
{
    public string SizeText => FormatSize(ExpectedBytes);
    public static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.00} ГБ" : $"{bytes / (double)(1L << 20):0} МБ";
}

/// <summary>Pinned artifacts. Never substitute a mutable 'main' URL or an on-the-fly hash.</summary>
public static class ModelCatalog
{
    public const string Revision = "5359861c739e955e79d9a303bcbc70fb988958b1";
    private static readonly ModelDescriptor[] Models =
    [
        new("base-q5_1", "Whisper Base · Q5_1", "Быстрее на слабом CPU. Подходит для коротких чётких фраз.",
            "ggml-base-q5_1.bin", 59_707_625,
            "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898",
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/ggml-base-q5_1.bin"),
        new("small-q5_1", "Whisper Small · Q5_1", "Рекомендуемый баланс для русского и английского на CPU.",
            "ggml-small-q5_1.bin", 190_085_487,
            "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/ggml-small-q5_1.bin", true),
        new("medium-q5_0", "Whisper Medium · Q5_0", "Больше точности, существенно выше нагрузка на CPU.",
            "ggml-medium-q5_0.bin", 539_212_467,
            "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f",
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/ggml-medium-q5_0.bin"),
        new("turbo-q5_0", "Large v3 Turbo · Q5_0", "Для быстрого многоядерного CPU. Требует больше памяти.",
            "ggml-large-v3-turbo-q5_0.bin", 574_041_195,
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/ggml-large-v3-turbo-q5_0.bin")
    ];
    public static IReadOnlyList<ModelDescriptor> All => Models;
    public static IReadOnlyDictionary<string, ModelDescriptor> ById { get; } = Models.ToDictionary(x => x.Id);
}
