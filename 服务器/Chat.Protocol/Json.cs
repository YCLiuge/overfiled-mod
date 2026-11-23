using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 本文件是共享源码，会被不同项目（可能开启或关闭可空引用类型）编译，
// 因此在这里显式声明自己的可空上下文，保证任何工程引用它都不会产生 CS86xx 警告。
#nullable disable

namespace Chat.Protocol
{
    /// <summary>JSON 值的种类。</summary>
    public enum JsonKind
    {
        /// <summary>不存在（字段缺失）。</summary>
        Undefined = 0,

        /// <summary><c>null</c>。</summary>
        Null,

        /// <summary>布尔值。</summary>
        Boolean,

        /// <summary>数字（本协议只使用 64 位整数）。</summary>
        Number,

        /// <summary>字符串。</summary>
        String,

        /// <summary>数组。</summary>
        Array,

        /// <summary>对象。</summary>
        Object,
    }

    /// <summary>
    /// 极简 JSON 值模型（对象 / 数组 / 字符串 / 整数 / 布尔 / null）。
    /// <para>
    /// 协议负载只需要这么点能力，所以这里不引用 System.Text.Json —— 它是 netstandard2.0 之外的
    /// 额外 NuGet 依赖，会让客户端 DLL 在 .NET Framework 上多出一堆需要绑定的程序集。
    /// 本实现无任何第三方依赖，可与 DLL 一起直接投放。
    /// </para>
    /// </summary>
    public sealed class JsonValue
    {
        private const int MaxDepth = 32;

        private static readonly JsonValue UndefinedInstance = new JsonValue(JsonKind.Undefined);
        private static readonly JsonValue NullInstance = new JsonValue(JsonKind.Null);
        private static readonly JsonValue TrueInstance = new JsonValue(JsonKind.Boolean, boolean: true);
        private static readonly JsonValue FalseInstance = new JsonValue(JsonKind.Boolean, boolean: false);
        private static readonly JsonValue[] NoItems = new JsonValue[0];
        private static readonly string[] NoStrings = new string[0];

        private readonly string _text;
        private readonly bool _boolean;
        private readonly List<JsonValue> _items;
        private readonly Dictionary<string, JsonValue> _fields;

        private JsonValue(
            JsonKind kind,
            string text = null,
            bool boolean = false,
            List<JsonValue> items = null,
            Dictionary<string, JsonValue> fields = null)
        {
            Kind = kind;
            _text = text;
            _boolean = boolean;
            _items = items;
            _fields = fields;
        }

        /// <summary>本值的种类。</summary>
        public JsonKind Kind { get; }

        /// <summary>表示「字段不存在」的空值。</summary>
        public static JsonValue Undefined
        {
            get { return UndefinedInstance; }
        }

        /// <summary>表示 <c>null</c> 的值。</summary>
        public static JsonValue Null
        {
            get { return NullInstance; }
        }

        /// <summary>布尔真。</summary>
        public static JsonValue True
        {
            get { return TrueInstance; }
        }

        /// <summary>布尔假。</summary>
        public static JsonValue False
        {
            get { return FalseInstance; }
        }

        /// <summary>是否为「字段不存在」。</summary>
        public bool IsUndefined
        {
            get { return Kind == JsonKind.Undefined; }
        }

        // ------------------------------------------------------------ 构造

        /// <summary>构造字符串值（<paramref name="value"/> 为 null 时得到 <see cref="Null"/>）。</summary>
        public static JsonValue Of(string value)
        {
            return value == null ? NullInstance : new JsonValue(JsonKind.String, value);
        }

        /// <summary>构造整数值。</summary>
        public static JsonValue Of(long value)
        {
            return new JsonValue(JsonKind.Number, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>构造布尔值。</summary>
        public static JsonValue Of(bool value)
        {
            return value ? TrueInstance : FalseInstance;
        }

        /// <summary>由字符串隐式转换，便于书写 <c>JsonValue.Object(("nick", "alice"))</c>。</summary>
        public static implicit operator JsonValue(string value)
        {
            return Of(value);
        }

        /// <summary>由 <see cref="int"/> 隐式转换。</summary>
        public static implicit operator JsonValue(int value)
        {
            return Of(value);
        }

        /// <summary>由 <see cref="long"/> 隐式转换。</summary>
        public static implicit operator JsonValue(long value)
        {
            return Of(value);
        }

        /// <summary>由 <see cref="bool"/> 隐式转换。</summary>
        public static implicit operator JsonValue(bool value)
        {
            return Of(value);
        }

        /// <summary>构造数组。</summary>
        public static JsonValue Array(params JsonValue[] items)
        {
            if (items == null || items.Length == 0)
            {
                return new JsonValue(JsonKind.Array, items: new List<JsonValue>());
            }

            return new JsonValue(JsonKind.Array, items: new List<JsonValue>(items));
        }

        /// <summary>把一批字符串构造成 JSON 字符串数组。</summary>
        public static JsonValue Strings(IEnumerable<string> values)
        {
            List<JsonValue> items = new List<JsonValue>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    items.Add(Of(value));
                }
            }

            return new JsonValue(JsonKind.Array, items: items);
        }

        /// <summary>构造对象。</summary>
        public static JsonValue Object(params (string Name, JsonValue Value)[] fields)
        {
            Dictionary<string, JsonValue> map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            if (fields != null)
            {
                foreach ((string name, JsonValue value) in fields)
                {
                    if (name == null)
                    {
                        continue;
                    }

                    map[name] = value ?? NullInstance;
                }
            }

            return new JsonValue(JsonKind.Object, fields: map);
        }

        // ------------------------------------------------------------ 读取

        /// <summary>本值作为字符串返回；不是字符串时返回 <c>null</c>。</summary>
        public string AsString()
        {
            return Kind == JsonKind.String ? _text : null;
        }

        /// <summary>本值作为 64 位整数返回；不是整数时返回 <c>null</c>。</summary>
        public long? AsInt64()
        {
            if (Kind != JsonKind.Number || _text == null)
            {
                return null;
            }

            return long.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : (long?)null;
        }

        /// <summary>本值作为布尔返回；不是布尔时返回 <c>null</c>。</summary>
        public bool? AsBoolean()
        {
            return Kind == JsonKind.Boolean ? _boolean : (bool?)null;
        }

        /// <summary>数组元素；不是数组时返回空集合。</summary>
        public IReadOnlyList<JsonValue> Items
        {
            get { return _items ?? (IReadOnlyList<JsonValue>)NoItems; }
        }

        /// <summary>按名字取字段。</summary>
        public bool TryGetProperty(string name, out JsonValue value)
        {
            if (_fields != null && name != null && _fields.TryGetValue(name, out JsonValue found))
            {
                value = found;
                return true;
            }

            value = UndefinedInstance;
            return false;
        }

        /// <summary>按名字取字段；字段不存在时返回 <see cref="Undefined"/>（不会抛异常）。</summary>
        public JsonValue this[string name]
        {
            get
            {
                JsonValue value;
                return TryGetProperty(name, out value) ? value : UndefinedInstance;
            }
        }

        /// <summary>取字符串字段；不存在或类型不符时返回 <c>null</c>。</summary>
        public string GetString(string name)
        {
            return this[name].AsString();
        }

        /// <summary>取整数字段；不存在或类型不符时返回 <c>null</c>。</summary>
        public long? GetInt64(string name)
        {
            return this[name].AsInt64();
        }

        /// <summary>取布尔字段；不存在或类型不符时返回 <c>null</c>。</summary>
        public bool? GetBoolean(string name)
        {
            return this[name].AsBoolean();
        }

        /// <summary>取字符串数组字段；不存在或类型不符时返回空数组。</summary>
        public string[] GetStringArray(string name)
        {
            JsonValue value = this[name];
            if (value.Kind != JsonKind.Array)
            {
                return NoStrings;
            }

            List<string> result = new List<string>(value.Items.Count);
            foreach (JsonValue item in value.Items)
            {
                string text = item.AsString();
                if (text != null)
                {
                    result.Add(text);
                }
            }

            return result.ToArray();
        }

        // ------------------------------------------------------------ 序列化

        /// <summary>序列化成 JSON 文本。</summary>
        public string ToJson()
        {
            StringBuilder builder = new StringBuilder();
            Write(builder);
            return builder.ToString();
        }

        /// <summary>等价于 <see cref="ToJson"/>。</summary>
        public override string ToString()
        {
            return ToJson();
        }

        private void Write(StringBuilder builder)
        {
            switch (Kind)
            {
                case JsonKind.Undefined:
                case JsonKind.Null:
                    builder.Append("null");
                    break;

                case JsonKind.Boolean:
                    builder.Append(_boolean ? "true" : "false");
                    break;

                case JsonKind.Number:
                    builder.Append(_text);
                    break;

                case JsonKind.String:
                    WriteString(builder, _text);
                    break;

                case JsonKind.Array:
                    builder.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        (_items[i] ?? NullInstance).Write(builder);
                    }

                    builder.Append(']');
                    break;

                case JsonKind.Object:
                    builder.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JsonValue> pair in _fields)
                    {
                        if (!first)
                        {
                            builder.Append(',');
                        }

                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        (pair.Value ?? NullInstance).Write(builder);
                    }

                    builder.Append('}');
                    break;
            }
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');

            if (value != null)
            {
                foreach (char ch in value)
                {
                    switch (ch)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (ch < ' ')
                            {
                                builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(ch);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }

        // ------------------------------------------------------------ 解析

        /// <summary>
        /// 解析 JSON 文本。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 null。</exception>
        /// <exception cref="FormatException">文本不是合法 JSON，或嵌套层级超过 32 层。</exception>
        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            int index = 0;
            JsonValue value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);

            if (index != text.Length)
            {
                throw new FormatException("JSON 末尾存在多余字符（位置 " + index.ToString(CultureInfo.InvariantCulture) + "）");
            }

            return value;
        }

        private static JsonValue ParseValue(string text, ref int index, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new FormatException("JSON 嵌套层级超过 " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " 层");
            }

            SkipWhitespace(text, ref index);

            if (index >= text.Length)
            {
                throw new FormatException("JSON 内容意外结束");
            }

            switch (text[index])
            {
                case '{':
                    return ParseObject(text, ref index, depth);
                case '[':
                    return ParseArray(text, ref index, depth);
                case '"':
                    return new JsonValue(JsonKind.String, ParseString(text, ref index));
                case 't':
                    Expect(text, ref index, "true");
                    return TrueInstance;
                case 'f':
                    Expect(text, ref index, "false");
                    return FalseInstance;
                case 'n':
                    Expect(text, ref index, "null");
                    return NullInstance;
                default:
                    return ParseNumber(text, ref index);
            }
        }

        private static JsonValue ParseObject(string text, ref int index, int depth)
        {
            index++; // '{'
            Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return new JsonValue(JsonKind.Object, fields: fields);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != '"')
                {
                    throw new FormatException("对象的键必须是字符串（位置 " + index.ToString(CultureInfo.InvariantCulture) + "）");
                }

                string name = ParseString(text, ref index);
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException("键之后需要 ':'（位置 " + index.ToString(CultureInfo.InvariantCulture) + "）");
                }

                index++;
                fields[name] = ParseValue(text, ref index, depth + 1);
                SkipWhitespace(text, ref index);

                if (index >= text.Length)
                {
                    throw new FormatException("对象没有闭合");
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return new JsonValue(JsonKind.Object, fields: fields);
                }

                throw new FormatException("对象里需要 ',' 或 '}'（位置 " + index.ToString(CultureInfo.InvariantCulture) + "）");
            }
        }

        private static JsonValue ParseArray(string text, ref int index, int depth)
        {
            index++; // '['
            List<JsonValue> items = new List<JsonValue>();

            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return new JsonValue(JsonKind.Array, items: items);
            }

            while (true)
            {
                items.Add(ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);

                if (index >= text.Length)
                {
                    throw new FormatException("数组没有闭合");
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return new JsonValue(JsonKind.Array, items: items);
                }

                throw new FormatException("数组里需要 ',' 或 ']'（位置 " + index.ToString(CultureInfo.InvariantCulture) + "）");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++; // 开头的引号
            StringBuilder builder = new StringBuilder();

            while (true)
            {
                if (index >= text.Length)
                {
                    throw new FormatException("字符串没有闭合");
                }

                char ch = text[index++];

                if (ch == '"')
                {
                    return builder.ToString();
                }

                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new FormatException("转义符后缺少字符");
                }

                char escaped = text[index++];
                switch (escaped)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new FormatException("\\u 转义不完整");
                        }

                        string hex = text.Substring(index, 4);
                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                        {
                            throw new FormatException("\\u 转义不是合法的十六进制: " + hex);
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException("不支持的转义字符: \\" + escaped);
                }
            }
        }

        private static JsonValue ParseNumber(string text, ref int index)
        {
            int start = index;

            if (index < text.Length && text[index] == '-')
            {
                index++;
            }

            int digitsStart = index;
            while (index < text.Length && IsNumberChar(text[index]))
            {
                index++;
            }

            if (index == digitsStart || !(text[digitsStart] >= '0' && text[digitsStart] <= '9'))
            {
                throw new FormatException("位置 " + start.ToString(CultureInfo.InvariantCulture) + " 处不是合法的 JSON 值");
            }

            return new JsonValue(JsonKind.Number, text.Substring(start, index - start));
        }

        private static bool IsNumberChar(char ch)
        {
            return (ch >= '0' && ch <= '9') || ch == '.' || ch == 'e' || ch == 'E' || ch == '+' || ch == '-';
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("位置 " + index.ToString(CultureInfo.InvariantCulture) + " 处期望 " + literal);
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char ch = text[index];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                {
                    index++;
                    continue;
                }

                return;
            }
        }
    }
}
