namespace Chat.Server;

/// <summary>昵称 / 频道名 / 消息内容校验。</summary>
internal static class Validation
{
    public const int MaxNickLength = 32;
    public const int MaxChannelLength = 64;

    /// <summary>
    /// 消息正文的默认字符上限（可用 <c>--max-text</c> 改）。
    /// 50000 字符按 UTF-8 最多约 200 KB，所以默认的单行上限（<see cref="Wire.DefaultLineBytes"/> = 256 KB）正好装得下。
    /// </summary>
    public const int DefaultTextChars = 50000;

    public static bool TryNick(string? raw, out string nick, out string error)
    {
        nick = (raw ?? string.Empty).Trim();

        if (nick.Length == 0)
        {
            error = "昵称不能为空";
            return false;
        }

        if (nick.Length > MaxNickLength)
        {
            error = $"昵称最长 {MaxNickLength} 个字符";
            return false;
        }

        foreach (char ch in nick)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                error = "昵称不能包含空白或控制字符";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryChannel(string? raw, out string channel, out string error)
    {
        channel = (raw ?? string.Empty).Trim().TrimStart('#');

        if (channel.Length == 0)
        {
            error = "频道名不能为空";
            return false;
        }

        if (channel.Length > MaxChannelLength)
        {
            error = $"频道名最长 {MaxChannelLength} 个字符";
            return false;
        }

        foreach (char ch in channel)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '#'))
            {
                error = "频道名只能包含字母、数字、下划线、连字符和点";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryText(string? raw, int maxChars, out string text, out string error)
    {
        text = raw ?? string.Empty;

        if (text.Length == 0)
        {
            error = "消息内容不能为空";
            return false;
        }

        if (maxChars > 0 && text.Length > maxChars)
        {
            error = $"消息最长 {maxChars} 个字符（当前 {text.Length} 个）";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
