using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Chat.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class LiveFreePaintingCheck
{
    public static async Task Run(HttpClient http, int port, string suffix, string channel)
    {
        async Task Command(string text)
        {
            using var content = new StringContent(JsonConvert.SerializeObject(new { command = text, target = "logic", waitMs = 2000 }), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("command", content);
            response.EnsureSuccessStatusCode();
            if ((string?)JObject.Parse(await response.Content.ReadAsStringAsync())["data"]?["status"] != "completed") throw new Exception("Command failed: " + text);
        }
        async Task Until(Func<bool> condition, string label)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 15)
            {
                if (condition()) { Console.WriteLine("LIVE FREE PASS: " + label); return; }
                await Task.Delay(40);
            }
            throw new Exception("Live free timeout: " + label);
        }
        // Current protocol: ordinary boards are channel-visible and creator-only, without a free game room.
        await Command("dg mode free");
        await Command("dg board");
        await Command("dg draw clear 0 0 0 0 EA4335 4 1000");
        await Command("dg draw line 40 40 80 40 34A853 4 1001");
        await Command("dg draw line 100 200 400 200 EA4335 4 1002");        await Command("dg hide");
        await Command("dg status");
        string gameRoot = Environment.GetEnvironmentVariable("DRAWGUESS_GAME_DIR") ??
            Path.GetDirectoryName(Process.GetProcessesByName("launcher").FirstOrDefault()?.MainModule?.FileName ??
                throw new Exception("Set DRAWGUESS_GAME_DIR to the running game's directory for local PNG inspection"))!;
        string logFile = Path.Combine(gameRoot, "MelonLoader", "Latest.log");
        async Task<string> Save(string prefix)
        {
            await Command("dg save " + prefix);
            string? path = null;
            await Until(() =>
            {
                using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                foreach (string line in reader.ReadToEnd().Split('\n').Reverse())
                {
                    const string marker = "画作已保存：";
                    int at = line.IndexOf(marker, StringComparison.Ordinal);
                    if (at < 0 || !line.Contains(prefix, StringComparison.Ordinal)) continue;
                    path = line[(at + marker.Length)..].Trim();
                    return File.Exists(path);
                }
                return false;
            }, "actual Unity framebuffer saved to local PNG");
            Console.WriteLine("LIVE PNG: " + path);
            return path!;
        }
        var personal = Decode(await Save("live-owner-" + suffix));
        AssertPixel(personal, 60, 40, 0x34, 0xA8, 0x53);
        AssertPixel(personal, 250, 200, 0xEA, 0x43, 0x35);
        AssertPixel(personal, 128, 256, 255, 255, 255);
        Console.WriteLine("LIVE FREE PASS: creator board PNG contains owner painting without remote free-room collaboration");
    }
    private static byte[] Decode(string path)
    {
        byte[] png = File.ReadAllBytes(path);
        if (!png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new Exception("Invalid PNG");
        int ReadInt(int offset) => (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
        if (ReadInt(16) != 512 || ReadInt(20) != 512) throw new Exception("PNG is not 512x512");
        using var compressed = new MemoryStream();
        for (int pos = 8; pos < png.Length;)
        {
            int count = ReadInt(pos);
            if (Encoding.ASCII.GetString(png, pos + 4, 4) == "IDAT") compressed.Write(png, pos + 8, count);
            pos += count + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream(); zlib.CopyTo(raw);
        byte[] bytes = raw.ToArray();
        if (bytes.Length != 512 * (512 * 4 + 1)) throw new Exception("PNG pixel length mismatch");
        for (int y = 0; y < 512; y++) if (bytes[y * 2049] != 0) throw new Exception("Unexpected PNG filter");
        return bytes;
    }

    private static void AssertPixel(byte[] rows, int x, int y, byte r, byte g, byte b)
    {
        int offset = (511 - y) * 2049 + 1 + x * 4;
        if (rows[offset] != r || rows[offset + 1] != g || rows[offset + 2] != b || rows[offset + 3] != 255)
            throw new Exception($"Saved pixel ({x},{y}) mismatch: {rows[offset]:X2}{rows[offset + 1]:X2}{rows[offset + 2]:X2}");
    }
}
