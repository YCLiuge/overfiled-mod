using System;
using System.Collections.Generic;
using System.Text;
using Chat.Protocol;

namespace Chat.Server;

/// <summary>
/// 单次传输（一行）的字节预算工具。
/// <para>
/// 服务器必须保证「发给任何客户端的每一行」都不超过对方配置的单行上限，
/// 否则对方会收到一条自己无法解析的超长行、被自己的 <see cref="LineReader"/> 判成
/// <c>line_too_long</c> 而断开——那就等于任何一个用户都能踢掉同频道的其他人。
/// 这里集中处理三件事：测量一行的字节数、把列表裁剪到预算内、给正文留额度。
/// </para>
/// </summary>
internal static class TransferBudget
{
    /// <summary>留给 JSON 骨架（字段名、id、时间戳等）的余量，避免把预算算得刚好卡住。</summary>
    public const int ReplyReserveBytes = 192;

    /// <summary>列表项的最小预算，保证再紧的配置也能返回点东西。</summary>
    public const int MinListBudget = 48;

    /// <summary>一行序列化后占用的字节数（含结尾换行符）。</summary>
    public static int LineBytes(string line) => Encoding.UTF8.GetByteCount(line) + 1;

    /// <summary>某个 JSON 值序列化成一行后占用的字节数（含结尾换行符）。</summary>
    public static int LineBytes(string command, JsonValue payload) => LineBytes(Wire.Serialize(command, payload));

    /// <summary>
    /// 把一个字符串列表裁剪成能塞进 <paramref name="budgetBytes"/> 的前缀。
    /// 单项成本按「UTF-8 字节数 × 2 + 3」保守估算（×2 是转义最坏情况，+3 是引号与逗号）。
    /// </summary>
    public static JsonValue FitStrings(IEnumerable<string> values, int budgetBytes, out bool truncated)
    {
        List<JsonValue> items = new();
        int used = 0;
        truncated = false;

        if (budgetBytes < MinListBudget)
        {
            budgetBytes = MinListBudget;
        }

        if (values != null)
        {
            foreach (string value in values)
            {
                int cost = (value == null ? 4 : Encoding.UTF8.GetByteCount(value) * 2) + 3;
                if (used + cost > budgetBytes)
                {
                    truncated = true;
                    break;
                }

                items.Add(JsonValue.Of(value));
                used += cost;
            }
        }

        return JsonValue.Array(items.ToArray());
    }

    /// <summary>把列表总额度减去固定开销后得到的列表预算（不会小于 <see cref="MinListBudget"/>）。</summary>
    public static int ListBudget(int maxLineBytes, int extraOverheadBytes = 0)
    {
        int budget = maxLineBytes - ReplyReserveBytes - extraOverheadBytes;

        // 列表本身只占回复的一半额度，剩下的留给 id / count / 其它字段
        budget = budget / 2;

        return budget < MinListBudget ? MinListBudget : budget;
    }
}
