using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using Chat.Protocol;

namespace Chat.Demo
{
    internal static class GameSmokeTest
    {
        public static async Task<int> RunAsync(string host, int port)
        {
            Checker checks = new Checker();
            var clients = new List<ChatClient>();
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            string room = "draw-" + tag;
            try
            {
                ChatClient owner = await Connect(host, port, "host-" + tag, clients);
                ChatClient painter = await Connect(host, port, "painter-" + tag, clients);
                ChatClient guesser = await Connect(host, port, "guesser-" + tag, clients);
                var hostInbox = new AsyncInbox<GameMessage>();
                var painterInbox = new AsyncInbox<GameMessage>();
                var guesserInbox = new AsyncInbox<GameMessage>();
                var painterEvents = new AsyncInbox<GameRoomEvent>();
                owner.GameMessageReceived += hostInbox.Add;
                painter.GameMessageReceived += painterInbox.Add;
                guesser.GameMessageReceived += guesserInbox.Add;
                painter.GameRoomChanged += painterEvents.Add;
                GameRoomInfo created = await owner.CreateGameAsync(room);
                checks.Check(owner.SessionId.Length == 32 && owner.SessionId != painter.SessionId, "server-issued distinct connection IDs");
                checks.Check(created.HostId == owner.SessionId && created.Members.Count == 1, "creator registered as sole game authority");
                await ExpectError(checks, () => painter.CreateGameAsync(room), "room_exists", "room cannot be taken over");
                GameRoomInfo joined = await painter.JoinGameAsync(room);
                await guesser.JoinGameAsync(room);
                checks.Check(joined.InstanceId == created.InstanceId && joined.HostId == owner.SessionId && joined.Members.Count == 2, "join provides full identity roster and room incarnation");
                checks.Check(await painter.SendGameAsync(room, "guess-secret") == 1, "participant default route is private to host");
                GameMessage? guess = await hostInbox.ReadAsync(TimeSpan.FromSeconds(2));
                checks.Check(guess != null && guess.Text == "guess-secret" && guess.FromId == painter.SessionId && guess.From == painter.Nick, "host receives server-stamped participant identity");
                checks.Check(await guesserInbox.ReadAsync(TimeSpan.FromMilliseconds(150)) == null, "guesses are not leaked to other participants");
                await ExpectError(checks, () => painter.SendPrivateAsync(room, guesser.SessionId, "spoof"), "host_only_route", "participant cannot route directly to peers");
                checks.Check(await owner.SendPrivateAsync(room, painter.SessionId, "word-private") == 1, "host can send word to painter privately");
                GameMessage? word = await painterInbox.ReadAsync(TimeSpan.FromSeconds(2));
                checks.Check(word != null && word.FromId == owner.SessionId && word.ToId == painter.SessionId && word.Text == "word-private", "private word carries host and destination identity");
                checks.Check(await guesserInbox.ReadAsync(TimeSpan.FromMilliseconds(150)) == null, "private word never reaches guesser");
                checks.Check(await owner.SendGameAsync(room, "state-public") == 2, "host can broadcast canonical state");
                checks.Check((await painterInbox.ReadAsync(TimeSpan.FromSeconds(2)))?.Text == "state-public" &&
                    (await guesserInbox.ReadAsync(TimeSpan.FromSeconds(2)))?.Text == "state-public", "all peers receive canonical broadcast");
                checks.Check(await hostInbox.ReadAsync(TimeSpan.FromMilliseconds(100)) == null, "broadcast has no sender echo");
                await ExpectError(checks, () => owner.SendGameAsync(room, new string('中', 11000)), "game_msg_too_large", "UTF-8 game text budget enforced without disconnect");
                checks.Check(owner.IsConnected && painter.IsConnected, "oversized request preserves healthy connections");

                using (TcpClient raw = new TcpClient())
                {
                    await raw.ConnectAsync(host, port);
                    NetworkStream stream = raw.GetStream();
                    LineReader reader = new LineReader(stream);
                    await reader.ReadLineAsync(CancellationToken.None);
                    JsonValue hello = await Raw(stream, reader, Wire.CmdHello, JsonValue.Object(("id", 1), ("nick", "raw-" + tag)));
                    string rawId = hello.GetString("sessionId");
                    await Raw(stream, reader, Wire.CmdGameJoin, JsonValue.Object(("id", 2), ("room", room)));
                    JsonValue wrongEpoch = await Raw(stream, reader, Wire.CmdGameSend, JsonValue.Object(("id", 3), ("room", room), ("instanceId", "old"), ("text", "bad")));
                    checks.Check(wrongEpoch.GetString("code") == "stale_game", "old room incarnation rejected at relay");
                    await Raw(stream, reader, Wire.CmdGameSend, JsonValue.Object(("id", 4), ("room", room),
                        ("instanceId", created.InstanceId), ("text", "forged"), ("fromId", owner.SessionId), ("from", owner.Nick)));
                    GameMessage? forged = await hostInbox.ReadAsync(TimeSpan.FromSeconds(2));
                    checks.Check(forged != null && forged.FromId == rawId && forged.FromId != owner.SessionId, "payload cannot forge sender or host identity");
                    await Raw(stream, reader, Wire.CmdGameLeave, JsonValue.Object(("id", 5), ("room", room), ("instanceId", created.InstanceId)));
                    JsonValue outsider = await Raw(stream, reader, Wire.CmdGameSend, JsonValue.Object(("id", 6), ("room", room), ("instanceId", created.InstanceId), ("text", "outsider")));
                    checks.Check(outsider.GetString("code") == "not_in_game", "non-members cannot send game payloads");
                }

                for (int i = 0; i < 13; i++)
                {
                    ChatClient peer = await Connect(host, port, "cap" + i + "-" + tag, clients);
                    await peer.JoinGameAsync(room);
                }
                ChatClient excess = await Connect(host, port, "excess-" + tag, clients);
                await ExpectError(checks, () => excess.JoinGameAsync(room), "game_full", "room limit is exactly sixteen connections");
                await guesser.LeaveGameAsync(room);
                await excess.JoinGameAsync(room);
                checks.Check(true, "leaving participant releases room capacity");
                await owner.QuitAsync("test-host-disconnect");
                GameRoomEvent? closed;
                do { closed = await painterEvents.ReadAsync(TimeSpan.FromSeconds(2)); }
                while (closed != null && closed.Type != "closed");
                checks.Check(closed != null && closed.Room.HostId == owner.SessionId && closed.Room.InstanceId == created.InstanceId, "host disconnect closes room without authority migration");
                bool closedLocally = false;
                try { await painter.SendGameAsync(room, "after-close"); } catch (InvalidOperationException) { closedLocally = true; }
                checks.Check(closedLocally, "closed event invalidates client-side routing cache");
                GameRoomInfo recreated = await painter.CreateGameAsync(room);
                checks.Check(recreated.InstanceId != created.InstanceId && recreated.HostId == painter.SessionId, "recreated name obtains new incarnation and explicit new host");
                GameRoomInfo outsiderRoom = await guesser.CreateGameAsync("other-" + tag);
                await ExpectError(checks, () => guesser.JoinGameAsync(room), "already_in_game", "single-room membership prevents accidental cross-game routing");
                await guesser.LeaveGameAsync(outsiderRoom.Room);
                await guesser.JoinGameAsync(room);
                await painter.LeaveGameAsync(room);
                checks.Check(true, "explicit host leave closes room and permits future clean reconnects");
            }
            catch (Exception ex) { checks.Check(false, "unexpected exception: " + ex); }
            finally { foreach (ChatClient client in clients) client.Dispose(); }
            return checks.Report();
        }

        private static async Task<ChatClient> Connect(string host, int port, string nick, List<ChatClient> clients)
        {
            ChatClient client = new ChatClient(); clients.Add(client);
            await client.ConnectAsync(host, port, nick);
            return client;
        }
        private static async Task ExpectError(Checker checks, Func<Task> action, string code, string label)
        {
            string actual = "none";
            try { await action(); } catch (ChatException ex) { actual = ex.Code; }
            checks.Check(actual == code, label + " (" + actual + ")");
        }
        private static async Task<JsonValue> Raw(NetworkStream stream, LineReader reader, string command, JsonValue payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Wire.Serialize(command, payload) + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync(timeout.Token);
                    if (line == null) throw new InvalidOperationException("raw connection closed");
                    string op, error; JsonValue value;
                    if (!Wire.TryParse(line, out op, out value, out error)) continue;
                    if (op == Wire.CmdPing)
                    {
                        byte[] pong = Encoding.UTF8.GetBytes("PONG\n"); await stream.WriteAsync(pong, 0, pong.Length); continue;
                    }
                    if ((op == Wire.CmdOk || op == Wire.CmdErr) && value.GetInt64("id") == payload.GetInt64("id")) return value;
                }
            }
        }
    }
}
