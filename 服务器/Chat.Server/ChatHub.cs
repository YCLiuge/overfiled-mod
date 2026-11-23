using System.Collections.Concurrent;

namespace Chat.Server;

/// <summary>频道与昵称的全局登记处，所有操作都是线程安全的。</summary>
internal sealed class ChatHub
{
    private readonly ConcurrentDictionary<string, ChannelRoom> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ClientSession> _nicks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>抢占昵称；返回 false 表示已被别的连接占用。</summary>
    public bool TryClaimNick(string nick, ClientSession session) => _nicks.TryAdd(nick, session);

    public void ReleaseNick(ClientSession session)
    {
        if (session.Nick is { } nick)
        {
            _nicks.TryRemove(new KeyValuePair<string, ClientSession>(nick, session));
        }
    }

    public ChannelRoom GetOrCreate(string channel)
        => _channels.GetOrAdd(channel, static name => new ChannelRoom(name));

    public bool TryGet(string channel, out ChannelRoom room)
    {
        if (_channels.TryGetValue(channel, out ChannelRoom? found))
        {
            room = found;
            return true;
        }

        room = null!;
        return false;
    }

    public ChannelRoom[] All()
        => [.. _channels.Values.OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)];

    public int OnlineCount => _nicks.Count;

    /// <summary>频道空了就回收，避免长时间运行后堆一堆空房间。</summary>
    public void RemoveIfEmpty(string channel)
    {
        if (_channels.TryGetValue(channel, out ChannelRoom? room) && room.Count == 0)
        {
            _channels.TryRemove(new KeyValuePair<string, ChannelRoom>(channel, room));
        }
    }
}
