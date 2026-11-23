using System.Net;
using System.Net.Sockets;
using System.Text;
using Chat.Client;
using Chat.Protocol;
using Chat.Server;

internal static class Program
{
    private static int _checks;

    public static async Task<int> Main()
    {
        // An ephemeral, in-process production server: no replacement service remains running.
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var stop = new CancellationTokenSource();
        var server = new ChatTcpServer(new ServerOptions { Host = "127.0.0.1", Port = port, HeartbeatSeconds = 0 }, new ChatHub());
        Task running = server.RunAsync(stop.Token);
        try
        {
            await VerifyIsolation(port).WaitAsync(TimeSpan.FromSeconds(40));
            await VerifyClientMetadata(port).WaitAsync(TimeSpan.FromSeconds(10));
            Check(await Chat.Demo.GameSmokeTest.RunAsync("127.0.0.1", port) == 0, "legacy general-channel game smoke test");
            Console.WriteLine($"CHANNEL RELAY PASS: {_checks} checks plus existing game smoke test");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CHANNEL RELAY FAILED: " + ex);
            return 1;
        }
        finally
        {
            await stop.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task VerifyIsolation(int port)
    {
        using var a = await Peer.Connect(port, "channel-a-host");
        using var ag = await Peer.Connect(port, "channel-a-guest");
        using var b = await Peer.Connect(port, "channel-b-host");
        using var bg = await Peer.Connect(port, "channel-b-guest");
        await b.MoveTo("beta");
        await bg.MoveTo("BETA");
        const string room = "shared-room";
        Packet made = await a.Request(Wire.CmdGameCreate, ("room", room));
        JsonValue ar = Room(made);
        string ai = ar.GetString("instanceId")!;
        Check(ar.GetString("channel") == "general" && ar.GetString("hostId") == a.Id, "create snapshot binds authenticated connection's channel");
        JsonValue aj = Room(await ag.Request(Wire.CmdGameJoin, ("room", room)));
        Check(aj.GetString("channel") == "general" && aj.GetString("instanceId") == ai, "same-channel join returns channel and matching incarnation");
        await Error(b, Wire.CmdGameJoin, "game_not_found", ("room", room), ("channel", "general"));
        await Error(b, Wire.CmdGameSend, "game_not_found", ("room", room), ("instanceId", ai), ("text", "intrude"), ("channel", "general"));
        await Error(b, Wire.CmdGameLeave, "not_in_game", ("room", room), ("instanceId", ai));
        await Error(b, Wire.CmdGameJoin, "game_not_found", ("room", "nonexistent"));
        Check(!(await b.Drain()).Any(IsGamePacket), "outsider cannot read room metadata or traffic");

        JsonValue br = Room(await b.Request(Wire.CmdGameCreate, ("room", room), ("channel", "general")));
        string bi = br.GetString("instanceId")!;
        Check(br.GetString("channel") == "beta" && bi != ai && br.GetString("hostId") == b.Id,
            "identical room name coexists in a separate channel; payload channel cannot select scope");
        JsonValue bj = Room(await bg.Request(Wire.CmdGameJoin, ("room", "SHARED-ROOM")));
        Check(bj.GetString("instanceId") == bi && string.Equals(bj.GetString("channel"), "beta", StringComparison.OrdinalIgnoreCase),
            "channel and room comparisons preserve case-insensitive chat behavior");
        await Error(bg, Wire.CmdGameSend, "stale_game", ("room", room), ("instanceId", ai), ("text", "wrong-incarnation"));
        await Error(b, Wire.CmdGameLeave, "stale_game", ("room", room), ("instanceId", ai));
        await a.Drain(); await ag.Drain(); await b.Drain(); await bg.Drain();

        Packet sent = await ag.Request(Wire.CmdGameSend, ("room", room), ("instanceId", ai), ("text", "private-guess"));
        Check(sent.Command == Wire.CmdOk && sent.Payload.GetInt64("delivered") == 1, "same-channel guest sends privately to host");
        Packet[] hostRead = await a.Drain();
        Check(hostRead.Count(p => p.Command == Wire.CmdGameMessage && p.Payload.GetString("text") == "private-guess" &&
            p.Payload.GetString("fromId") == ag.Id) == 1, "host reads same-channel message with server identity");
        Check(!(await b.Drain()).Any(IsGamePacket) && !(await bg.Drain()).Any(IsGamePacket), "guest message cannot cross into identically named room");
        await a.Request(Wire.CmdGameSend, ("room", room), ("instanceId", ai), ("text", "alpha-state"));
        await b.Request(Wire.CmdGameSend, ("room", room), ("instanceId", bi), ("text", "beta-state"));
        Packet[] alphaRead = await ag.Drain();
        Packet[] betaRead = await bg.Drain();
        Check(alphaRead.Count(p => p.Command == Wire.CmdGameMessage) == 1 && alphaRead.Single(p => p.Command == Wire.CmdGameMessage).Payload.GetString("text") == "alpha-state",
            "general guest reads only its own room's broadcast");
        Check(betaRead.Count(p => p.Command == Wire.CmdGameMessage) == 1 && betaRead.Single(p => p.Command == Wire.CmdGameMessage).Payload.GetString("text") == "beta-state",
            "beta guest reads only its own room's broadcast");
        await Error(a, Wire.CmdGameSend, "recipient_not_in_game", ("room", room), ("instanceId", ai), ("text", "direct-cross"), ("toId", bg.Id));
        Check(!(await bg.Drain()).Any(IsGamePacket), "explicit foreign recipient cannot receive a game packet");

        await ag.Request(Wire.CmdJoin, ("channel", "extra"));
        await Error(ag, Wire.CmdGameCreate, "single_channel_required", ("room", "new-room"));
        await Error(ag, Wire.CmdGameJoin, "single_channel_required", ("room", room));
        await Error(ag, Wire.CmdGameSend, "single_channel_required", ("room", room), ("instanceId", ai), ("text", "ambiguous"));
        Check((await ag.Request(Wire.CmdGameLeave, ("room", room), ("instanceId", ai))).Command == Wire.CmdOk,
            "GAME_LEAVE succeeds for cleanup after channel set changes");
        await ag.Request(Wire.CmdLeave, ("channel", "extra"));
        await ag.Request(Wire.CmdGameJoin, ("room", room));
        await ag.Request(Wire.CmdJoin, ("channel", "unrelated"));
        await ag.Request(Wire.CmdLeave, ("channel", "unrelated"));
        Check((await ag.Request(Wire.CmdGameSend, ("room", room), ("instanceId", ai), ("text", "still-member"))).Command == Wire.CmdOk,
            "leaving an unrelated chat channel preserves the corresponding game membership");
        await a.Drain(); await ag.Drain();

        await ag.Request(Wire.CmdLeave, ("channel", "GENERAL"));
        Packet[] guestLeft = await a.Drain();
        Check(guestLeft.Any(p => p.Command == Wire.CmdGameEvent && p.Payload.GetString("type") == "leave" &&
            p.Payload.GetString("memberId") == ag.Id && RoomSnapshot(p).GetString("channel") == "general" &&
            MemberCount(p) == 1),
            "LEAVE chat channel removes guest and emits scoped game roster");
        await Error(ag, Wire.CmdGameCreate, "single_channel_required", ("room", "zero-channel"));
        await Error(ag, Wire.CmdGameJoin, "single_channel_required", ("room", room));
        await Error(ag, Wire.CmdGameSend, "single_channel_required", ("room", room), ("instanceId", ai), ("text", "zero-channel"));
        await ag.Request(Wire.CmdJoin, ("channel", "beta"));
        await Error(ag, Wire.CmdGameSend, "not_in_game", ("room", room), ("instanceId", bi), ("text", "removed-guest"));
        JsonValue guestRecreated = Room(await ag.Request(Wire.CmdGameCreate, ("room", "guest-cleaned")));
        await ag.Request(Wire.CmdGameLeave, ("room", "guest-cleaned"), ("instanceId", guestRecreated.GetString("instanceId")!));
        Check((await a.Request(Wire.CmdGameSend, ("room", room), ("instanceId", ai), ("text", "after-guest-leave"))).Payload.GetInt64("delivered") == 0,
            "removed guest is absent from routing");
        Check(!(await ag.Drain()).Any(IsGamePacket), "former guest in another channel receives no game traffic");

        await a.Request(Wire.CmdLeave, ("channel", "general"));
        await a.Request(Wire.CmdJoin, ("channel", "beta"));
        Room(await a.Request(Wire.CmdGameJoin, ("room", room)));
        await a.Drain(); await b.Drain(); await bg.Drain();
        await b.Request(Wire.CmdLeave, ("channel", "beta"));
        Packet[] closedA = await a.Drain();
        Packet[] closedBg = await bg.Drain();
        Check(closedA.Any(p => p.Command == Wire.CmdGameEvent && p.Payload.GetString("type") == "closed" && RoomSnapshot(p).GetString("instanceId") == bi) &&
            closedBg.Any(p => p.Command == Wire.CmdGameEvent && p.Payload.GetString("type") == "closed" && RoomSnapshot(p).GetString("channel") == "beta"),
            "host LEAVE chat channel closes room for remaining members");
        await Error(bg, Wire.CmdGameJoin, "game_not_found", ("room", room));
        JsonValue afterClose = Room(await bg.Request(Wire.CmdGameCreate, ("room", room)));
        Check(afterClose.GetString("instanceId") != bi, "host channel leave frees room name and all member registrations");
        await b.Request(Wire.CmdJoin, ("channel", "general"));
        JsonValue alphaRecreated = Room(await b.Request(Wire.CmdGameCreate, ("room", room)));
        Check(alphaRecreated.GetString("instanceId") != ai, "host leaving general also freed its independent room");

        // Concurrent sends and channel leaves exercise gate ordering over real TCP; all awaits are bounded.
        JsonValue previous = alphaRecreated;
        for (int i = 0; i < 12; i++)
        {
            await a.MoveTo("general");
            string raceRoom = "race-" + i;
            await b.Request(Wire.CmdGameLeave, ("room", previous.GetString("room")!), ("instanceId", previous.GetString("instanceId")!));
            JsonValue race = Room(await b.Request(Wire.CmdGameCreate, ("room", raceRoom)));
            previous = race;
            await a.Request(Wire.CmdGameJoin, ("room", raceRoom));
            await Task.WhenAll(
                b.Request(Wire.CmdGameSend, ("room", raceRoom), ("instanceId", race.GetString("instanceId")!), ("text", "racing")),
                a.Request(Wire.CmdLeave, ("channel", "general"))).WaitAsync(TimeSpan.FromSeconds(3));
            Check((await b.Request(Wire.CmdGameSend, ("room", raceRoom), ("instanceId", race.GetString("instanceId")!), ("text", "after-barrier"))).Payload.GetInt64("delivered") == 0,
                "concurrent send/channel leave completes without stale recipient or lock wait " + i);
        }
    }

    private static async Task VerifyClientMetadata(int port)
    {
        var legacy = new GameRoomInfo("old", "epoch", "host", "nick", Array.Empty<GameMember>());
        Check(legacy.Channel == string.Empty, "existing GameRoomInfo constructor callers retain empty default channel");
        using var host = new ChatClient();
        using var guest = new ChatClient();
        await host.ConnectAsync("127.0.0.1", port, "typed-channel-host");
        await guest.ConnectAsync("127.0.0.1", port, "typed-channel-guest");
        await host.LeaveAsync("general"); await guest.LeaveAsync("general");
        await host.JoinAsync("typed"); await guest.JoinAsync("typed");
        GameRoomInfo created = await host.CreateGameAsync("typed-room");
        GameRoomInfo joined = await guest.JoinGameAsync("typed-room");
        Check(created.Channel == "typed" && joined.Channel == "typed", "ChatClient parser exposes Channel on create and join snapshots");
        var closed = new TaskCompletionSource<GameRoomEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.GameRoomChanged += e => { if (e.Type == "closed") closed.TrySetResult(e); };
        await host.LeaveAsync("typed");
        GameRoomEvent ev = await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(ev.Room.Channel == "typed" && ev.Reason == "channel_left", "ChatClient event parser exposes scoped host channel-leave closure");
    }

    private static async Task Error(Peer peer, string command, string code, params (string, JsonValue)[] fields)
    {
        Packet result = await peer.Request(command, fields);
        Check(result.Command == Wire.CmdErr && result.Payload.GetString("code") == code && !result.Payload.TryGetProperty("room", out _),
            command + " rejects with " + code + " and no room metadata");
    }

    private static JsonValue Room(Packet packet)
    {
        if (packet.Command != Wire.CmdOk || !packet.Payload.TryGetProperty("room", out JsonValue room))
            throw new InvalidOperationException("Expected room snapshot, got " + packet.Command + " " + packet.Payload.ToJson());
        return room;
    }
    private static JsonValue RoomSnapshot(Packet packet) => packet.Payload.TryGetProperty("room", out JsonValue room) ? room : JsonValue.Undefined;
    private static int MemberCount(Packet packet) => RoomSnapshot(packet).TryGetProperty("members", out JsonValue members) ? members.Items.Count() : -1;
    private static bool IsGamePacket(Packet p) => p.Command == Wire.CmdGameEvent || p.Command == Wire.CmdGameMessage;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        _checks++;
        Console.WriteLine("PASS: " + label);
    }

    private sealed record Packet(string Command, JsonValue Payload);

    private sealed class Peer(TcpClient tcp) : IDisposable
    {
        private readonly NetworkStream _stream = tcp.GetStream();
        private readonly LineReader _reader = new(tcp.GetStream());
        private readonly List<Packet> _received = [];
        private long _sequence;
        public string Id { get; private set; } = "";
        public static async Task<Peer> Connect(int port, string nick)
        {
            var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var peer = new Peer(tcp);
            Packet hello = await peer.Request(Wire.CmdHello, ("nick", nick));
            if (hello.Command != Wire.CmdOk) throw new InvalidOperationException(hello.Payload.ToJson());
            peer.Id = hello.Payload.GetString("sessionId")!;
            return peer;
        }
        public async Task<Packet> Request(string command, params (string, JsonValue)[] fields)
        {
            long id = ++_sequence;
            var values = new List<(string, JsonValue)> { ("id", id) };
            values.AddRange(fields);
            byte[] bytes = Encoding.UTF8.GetBytes(Wire.Serialize(command, JsonValue.Object(values.ToArray())) + "\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _stream.WriteAsync(bytes, timeout.Token);
            while (true)
            {
                string? line = await _reader.ReadLineAsync(timeout.Token);
                if (line == null) throw new IOException("TCP peer closed before reply");
                if (!Wire.TryParse(line, out string op, out JsonValue value, out string error)) throw new IOException(error);
                var packet = new Packet(op, value);
                if ((op == Wire.CmdOk || op == Wire.CmdErr || op == Wire.CmdPong) && value.GetInt64("id") == id)
                    return packet;
                _received.Add(packet);
            }
        }
        public async Task<Packet[]> Drain()
        {
            // A PING response is queued after earlier deliveries to this peer; no negative-assertion sleeps.
            await Request(Wire.CmdPing);
            Packet[] packets = _received.ToArray();
            _received.Clear();
            return packets;
        }
        public async Task MoveTo(string channel)
        {
            Packet list = await Request(Wire.CmdList);
            foreach (string joined in list.Payload.GetStringArray("joined"))
                if (!string.Equals(channel, joined, StringComparison.OrdinalIgnoreCase))
                    await Request(Wire.CmdLeave, ("channel", joined));
            await Request(Wire.CmdJoin, ("channel", channel));
        }
        public void Dispose() => tcp.Dispose();
    }
}
