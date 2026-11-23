using MelonLoader;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
namespace mod.tool
{

    /// <summary>
    /// 文件存储与读取管理类
    /// 提供文本、二进制文件的同步/异步读写，以及文件操作辅助方法。
    /// </summary>
    public class FileManager
    {
        /// <summary>
        /// 默认编码，若未指定则使用 UTF-8
        /// </summary>
        public Encoding DefaultEncoding { get; set; } = Encoding.UTF8;
        public static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,   // 缩进使文件更易读
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        /// <summary>
        /// 默认缓冲区大小（字节）
        /// </summary>
        public int DefaultBufferSize { get; set; } = 4096;

        #region 文本读写（同步）

        /// <summary>
        /// 将文本写入文件（覆盖原文件）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">要写入的内容</param>
        /// <param name="encoding">编码，若为 null 则使用默认编码</param>
        public void WriteText(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            encoding ??= DefaultEncoding;
            // 确保目录存在
            EnsureDirectoryExists(path);

            using (var writer = new StreamWriter(path, false, encoding))
            {
                writer.Write(content);
            }
        }

        /// <summary>
        /// 读取文本文件全部内容
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="encoding">编码，若为 null 则使用默认编码</param>
        /// <returns>文件内容</returns>
        public string ReadText(string path, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"文件不存在: {path}");

            encoding ??= DefaultEncoding;
            using (var reader = new StreamReader(path, encoding))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// 将文本追加到文件末尾
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">要追加的内容</param>
        /// <param name="encoding">编码，若为 null 则使用默认编码</param>
        public void AppendText(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            encoding ??= DefaultEncoding;
            EnsureDirectoryExists(path);

            using (var writer = new StreamWriter(path, true, encoding))
            {
                writer.Write(content);
            }
        }

        #endregion

        #region 文本读写（异步）

        /// <summary>
        /// 异步写入文本文件（覆盖）
        /// </summary>
        public async Task WriteTextAsync(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            encoding ??= DefaultEncoding;
            EnsureDirectoryExists(path);

            using (var writer = new StreamWriter(path, false, encoding))
            {
                await writer.WriteAsync(content);
            }
        }

        /// <summary>
        /// 异步读取文本文件全部内容
        /// </summary>
        public async Task<string> ReadTextAsync(string path, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"文件不存在: {path}");

            encoding ??= DefaultEncoding;
            using (var reader = new StreamReader(path, encoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        /// <summary>
        /// 异步追加文本到文件末尾
        /// </summary>
        public async Task AppendTextAsync(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            encoding ??= DefaultEncoding;
            EnsureDirectoryExists(path);

            using (var writer = new StreamWriter(path, true, encoding))
            {
                await writer.WriteAsync(content);
            }
        }

        #endregion

        #region 二进制读写（同步）

        /// <summary>
        /// 将字节数组写入文件（覆盖）
        /// </summary>
        public void WriteBytes(string path, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            EnsureDirectoryExists(path);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize))
            {
                stream.Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// 读取二进制文件全部字节
        /// </summary>
        public byte[] ReadBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"文件不存在: {path}");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize))
            {
                var result = new byte[stream.Length];
                stream.Read(result, 0, result.Length);
                return result;
            }
        }

        #endregion

        #region 二进制读写（异步）

        /// <summary>
        /// 异步写入字节数组到文件（覆盖）
        /// </summary>
        public async Task WriteBytesAsync(string path, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            EnsureDirectoryExists(path);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true))
            {
                await stream.WriteAsync(data, 0, data.Length);
            }
        }

        /// <summary>
        /// 异步读取二进制文件全部字节
        /// </summary>
        public async Task<byte[]> ReadBytesAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"文件不存在: {path}");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, useAsync: true))
            {
                var result = new byte[stream.Length];
                await stream.ReadAsync(result, 0, result.Length);
                return result;
            }
        }

        #endregion

        #region 文件操作辅助方法

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        /// 删除文件（如果存在）
        /// </summary>
        public void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 移动文件（可跨卷）
        /// </summary>
        public void Move(string sourcePath, string destPath, bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("源路径不能为空", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(destPath))
                throw new ArgumentException("目标路径不能为空", nameof(destPath));

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"源文件不存在: {sourcePath}");

            // 若目标目录不存在则创建
            EnsureDirectoryExists(destPath);

            if (overwrite && File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(sourcePath, destPath);
        }

        /// <summary>
        /// 复制文件
        /// </summary>
        public void Copy(string sourcePath, string destPath, bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("源路径不能为空", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(destPath))
                throw new ArgumentException("目标路径不能为空", nameof(destPath));

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"源文件不存在: {sourcePath}");

            EnsureDirectoryExists(destPath);
            File.Copy(sourcePath, destPath, overwrite);
        }

        /// <summary>
        /// 确保文件所在目录存在（若不存在则创建）
        /// </summary>
        public void EnsureDirectoryExists(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        public static void Save<T>(string path, List<T> list)
        {
            if (list == null) return;
            string json = JsonConvert.SerializeObject(list, _settings);
            // 利用之前的 FileManager 写入文本
            var fileManager = new FileManager();
            fileManager.WriteText(path, json);
        }

        /// <summary>
        /// 从文件读取 List<Itempot>
        /// </summary>
        public static List<T> Load<T>(string path)
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path))
                return null;

            string json = fileManager.ReadText(path);
            return JsonConvert.DeserializeObject<List<T>>(json, _settings);
        }
        public static string ListToString<T>(List<T> list)
        {
            if (list == null) return null;
            try
            {
                return JsonConvert.SerializeObject(list, _settings);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ListToString] 序列化失败: {ex.Message}");
                return null;
            }
        }

        public static List<T> StringToList<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<List<T>>(json, _settings);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[StringToList] 反序列化失败: {ex.Message}");
                return null;
            }
        }
        #endregion
    }
}

