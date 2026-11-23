using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protocol;
namespace Chat.Client
{
    public sealed class BoardChannelMessage
    {
        public string Channel { get; } public string AuthenticatedId { get; } public string Name { get; } public string Text { get; }
        internal BoardChannelMessage(string channel,string id,string name,string text) {Channel=channel;AuthenticatedId=id;Name=name;Text=text;}
    }
    public sealed partial class ChatClient
    {
        public event Action<BoardChannelMessage>? BoardMessageReceived;
        public async Task BroadcastBoardAsync(string channel,string text,CancellationToken token=default(CancellationToken))
        { await RequestAsync("BOARD",new List<(string,JsonValue)>{("channel",channel),("text",text)},token).ConfigureAwait(false); }
        private void HandleBoardLine(JsonValue p) => Raise(BoardMessageReceived,new BoardChannelMessage(p.GetString("channel")??"",p.GetString("fromId")??"",p.GetString("from")??"",p.GetString("text")??""));
    }
}
