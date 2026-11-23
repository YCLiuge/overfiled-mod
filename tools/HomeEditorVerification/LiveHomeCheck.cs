using System.Text;
using System.Text.Json.Nodes;

internal static class LiveHomeCheck
{
    public static async Task Run(string root)
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(30) };
        string artifacts = Path.Combine(root, "artifacts"); Directory.CreateDirectory(artifacts);
        string module = Path.Combine(root, "GameMods", "Mods", "HomeEditorModule.dll").Replace('\\', '/');
        string helper = Path.Combine(root, "tools", "LiveHomeProbe", "bin", "Release", "net6.0", "LiveHomeProbe.dll").Replace('\\', '/');
        int checks = 0;
        async Task Command(string text)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new { command = text, target = "logic", waitMs = 5000 });
            using var response = await http.PostAsync("/api/command", new StringContent(json, Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            var reply = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            if ((int?)reply["code"] != 200) throw new Exception(reply.ToJsonString());
        }
        async Task<JsonNode> Probe(string action, string? value = null)
        {
            string output = Path.Combine(artifacts, "home-live-" + action + "-" + Guid.NewGuid().ToString("N") + ".json");
            await Command("call Verification.LiveHomeProbe.Run " + action + " " + output.Replace('\\', '/') + " " + (value ?? module));
            if (!File.Exists(output)) throw new Exception("Probe did not write " + output);
            var result = JsonNode.Parse(await File.ReadAllTextAsync(output))!;
            if ((bool?)result["Success"] != true) throw new Exception(result.ToJsonString());
            return result;
        }
        async Task<JsonNode> Send(string text) => await Probe("command", Uri.EscapeDataString(text));
        JsonNode Diagnostics(JsonNode node) => JsonNode.Parse((string?)node["Diagnostics"] ?? throw new Exception("Missing editor diagnostics"))!;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("HOME LIVE FAIL: " + label);
            checks++; Console.WriteLine("HOME LIVE PASS: " + label);
        }
        await Command("call System.Reflection.Assembly.LoadFile " + helper);
        var baseline = await Probe("baseline");
        string sceneBaseline = baseline["SceneObjects"]!.ToJsonString();
        try
        {
            var loaded = await Probe("load");
            Check((bool?)loaded["ModuleLoaded"] == true && (bool?)Diagnostics(loaded)["active"] == false, "independent DLL loads without entering editor mode");
            var entered = Diagnostics(await Send("~home"));
            Check((bool?)entered["active"] == true, "~home activates migrated editor in real Unity");
            var repeated = Diagnostics(await Send("~home"));
            Check((int?)entered["generation"] == (int?)repeated["generation"], "repeated entry keeps one editor session");
            var copied = Diagnostics(await Send("-cc"));
            Check((bool?)copied["active"] == true && (int?)copied["clipboardCount"] >= 0, "read-only furniture copy uses shared clipboard");
            int count = (int)copied["clipboardCount"]!;
            var missing = Diagnostics(await Send("-rd missing_home_probe_" + Guid.NewGuid().ToString("N")));
            Check((int?)missing["clipboardCount"] == count, "missing preset preserves shared clipboard");
            var stopped = await Send("~stop");
            var idle = Diagnostics(stopped);
            Check((bool?)idle["active"] == false && (int?)idle["objectCount"] == 0 && (int?)idle["coroutineCount"] == 0, "stop clears owned objects and coroutines");
            Check(stopped["SceneObjects"]!.ToJsonString() == sceneBaseline, "stop restores original scene transforms and active states");
            var stoppedAgain = await Send("~stop");
            Check(stoppedAgain["SceneObjects"]!.ToJsonString() == sceneBaseline, "repeated stop preserves restored scene");
            await Send("~home");
            var selection = Diagnostics(await Send("-cc part"));
            Check((bool?)selection["active"] == true && (int?)selection["objectCount"] > 0, "selection helper is owned by editor module");
            var ended = Diagnostics(await Send("-end"));
            Check((bool?)ended["active"] == true && (int?)ended["coroutineCount"] == 0 && (int?)ended["objectCount"] == (int?)entered["objectCount"], "ending selection cancels child resources and retains base home mode");
            await Send("-cc part");
            await Probe("unload");
            await Task.Delay(1200);
            var unloaded = await Probe("snapshot");
            Check((bool?)unloaded["ModuleLoaded"] == false && unloaded["SceneObjects"]!.ToJsonString() == sceneBaseline, "hot unload while active restores scene without delayed work");
            await Probe("load");
            Check((bool?)Diagnostics(await Send("~home"))["active"] == true, "reloaded DLL starts a fresh usable session");
            var final = await Send("~stop");
            Check(final["SceneObjects"]!.ToJsonString() == sceneBaseline, "reload cycle leaves original scene intact");
            Console.WriteLine($"HOME LIVE INTEGRATION: {checks} checks passed");
        }
        finally
        {
            try { await Send("~stop"); } finally { await Probe("restore"); }
        }
    }
}
