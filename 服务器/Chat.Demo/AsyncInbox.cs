using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Chat.Demo
{
    /// <summary>
    /// 极简异步收件箱：把后台线程收到的消息安全地交给异步的测试代码。
    /// 用它替代 System.Threading.Channels，好让示例程序在 .NET Framework 4.8 上也能编译运行。
    /// </summary>
    /// <typeparam name="T">元素类型（引用类型）。</typeparam>
    internal sealed class AsyncInbox<T> where T : class
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        /// <summary>投递一条消息（可从任意线程调用）。</summary>
        public void Add(T item)
        {
            _queue.Enqueue(item);
            _signal.Release();
        }

        /// <summary>在超时时间内取一条消息；超时返回 <c>null</c>。</summary>
        public async Task<T?> ReadAsync(TimeSpan timeout)
        {
            if (!await _signal.WaitAsync(timeout).ConfigureAwait(false))
            {
                return null;
            }

            T? item;
            return _queue.TryDequeue(out item) ? item : null;
        }
    }
}
