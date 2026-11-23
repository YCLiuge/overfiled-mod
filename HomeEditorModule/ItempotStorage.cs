using mod;
using Newtonsoft.Json;
using System.Globalization;
using mod.tool;

namespace HomeEditorModule.Core
{
    public static class ItempotStorage
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,   // 缩进使文件更易读
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        /// <summary>
        /// 存储 List<Itempot> 到文件（JSON）
        /// </summary>
        public static void SaveItempots(string path, List<Itempot> list)
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
        public static List<Itempot> LoadItempots(string path)
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path))
                return null;

            string json = fileManager.ReadText(path);
            return JsonConvert.DeserializeObject<List<Itempot>>(json, _settings);
        }
        #region 浮点数存储（文本方式，推荐）

        /// <summary>
        /// 存储单个浮点数到文件（文本格式）
        /// </summary>
        public static void SaveFloat(string path, float value)
        {
            var fileManager = new FileManager();
            string content = value.ToString(CultureInfo.InvariantCulture);
            fileManager.WriteText(path, content);
        }

        /// <summary>
        /// 从文件读取单个浮点数（文本格式）
        /// </summary>
        public static float LoadFloat(string path)
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path))
                return 0;

            string content = fileManager.ReadText(path);
            return float.Parse(content, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 存储浮点数数组到文件（每行一个数，或逗号分隔，这里每行一个）
        /// </summary>
        public static void SaveFloats(string path, float[] values)
        {
            if (values == null) return;
            var fileManager = new FileManager();
            string content = string.Join(Environment.NewLine,
                values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            fileManager.WriteText(path, content);
        }

        /// <summary>
        /// 从文件读取浮点数数组（每行一个数）
        /// </summary>
        public static float[] LoadFloats(string path)
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path))
                return null;

            string content = fileManager.ReadText(path);
            if (string.IsNullOrWhiteSpace(content))
                return new float[0];

            string[] lines = content.Split(new[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries);
            return lines.Select(line => float.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        }

        #endregion

        #region 浮点数存储（二进制方式，可选，节省空间）

        /// <summary>
        /// 存储单个浮点数到二进制文件
        /// </summary>
        public static void SaveFloatBinary(string path, float value)
        {
            var fileManager = new FileManager();
            byte[] bytes = BitConverter.GetBytes(value);
            fileManager.WriteBytes(path, bytes);
        }

        /// <summary>
        /// 从二进制文件读取单个浮点数
        /// </summary>
        public static float LoadFloatBinary(string path)
        {
            var fileManager = new FileManager();
            byte[] bytes = fileManager.ReadBytes(path);
            if (bytes.Length != 4)
                return 0;
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 存储浮点数数组到二进制文件（连续写入）
        /// </summary>
        public static void SaveFloatsBinary(string path, float[] values)
        {
            if (values == null) return;
            byte[] bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            var fileManager = new FileManager();
            fileManager.WriteBytes(path, bytes);
        }

        /// <summary>
        /// 从二进制文件读取浮点数数组
        /// </summary>
        public static float[] LoadFloatsBinary(string path)
        {
            var fileManager = new FileManager();
            byte[] bytes = fileManager.ReadBytes(path);
            if (bytes.Length % 4 != 0)
                return null;
            float[] result = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
        public static List<string> GetFileNamesInDirectory(string directoryPath, string searchPattern = "*")
        {
            if (!Directory.Exists(directoryPath))
                return new List<string>();

            // 获取所有文件路径
            string[] filePaths = Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);

            // 提取文件名（不含路径）
            return filePaths.Select(Path.GetFileName).ToList();
        }
        #endregion
    }
}