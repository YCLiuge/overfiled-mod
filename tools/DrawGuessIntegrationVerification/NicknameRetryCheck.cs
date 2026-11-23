using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Chat.Client;
using Chat.Protocol;
using mod.st;

internal static class NicknameRetryCheck
{
    public static async Task<int> Run(int port)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var requestId = typeof(ChatClient).GetField("_requestId", flags)!;
        var tcpField = typeof(ChatClient).GetField("_tcp", flags)!;
        long Requests(ChatClient client) => (long)requestId.GetValue(client)!;
        int checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new Exception("NICK RETRY FAIL: " + label);
            Console.WriteLine("NICK RETRY PASS: " + label); checks++;
        }
        async Task RepeatedHello(ChatClient client, Task pending)
        {
            var timer = Stopwatch.StartNew();
            while (Requests(client) < 2 && timer.Elapsed < TimeSpan.FromSeconds(3))
            {
                if (pending.IsCompleted) { await pending; throw new Exception("Expected occupied nickname retry"); }
                await Task.Delay(5);
            }
            if (Requests(client) < 2) throw new Exception("Timeout waiting for a second HELLO");
        }
        string nick = "retry" + Guid.NewGuid().ToString("N")[..12];
        using var holder = new ChatClient();
        await holder.ConnectAsync("127.0.0.1", port, nick);
        using (var immediate = new ChatClient())
        {
            ChatException? failure = null;
            try { await immediate.ConnectAsync("127.0.0.1", port, nick); }
            catch (ChatException e) { failure = e; }
            Check(failure?.Code == "nick_in_use" && Requests(immediate) == 1,
                "original four-parameter overload rejects occupied nickname without retry");
        }
        using (var bounded = new ChatClient())
        {
            var timer = Stopwatch.StartNew(); ChatException? failure = null;
            try { await bounded.ConnectAsync("127.0.0.1", port, nick, CancellationToken.None, 10); }
            catch (ChatException e) { failure = e; }
            Check(failure?.Code == "nick_in_use" && Requests(bounded) == 11 &&
                timer.Elapsed >= TimeSpan.FromMilliseconds(900) && timer.Elapsed < TimeSpan.FromSeconds(4) && holder.IsConnected,
                "persistent holder remains connected; ten retries then bounded nick_in_use failure");
        }
        using (var cancelClient = new ChatClient())
        using (var cancel = new CancellationTokenSource())
        {
            Task pending = cancelClient.ConnectAsync("127.0.0.1", port, nick, cancel.Token, 10);
            await RepeatedHello(cancelClient, pending);
            var timer = Stopwatch.StartNew(); cancel.Cancel(); bool cancelled = false;
            try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && timer.Elapsed < TimeSpan.FromMilliseconds(500) && Requests(cancelClient) < 11,
                "token cancels nickname retry promptly without using remaining attempts");
        }
        using (var invalid = new ChatClient())
        {
            ChatException? failure = null;
            try { await invalid.ConnectAsync("127.0.0.1", port, new string('x', 33), CancellationToken.None, 10); }
            catch (ChatException e) { failure = e; }
            Check(failure?.Code == "bad_nick" && Requests(invalid) == 1,
                "other HELLO errors propagate immediately without retry");
        }
        using (var invalidCount = new ChatClient())
        {
            bool rejected = false;
            try { await invalidCount.ConnectAsync("127.0.0.1", port, nick, CancellationToken.None, 11); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && Requests(invalidCount) == 0, "retry count above ten is rejected before connecting");
        }
        using (var successor = new ChatClient())
        {
            Task pending = successor.ConnectAsync("127.0.0.1", port, nick, CancellationToken.None, 10);
            await RepeatedHello(successor, pending);
            var tcp = (TcpClient)tcpField.GetValue(successor)!;
            string endpoint = tcp.Client.LocalEndPoint!.ToString()!;
            holder.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Check(ReferenceEquals(tcp, tcpField.GetValue(successor)) && tcp.Client.LocalEndPoint!.ToString() == endpoint &&
                successor.Nick == nick && !string.IsNullOrEmpty(successor.SessionId) && Requests(successor) >= 2,
                "released nickname handshakes on the same TCP socket with unchanged requested identity");
        }
        using (var hostHolder = new ChatClient())
        using (var host = new NetChater())
        {
            string hostNick = nick + "h";
            await hostHolder.ConnectAsync("127.0.0.1", port, hostNick);
            Task pending = host.ConnectAsync("127.0.0.1", port, hostNick);
            var timer = Stopwatch.StartNew();
            while (host.Client == null && !pending.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(5);
            var client = host.Client ?? throw new Exception("Host did not publish client");
            await RepeatedHello(client, pending); hostHolder.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Check(host.IsConnected && ReferenceEquals(client, host.Client) && client.Nick == hostNick,
                "production NetChater uses bounded HELLO retry when previous nickname is still reserved");
        }
        Console.WriteLine($"NICKNAME RETRY INTEGRATION: {checks} checks passed");
        return checks;
    }
}
