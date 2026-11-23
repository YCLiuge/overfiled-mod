using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Chat.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class LiveSharedConnectionCheck
{
    public static async Task Run(HttpClient http, int port, string channel)
    {
        async Task Command(string text, string target = "logic")
        {
            using var body = new StringContent(JsonConvert.SerializeObject(new { command = text, target, waitMs = 2000 }), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("command", body);
            response.EnsureSuccessStatusCode();
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());
            if ((string?)result["data"]?["status"] != "completed") throw new Exception("Live shared command failed: " + result);
        }
        async Task Until(Func<bool> predicate, string label)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 15) { if (predicate()) return; await Task.Delay(40); }
            throw new Exception("LIVE SHARED timeout: " + label);
        }
        void Check(bool value, string label)
        {
            if (!value) throw new Exception("LIVE SHARED FAIL: " + label);
            Console.WriteLine("LIVE SHARED PASS: " + label);
        }
        string probeDll = Environment.GetEnvironmentVariable("LIVE_HOST_PROBE_DLL") ?? Path.GetFullPath("tools/LiveHostProbe/bin/Release/net6.0/LiveHostProbe.dll");
        if (!File.Exists(probeDll)) throw new FileNotFoundException("Build LiveHostProbe first", probeDll);
        await Command("call System.Reflection.Assembly.LoadFile " + probeDll.Replace('\\', '/'));
        async Task<JObject> Probe(string action)
        {
            string output = Path.GetFullPath("artifacts/live-host-" + action + "-" + Guid.NewGuid().ToString("N") + ".json");
            await Command("call Verification.LiveHostProbe.Run " + action + " " + output.Replace('\\', '/'));
            await Until(() => File.Exists(output), "probe " + action);
            return JObject.Parse(await File.ReadAllTextAsync(output));
        }
        using var peer = new ChatClient();
        await peer.ConnectAsync("127.0.0.1", port, "LiveSharedVerifier");
        foreach (string old in peer.Channels.ToArray()) await peer.LeaveAsync(old);
        await peer.JoinAsync(channel);
        var chats = new ConcurrentQueue<ChatMessage>();
        peer.MessageReceived += chats.Enqueue;
        bool closed = false;
        peer.GameRoomChanged += e => { if (e.Type == "closed") closed = true; };
        string name = "shared" + Guid.NewGuid().ToString("N")[..8];
        string actual = name;
        GameRoomInfo? room = null;
        bool unloaded = false;
        try
        {
            JObject before = await Probe("snapshot");
            Check((bool?)before["ChannelReady"] == true && (string?)before["DesiredChannel"] == channel, "actual host is ready in the game channel");
            await Command("dg board");await Command("dg mode guess");await Command("dg host " + name);
            await Until(() => { try { room = peer.JoinGameAsync(actual).GetAwaiter().GetResult(); return true; } catch { return false; } }, "shared native room");
            Check(room!.HostId == (string?)before["SessionId"], "Unity drawing room uses host net session identity");
            await Command("# shared-before-unload", "host");
            await Until(() => chats.Any(m => m.Text.Contains("shared-before-unload")), "chat before unload");
            JObject absent = await Probe("unload"); unloaded = true;
            await Until(() => closed, "module unload closes room");
            Check((bool?)absent["IsConnected"] == true && JToken.DeepEquals(before["SessionId"], absent["SessionId"]) && JToken.DeepEquals(before["ClientId"], absent["ClientId"]), "hot unload closes painting room and preserves exact host socket/session");
            await Command("# shared-after-unload", "host");
            await Until(() => chats.Any(m => m.Text.Contains("shared-after-unload")), "chat after unload");
            Check(true, "host chat remains usable with painting module absent");
            JObject reloaded = await Probe("reload"); unloaded = false;
            Check(JToken.DeepEquals(before["SessionId"], reloaded["SessionId"]) && JToken.DeepEquals(before["ClientId"], reloaded["ClientId"]), "hot reload retains original host client and session");
            await Command("dg board");await Command("dg mode guess");await Command("dg host " + name);
            room = null; closed = false;
            await Until(() => { try { room = peer.JoinGameAsync(actual).GetAwaiter().GetResult(); return true; } catch { return false; } }, "room after reload");
            Check(room!.HostId == (string?)before["SessionId"], "reloaded drawing module automatically borrows original connection");
            string instance = room.InstanceId;
            if (Environment.GetEnvironmentVariable("LIVE_RECONNECT_SETTLE") == "1")
            {
                await Command("net disconnect", "host");
                await Until(() => closed, "explicit disconnect closes old room");
                await Task.Delay(250);
                Console.WriteLine("LIVE SHARED NOTE: explicit disconnect settled before reconnect (diagnostic mode)");
            }
            await Command("net reconnect", "host");
            await Until(() => closed, "reconnect closes old room");
            JObject after = await Probe("snapshot");
            for (int i = 0; i < 50 && (bool?)after["ChannelReady"] != true; i++) { await Task.Delay(100); after = await Probe("snapshot"); }
            Check((bool?)after["ChannelReady"] == true && (string?)after["SessionId"] != (string?)before["SessionId"], "net reconnect creates a new session and restores game channel");
            await Command("dg board");await Command("dg mode guess");await Command("dg host " + name); room = null;
            await Until(() => { try { room = peer.JoinGameAsync(actual).GetAwaiter().GetResult(); return true; } catch { return false; } }, "new room after reconnect");
            Check(room!.HostId == (string?)after["SessionId"] && room.InstanceId != instance, "drawing follows replacement socket and creates a fresh room incarnation");
            await Command("# shared-after-reconnect", "host");
            await Until(() => chats.Any(m => m.Text.Contains("shared-after-reconnect")), "chat after reconnect");
            Check(true, "chat and drawing both remain usable after host reconnect");
            Console.WriteLine("LIVE SHARED CONNECTION VERIFICATION PASSED");
        }
        finally
        {
            if (unloaded) await Probe("reload");
            await Command("dg leave"); await Command("dg hide");
            await Probe("reset");
        }
    }
}
