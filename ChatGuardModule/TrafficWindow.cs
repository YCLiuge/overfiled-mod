using System.Globalization;

namespace ChatGuardModule;

// Snapshot window uses actual elapsed time, including stalls; never assumes the timer ran on time.
public sealed class TrafficWindow
{
    private double at;
    private long chatReceived, chatPassed, spellReceived, spellDropped;
    public void Reset(double now) { at = now; chatReceived = chatPassed = spellReceived = spellDropped = 0; }
    public string Sample(double now, long chatRx, long chatPass, long spellRx, long spellDrop)
    {
        double seconds = now - at;
        if (seconds <= 0) return "rates=pending";
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"window={seconds:F1}s chatRx/s={Math.Max(0, chatRx - chatReceived) / seconds:F1} chatPass/s={Math.Max(0, chatPass - chatPassed) / seconds:F1} spellRx/s={Math.Max(0, spellRx - spellReceived) / seconds:F1} spellDrop/s={Math.Max(0, spellDrop - spellDropped) / seconds:F1}");
        at = now; chatReceived = chatRx; chatPassed = chatPass; spellReceived = spellRx; spellDropped = spellDrop;
        return summary;
    }
}
