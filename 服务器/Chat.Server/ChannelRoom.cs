using System.Collections.Concurrent;

namespace Chat.Server;

/// <summary>一个频道（房间）及其成员。</summary>
internal sealed class ChannelRoom(string name)
{
    private readonly ConcurrentDictionary<long, ClientSession> _members = new();

    public string Name { get; } = name;

    public int Count => _members.Count;

    public bool Add(ClientSession session) => _members.TryAdd(session.Id, session);

    public bool Remove(ClientSession session) => _members.TryRemove(session.Id, out _);

    public bool Contains(long sessionId) => _members.ContainsKey(sessionId);

    /// <summary>已握手的成员昵称（按字典序）。</summary>
    public string[] Nicks()
        => [.. _members.Values
            .Where(member => member.Nick is not null)
            .Select(member => member.Nick!)
            .OrderBy(nick => nick, StringComparer.OrdinalIgnoreCase)];

    /// <summary>把一行原文投递到频道内除 <paramref name="exceptSessionId"/> 之外的所有成员，返回成功入队的人数。</summary>
    public int Broadcast(string line, long? exceptSessionId)
    {
        int delivered = 0;

        foreach (ClientSession member in _members.Values)
        {
            if (exceptSessionId == member.Id)
            {
                continue;
            }

            if (member.Send(line))
            {
                delivered++;
            }
        }

        return delivered;
    }
}
