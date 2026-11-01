using LocalVoice.Core;
using Xunit;
namespace LocalVoice.Core.Tests;

public sealed class AudioTests
{
    private static AudioFrame Frame(float amplitude, int count = 1600)
    {
        var samples = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? amplitude : -amplitude).ToArray();
        return new AudioFrame(samples, AudioFrame.ComputeRms(samples));
    }
    [Fact] public void Silence_never_reaches_recognizer()
    {
        var vad = new UtteranceAccumulator();
        for (var i = 0; i < 100; i++) Assert.False(vad.Accept(Frame(0)).FinalDue);
        Assert.Empty(vad.TakeFinal(true)); Assert.Equal(0, vad.BufferedSeconds);
    }
    [Fact] public void Speech_is_buffered_and_silence_commits_once()
    {
        var vad = new UtteranceAccumulator();
        Assert.True(vad.Accept(Frame(.1f)).Started);
        for (var i = 0; i < 10; i++) vad.Accept(Frame(.1f));
        Assert.True(vad.BufferedSeconds >= 1);
        var finished = false;
        for (var i = 0; i < 10; i++) finished |= vad.Accept(Frame(0)).FinalDue;
        Assert.True(finished); Assert.NotEmpty(vad.TakeFinal()); Assert.Empty(vad.TakeFinal());
    }
    [Fact] public void Long_continuous_speech_is_bounded()
    {
        var vad = new UtteranceAccumulator(); var due = false;
        for (var i = 0; i < 130; i++) due |= vad.Accept(Frame(.1f)).FinalDue;
        Assert.True(due);
        Assert.NotEmpty(vad.TakeFinal(force: true));
        Assert.True(vad.BufferedSeconds <= 12.1);
    }
    [Fact] public void Very_short_noise_is_not_a_final_transcript()
    {
        var vad = new UtteranceAccumulator(); vad.Accept(Frame(.2f, 160));
        for (var i = 0; i < 10; i++) vad.Accept(Frame(0));
        Assert.Empty(vad.TakeFinal());
    }
    [Fact] public void Partial_never_overwrites_final_and_order_is_stable()
    {
        var text = new TranscriptBuffer();
        text.Accept(0, "Привет", false); Assert.Equal("Привет", text.DisplayText);
        text.Accept(1, "мир", false); Assert.Equal("Привет", text.DisplayText);
        text.Accept(0, "Привет,", true); text.Accept(0, "wrong partial", false);
        text.Accept(1, "мир!", true);
        Assert.Equal("Привет, мир!", text.FinalText); Assert.Equal(text.FinalText, text.DisplayText);
    }
    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("Субтитры сделал DimaTorzok")]
    [InlineData("Спасибо за просмотр")]
    [InlineData("   ")]
    public void Known_silence_hallucinations_are_removed(string input) => Assert.Equal("", TranscriptText.Clean(input));
    [Fact] public void Overlap_deduplication_preserves_russian_and_english()
    {
        Assert.Equal("Привет мой друг", TranscriptText.Join("Привет мой", "мой друг"));
        Assert.Equal("Hello my friend", TranscriptText.Join("Hello my", "my friend"));
        Assert.Equal("Первая фраза. Вторая фраза.", TranscriptText.Join("Первая фраза.", "Вторая фраза."));
    }
    [Fact] public void Rms_is_empty_safe()
    {
        Assert.Equal(0, AudioFrame.ComputeRms([]));
        Assert.Equal(.5, AudioFrame.ComputeRms([.5f, -.5f]), 5);
    }
}
