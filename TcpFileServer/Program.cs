using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace TcpFileServer
{
    internal static class Program
    {
        private static string _root = "";
        private static string _rootPrefix = "";
        private static TimeSpan _timeout;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // TcpFileServer.exe [port=10086] [root=./Mods] [maxConnections=64] [timeoutSeconds=30]
        // Ctrl+C or a line containing "stop" on standard input stops the server.
        private static async Task<int> Main(string[] args)
        {
            if (args.Length > 4 || !TryNumber(args, 0, 10086, 1, 65535, out int port) ||
                !TryNumber(args, 2, 64, 1, 4096, out int maxConnections) ||
                !TryNumber(args, 3, 30, 1, 3600, out int timeoutSeconds))
            {
                Console.Error.WriteLine("Usage: TcpFileServer [port=10086] [root=./Mods] [maxConnections=64] [timeoutSeconds=30]");
                return 1;
            }
            // The final opened-handle check below is a Windows security boundary.
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("[TCP] This build requires Windows for opened-file path validation.");
                return 1;
            }
            try
            {
                _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args.Length > 1
                    ? args[1] : Path.Combine(AppContext.BaseDirectory, "Mods")));
                if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
                RejectReparsePoints(_root);
                _rootPrefix = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;
                _timeout = TimeSpan.FromSeconds(timeoutSeconds);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[TCP] 根目录无效: {ex.Message}");
                return 1;
            }

            using var stopping = new CancellationTokenSource();
            var listener = new TcpListener(IPAddress.Any, port);
            var clients = new HashSet<Task>();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopping.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                listener.Start(maxConnections);
                Console.WriteLine($"[TCP] 监听 0.0.0.0:{port}; 根目录 {_root}; 最大连接 {maxConnections}; 超时 {timeoutSeconds}s");
                _ = Task.Run(() => WatchInputAsync(stopping));
                while (!stopping.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stopping.Token);
                    clients.RemoveWhere(t => t.IsCompleted);
                    if (clients.Count >= maxConnections)
                    {
                        client.Dispose(); // Do not queue unbounded work for slow/idle peers.
                        continue;
                    }
                    clients.Add(HandleAsync(client, stopping.Token));
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TCP] 监听失败: {ex.Message}");
                return 1;
            }
            finally
            {
                listener.Stop();
                stopping.Cancel();
                await Task.WhenAll(clients);
                Console.CancelKeyPress -= cancel;
                // An interactive Console.ReadLine cannot be cancelled; never wait on it at shutdown.
                Console.WriteLine("[TCP] 已停止，所有连接已关闭");
            }
            return 0;
        }

        private static bool TryNumber(string[] args, int index, int fallback, int min, int max, out int value)
        {
            value = fallback;
            return index >= args.Length || (int.TryParse(args[index], out value) && value >= min && value <= max);
        }

        private static async Task WatchInputAsync(CancellationTokenSource stopping)
        {
            try
            {
                while (await Console.In.ReadLineAsync() is string line)
                    if (line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                    {
                        stopping.Cancel();
                        return;
                    }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException) { }
        }

        private static async Task HandleAsync(TcpClient client, CancellationToken shutdown)
        {
            using (client)
            using (shutdown.Register(client.Dispose))
            {
                bool responseStarted = false;
                try
                {
                    var stream = client.GetStream();
                    string name;
                    // One deadline for the entire request, including fragmented/slow headers.
                    using (var deadline = Deadline(shutdown))
                    {
                        byte[] length = new byte[4];
                        await ReadExactAsync(stream, length, deadline.Token);
                        int count = BinaryPrimitives.ReadInt32BigEndian(length);
                        if (count <= 0 || count > 4096) throw new ArgumentException("非法文件名长度");
                        byte[] bytes = new byte[count];
                        await ReadExactAsync(stream, bytes, deadline.Token);
                        name = Utf8.GetString(bytes);
                    }
                    string full = ValidatePath(name);
                    await using var file = new FileStream(full, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    // Validate the actual opened object as well as the lexical path: a junction
                    // swapped between the path check and open must never disclose an outside file.
                    string actual = GetOpenedPath(file.SafeFileHandle);
                    if (!actual.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("非法路径");
                    RejectReparsePoints(full);
                    long remaining = file.Length;
                    if (remaining > int.MaxValue) throw new IOException("文件超出协议长度上限");
                    responseStarted = true;
                    await WriteFrameAsync(stream, 0, (int)remaining, null, shutdown);
                    byte[] buffer = new byte[65536];
                    while (remaining > 0)
                    {
                        // Each chunk has a bounded read+write deadline; large healthy transfers
                        // can continue without a total-file time limit.
                        using var deadline = Deadline(shutdown);
                        int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), deadline.Token);
                        if (read == 0) throw new EndOfStreamException("文件传输中截断");
                        await stream.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                        remaining -= read;
                    }
                }
                catch (Exception ex)
                {
                    if (!shutdown.IsCancellationRequested && !responseStarted &&
                        ex is not OperationCanceledException && ex is not EndOfStreamException)
                    {
                        try
                        {
                            bool missing = ex is FileNotFoundException || ex is DirectoryNotFoundException;
                            await WriteFrameAsync(client.GetStream(), missing ? (byte)1 : (byte)2, 0,
                                missing ? null : "请求无效或文件不可读取", shutdown);
                        }
                        catch (Exception) { /* Peer may have gone away; close the connection. */ }
                    }
                    if (!shutdown.IsCancellationRequested) Console.WriteLine($"[TCP] 请求结束: {ex.Message}");
                }
            }
        }

        private static string ValidatePath(string name)
        {
            // Forbid rooted/device/UNC paths and NTFS alternate data streams explicitly.
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0)
                throw new UnauthorizedAccessException("非法路径");
            string full = Path.GetFullPath(Path.Combine(_root, name));
            if (!full.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("非法路径");
            RejectReparsePoints(full);
            return full;
        }

        private static void RejectReparsePoints(string full)
        {
            // Include the root and its parents, not just the file's immediate directory.
            for (string? path = full; path != null; path = Path.GetDirectoryName(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("禁止重解析点路径");
            }
        }

        private static string GetOpenedPath(SafeFileHandle handle)
        {
            var buffer = new StringBuilder(512);
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length >= buffer.Capacity)
            {
                buffer.EnsureCapacity(checked((int)length + 1));
                length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            }
            if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
            string path = buffer.ToString();
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

        private static CancellationTokenSource Deadline(CancellationToken shutdown)
        {
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            deadline.CancelAfter(_timeout);
            return deadline;
        }

        // Unchanged wire format: request = int32 BE UTF8 byte count + name;
        // response = status byte (0 success, 1 missing, 2 error) + int32 BE length + payload.
        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(offset), token);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        private static async Task WriteFrameAsync(NetworkStream stream, byte status, int length, string? message, CancellationToken shutdown)
        {
            byte[]? payload = message == null ? null : Encoding.UTF8.GetBytes(message);
            byte[] header = new byte[5];
            header[0] = status;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload?.Length ?? length);
            using var deadline = Deadline(shutdown);
            await stream.WriteAsync(header.AsMemory(), deadline.Token);
            if (payload != null) await stream.WriteAsync(payload.AsMemory(), deadline.Token);
        }
    }
}
