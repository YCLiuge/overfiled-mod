namespace DrawGuessModule;

// Only the framebuffer contract is provided: export cannot read any texture or upload API.
public static partial class PaintingSystem
{
    public const int CanvasSize = 2;
    public static TestColor32[] AcceptedPixels = Array.Empty<TestColor32>();
    public static int CopyCount;

    public static TestColor32[] CopyPixels()
    {
        Interlocked.Increment(ref CopyCount);
        return (TestColor32[])AcceptedPixels.Clone();
    }
}

public readonly struct TestColor32
{
    public readonly byte r, g, b, a;
    public TestColor32(byte r, byte g, byte b, byte a)
    {
        this.r = r; this.g = g; this.b = b; this.a = a;
    }
}
