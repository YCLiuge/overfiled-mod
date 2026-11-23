using System;
using System.Globalization;

#nullable disable

namespace Chat.Protocol
{
    /// <summary>服务器返回 <c>ERR</c> 时抛出的异常，<see cref="Code"/> 是机器可读的错误码。</summary>
    public sealed class ChatException : Exception
    {
        /// <summary>用错误码和错误描述构造。</summary>
        public ChatException(string code, string message)
            : base(message)
        {
            Code = code ?? "error";
        }

        /// <summary>错误码，例如 <c>nick_in_use</c>、<c>not_in_channel</c>。</summary>
        public string Code { get; }

        /// <summary>带错误码的文本形式。</summary>
        public override string ToString()
        {
            return "ChatException(" + Code + "): " + Message;
        }
    }
}
