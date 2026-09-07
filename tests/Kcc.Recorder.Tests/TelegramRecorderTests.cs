using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramRecorderTests
{
    [Theory]
    [InlineData(1, 5, 60, 5)]
    [InlineData(2, 5, 60, 10)]
    [InlineData(3, 5, 60, 20)]
    [InlineData(4, 5, 60, 40)]
    [InlineData(5, 5, 60, 60)]     // gedeckelt
    [InlineData(9, 5, 60, 60)]     // bleibt gedeckelt
    [InlineData(1, 0, 60, 1)]      // Mindest-Startwert 1 s
    [InlineData(1, 30, 10, 30)]    // Deckel < Startwert wird auf den Startwert angehoben
    public void Reconnect_Wartezeit_verdoppelt_sich_bis_zum_Deckel(
        int attempt, int initial, int max, double expected) =>
        Assert.Equal(expected, TelegramRecorder.ReconnectDelaySeconds(attempt, initial, max));
}
