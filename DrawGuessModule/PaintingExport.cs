using System;
using System.Globalization;
using System.IO;
using System.Text;
using DrawGuessModule.Core;

namespace DrawGuessModule;

public static partial class PaintingSystem
{
    /// <summary>
    /// Saves the current accepted framebuffer locally, without waiting for a texture upload.
    /// Call on the drawing main thread; the runtime controls whether saving is allowed in its mode.
    /// </summary>
    public static string SaveLocalPainting(string? name = null)
    {
        var pixels = CopyPixels();
        var rgba = new byte[checked(pixels.Length * 4)];
        for (int i = 0; i < pixels.Length; i++)
        {
            int offset = i * 4;
            rgba[offset] = pixels[i].r;
            rgba[offset + 1] = pixels[i].g;
            rgba[offset + 2] = pixels[i].b;
            rgba[offset + 3] = pixels[i].a;
        }
        byte[] png = PngWriter.EncodeRgba(CanvasSize, CanvasSize, rgba);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "UserData", "DrawGuess", "Paintings"));
        Directory.CreateDirectory(directory);
        string prefix = SanitizePaintingName(name);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, prefix + "_" + timestamp + "_" + Guid.NewGuid().ToString("N") + ".png");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            file.Write(png, 0, png.Length);
        return path;
    }

    private static string SanitizePaintingName(string? name)
    {
        const int maximumLength = 64;
        if (string.IsNullOrWhiteSpace(name)) return "painting";
        var safe = new StringBuilder(maximumLength);
        foreach (char value in name)
        {
            if (safe.Length == maximumLength) break;
            // An allowlist also excludes both path separators, dots, colons and controls on every OS.
            safe.Append(char.IsLetterOrDigit(value) || value == '-' || value == '_' ? value : '_');
        }
        return safe.Length == 0 ? "painting" : safe.ToString();
    }
}
