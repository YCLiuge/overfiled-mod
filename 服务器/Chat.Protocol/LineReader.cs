using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable disable

namespace Chat.Protocol
{
    /// <summary>单行超过字节上限时抛出。</summary>
    public sealed class LineTooLongException : Exception
    {
        /// <summary>用触发异常的上限值构造。</summary>
        public LineTooLongException(int limit)
            : base("单行长度超过上限 " + limit + " 字节")
        {
            Limit = limit;
        }

        /// <summary>该连接允许的单行最大字节数。</summary>
        public int Limit { get; }
    }

    /// <summary>
    /// 按 <c>\n</c> 分帧的 UTF-8 行读取器。
    /// <para>
    /// 三个要点：自动去掉行尾的 <c>\r</c>（兼容 telnet 之类的工具）；强制单行字节上限，
    /// 避免对端用一条超长行把内存撑爆；保留「已收到但还没成行」的字节，不丢数据。
    /// </para>
    /// <para>本类只使用 .NET Standard 2.0 的 API，可被 .NET Framework 4.6.2+ 直接使用。</para>
    /// </summary>
    public sealed class LineReader
    {
        private const int ChunkSize = 4096;

        private readonly Stream _stream;
        private readonly byte[] _chunk = new byte[ChunkSize];
        private byte[] _line = new byte[256];
        private int _length;
        private int _chunkStart;
        private int _chunkEnd;
        private int _maxLineBytes;

        /// <summary>在指定流上创建读取器。</summary>
        /// <param name="stream">要读取的流（通常是 TCP 的 NetworkStream）。</param>
        /// <param name="maxLineBytes">单行最大字节数，超过则抛 <see cref="LineTooLongException"/>。</param>
        public LineReader(Stream stream, int maxLineBytes = Wire.DefaultLineBytes)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (maxLineBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLineBytes), "单行上限必须大于 0");
            }

            _stream = stream;
            _maxLineBytes = maxLineBytes;
        }

        /// <summary>
        /// 单行最大字节数。可以在读完握手行（WELCOME）之后按服务器下发的上限调大——
        /// 客户端一开始并不知道服务器把上限配成了多少。
        /// </summary>
        public int MaxLineBytes
        {
            get { return _maxLineBytes; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "单行上限必须大于 0");
                }

                _maxLineBytes = value;
            }
        }

        /// <summary>
        /// 读取一行（不含行尾的 <c>\n</c> / <c>\r</c>）。
        /// 返回 <c>null</c> 表示对端已关闭连接（EOF）。
        /// </summary>
        /// <exception cref="LineTooLongException">累计字节数超过 <c>maxLineBytes</c>。</exception>
        public async Task<string> ReadLineAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                int newline = Array.IndexOf(_chunk, (byte)'\n', _chunkStart, _chunkEnd - _chunkStart);
                if (newline >= 0)
                {
                    Append(_chunk, _chunkStart, newline - _chunkStart);
                    _chunkStart = newline + 1;
                    return TakeLine();
                }

                Append(_chunk, _chunkStart, _chunkEnd - _chunkStart);
                _chunkStart = 0;
                _chunkEnd = 0;

                int read = await _stream.ReadAsync(_chunk, 0, _chunk.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    // 对端关闭：把最后一段没有换行符的内容也当成一行交出去
                    return _length > 0 ? TakeLine() : null;
                }

                _chunkEnd = read;
            }
        }

        private void Append(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_length + count > _maxLineBytes)
            {
                throw new LineTooLongException(_maxLineBytes);
            }

            if (_length + count > _line.Length)
            {
                int capacity = _line.Length * 2;
                if (capacity < _length + count)
                {
                    capacity = _length + count;
                }

                Array.Resize(ref _line, capacity);
            }

            Array.Copy(data, offset, _line, _length, count);
            _length += count;
        }

        private string TakeLine()
        {
            int length = _length;
            if (length > 0 && _line[length - 1] == (byte)'\r')
            {
                length--;
            }

            _length = 0;
            return Encoding.UTF8.GetString(_line, 0, length);
        }
    }
}
