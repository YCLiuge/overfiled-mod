using System.Text;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace DeepSeekClientMod
{
    /// <summary>
    /// DeepSeek API 客户端（适配 MelonLoader Mod 环境）
    /// 单例模式，自动维护对话历史，支持多轮上下文对话
    /// 新增：长期角色扮演的历史压缩功能
    /// </summary>
    public sealed class DeepSeekClient
    {
        // ---------- 单例 ----------
        private static readonly Lazy<DeepSeekClient> _instance = new Lazy<DeepSeekClient>(() => new DeepSeekClient());
        public static DeepSeekClient Instance => _instance.Value;

        // ---------- 配置 ----------
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        // ---------- HttpClient ----------
        private static readonly HttpClient _httpClient = new HttpClient();

        // ---------- 对话历史 ----------
        private readonly List<Message> _conversationHistory = new List<Message>();
        private readonly object _historyLock = new object();

        // ---------- 状态 ----------
        private bool _isInitialized = false;
        private string _lastError = null;

        // ---------- 历史压缩配置 ----------
        private int _maxHistoryMessages = 40;        // 触发压缩的最大消息数
        private int _keepRecentMessages = 20;        // 压缩后保留的最近消息数
        private int _maxSummaryTokens = 200;         // 摘要生成的最大 token 数
        private bool _isCompressing = false;

        // ---------- 私有构造函数 ----------
        private DeepSeekClient()
        {
            // 尝试从配置文件读取 API Key
            string configPath = Path.Combine(Application.dataPath, "..", "Mods", "deepseek_config.json");
            string apiKey = null;
            string baseUrl = null;
            string model = null;

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<Config>(json);
                    apiKey = config?.ApiKey;
                    baseUrl = config?.BaseUrl;
                    model = config?.Model;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"读取配置文件失败: {ex.Message}");
                }
            }

            // 如果配置中没有，尝试环境变量
            if (string.IsNullOrEmpty(apiKey))
                apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

            if (string.IsNullOrEmpty(apiKey))
            {
                MelonLogger.Error("DeepSeek API Key 未配置！请设置环境变量 DEEPSEEK_API_KEY 或在 Mods/deepseek_config.json 中配置。");
                return;
            }

            // 设置默认值
            _apiKey = apiKey;
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://api.deepseek.com/chat/completions" : baseUrl;
            _model = string.IsNullOrEmpty(model) ? "deepseek-v4-flash" : model;

            // 设置 HttpClient 默认请求头
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _httpClient.Timeout = TimeSpan.FromSeconds(120);

            _isInitialized = true;
            MelonLogger.Msg($"DeepSeek 客户端初始化成功，BaseUrl: {_baseUrl}");
            MelonLogger.Msg($"使用模型: {_model}");
            LoadHistory();
            MelonLogger.Msg("加载对话历史");
        }

        // ---------- 属性 ----------
        public bool IsInitialized => _isInitialized;
        public string LastError => _lastError;

        // ---------- 核心方法 ----------
        /// <summary>
        /// 发送消息并获取 AI 回复（异步）
        /// </summary>
        public async Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("DeepSeek 客户端未初始化，请检查 API Key 配置。");

            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("消息不能为空", nameof(userMessage));

            // 1. 构造请求
            ChatRequest requestBody;
            lock (_historyLock)
            {
                _conversationHistory.Add(Message.NewUserMessage(userMessage));
                requestBody = new ChatRequest
                {
                    Model = _model,
                    Messages = _conversationHistory,
                    MaxTokens = 70,
                    Temperature = 0.7
                };
            }

            // 2. 序列化（使用驼峰命名）
            var json = JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            });

            MelonLogger.Msg($"请求 JSON: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(_baseUrl, content, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _lastError = $"HTTP {(int)response.StatusCode}: {responseString}";
                    MelonLogger.Error($"DeepSeek API 错误: {_lastError}");
                    return null;
                }

                var chatResponse = JsonConvert.DeserializeObject<ChatResponse>(responseString);
                if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
                {
                    _lastError = "响应中未包含任何选项";
                    return null;
                }

                var assistantReply = chatResponse.Choices[0].Message?.Content;
                if (string.IsNullOrEmpty(assistantReply))
                {
                    _lastError = "AI 回复内容为空";
                    return null;
                }

                lock (_historyLock)
                {
                    _conversationHistory.Add(Message.NewAssistantMessage(assistantReply));
                }

                _lastError = null;

                // ---------- 新增：自动压缩历史 ----------
                if (_conversationHistory.Count > _maxHistoryMessages && !_isCompressing)
                {
                    _ = Task.Run(async () =>
                    {
                        await CompressHistoryAsync(_keepRecentMessages);
                    });
                }

                return assistantReply;
            }
            catch (Exception ex)
            {
                _lastError = $"请求异常: {ex.Message}";
                MelonLogger.Error($"DeepSeek 请求异常: {_lastError}");
                return null;
            }
        }

        /// <summary>
        /// 异步请求，并在主线程执行回调（适用于需要操作 Unity API 的场景）
        /// </summary>
        public void ChatAsyncWithCallback(string userMessage, Action<string> onSuccess, Action<string> onError = null)
        {
            if (!_isInitialized)
            {
                onError?.Invoke("DeepSeek 客户端未初始化");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var result = await ChatAsync(userMessage);
                    // 回调在主线程执行
                    MelonCoroutines.Start(CallbackCoroutine(result, onSuccess, onError));
                }
                catch (Exception ex)
                {
                    MelonCoroutines.Start(CallbackCoroutine(null, null, (err) => onError?.Invoke(ex.Message)));
                }
            });
        }

        private System.Collections.IEnumerator CallbackCoroutine(string result, Action<string> onSuccess, Action<string> onError)
        {
            yield return null; // 等待一帧，确保在主线程
            if (!string.IsNullOrEmpty(result))
                onSuccess?.Invoke(result);
            else
                onError?.Invoke(_lastError ?? "未知错误");
        }

        // ---------- 历史压缩方法 ----------
        /// <summary>
        /// 生成历史摘要（用于压缩）
        /// </summary>
        private async Task<string> GenerateHistorySummaryAsync(int keepCount)
        {
            List<Message> toCompress;
            bool hasSystem = false;
            lock (_historyLock)
            {
                if (_conversationHistory.Count <= keepCount) return null;
                hasSystem = _conversationHistory.Count > 0 && _conversationHistory[0].Role == "system";
                int start = hasSystem ? 1 : 0;
                int count = _conversationHistory.Count - start - keepCount;
                if (count <= 0) return null;
                toCompress = _conversationHistory.GetRange(start, count);
            }

            // 构建摘要请求
            string historyText = string.Join("\n", toCompress.Select(m => $"{m.Role}: {m.Content}"));
            var messages = new List<Message>
            {
                Message.NewSystemMessage("请用简洁的段落总结以下对话内容，保留角色关系、关键事件和重要情节，不超过200字："),
                Message.NewUserMessage(historyText)
            };

            var summaryRequest = new ChatRequest
            {
                Model = _model,
                Messages = messages,
                MaxTokens = _maxSummaryTokens,
                Temperature = 0.3
            };

            var json = JsonConvert.SerializeObject(summaryRequest, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(_baseUrl, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MelonLogger.Error($"摘要生成失败: {responseString}");
                    return null;
                }

                var chatResponse = JsonConvert.DeserializeObject<ChatResponse>(responseString);
                return chatResponse?.Choices?[0]?.Message?.Content;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"摘要生成异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 执行历史压缩（自动调用）
        /// </summary>
        private async Task CompressHistoryAsync(int keepCount)
        {
            if (_isCompressing) return;
            _isCompressing = true;
            try
            {
                var summary = await GenerateHistorySummaryAsync(keepCount);
                if (string.IsNullOrEmpty(summary))
                {
                    MelonLogger.Warning("摘要生成失败，跳过压缩");
                    return;
                }

                lock (_historyLock)
                {
                    if (_conversationHistory.Count <= keepCount) return;

                    // 保留系统提示（如果有）
                    bool hasSystem = _conversationHistory[0].Role == "system";
                    int start = hasSystem ? 1 : 0;

                    // 移除除最近 keepCount 条外的所有消息
                    int removeCount = _conversationHistory.Count - start - keepCount;
                    if (removeCount > 0)
                        _conversationHistory.RemoveRange(start, removeCount);

                    // 将摘要作为系统消息插入（在系统提示之后）
                    if (hasSystem)
                        _conversationHistory.Insert(1, Message.NewSystemMessage($"【历史摘要】{summary}"));
                    else
                        _conversationHistory.Insert(0, Message.NewSystemMessage($"【历史摘要】{summary}"));
                }

                MelonLogger.Msg($"对话历史已压缩：保留最近 {keepCount} 条消息 + 摘要");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"压缩历史异常: {ex.Message}");
            }
            finally
            {
                _isCompressing = false;
            }
        }

        /// <summary>
        /// 手动触发压缩（供外部调用）
        /// </summary>
        public async Task ManualCompressAsync(int keepCount = 20)
        {
            await CompressHistoryAsync(keepCount);
        }

        // ---------- 历史管理 ----------
        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _conversationHistory.Clear();
            }
        }

        public void SetSystemMessage(string content)
        {
            lock (_historyLock)
            {
                _conversationHistory.RemoveAll(m => m.Role == "system");
                if (!string.IsNullOrEmpty(content))
                    _conversationHistory.Insert(0, Message.NewSystemMessage(content));
            }
        }

        public IReadOnlyList<Message> GetHistory()
        {
            lock (_historyLock)
            {
                return _conversationHistory.AsReadOnly();
            }
        }

        // ---------- 释放资源 ----------
        public void Dispose()
        {
            
            _httpClient?.Dispose();
            MelonLogger.Msg("DeepSeek 客户端已释放");
        }
        public void SaveHistory(string filePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                filePath = Path.Combine(Application.dataPath, "..", "Mods", "chat_history.json");

            List<Message> historyCopy;
            lock (_historyLock)
            {
                historyCopy = new List<Message>(_conversationHistory);
            }

            string json = JsonConvert.SerializeObject(historyCopy, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            File.WriteAllText(filePath, json);
            MelonLogger.Msg($"对话历史已保存至 {filePath}");
        }
        public void LoadHistory(string filePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                filePath = Path.Combine(Application.dataPath, "..", "Mods", "chat_history.json");

            if (!File.Exists(filePath))
            {
                MelonLogger.Warning($"历史文件不存在: {filePath}");
                return;
            }

            string json = File.ReadAllText(filePath);
            var history = JsonConvert.DeserializeObject<List<Message>>(json, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            if (history == null || history.Count == 0)
            {
                MelonLogger.Warning("历史文件为空或无效");
                return;
            }

            lock (_historyLock)
            {
                _conversationHistory.Clear();
                _conversationHistory.AddRange(history);
            }
            MelonLogger.Msg($"已加载 {history.Count} 条历史消息");
        }
    }

    // ---------- 数据模型 ----------
    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }

        public static Message NewUserMessage(string content) => new Message { Role = "user", Content = content };
        public static Message NewAssistantMessage(string content) => new Message { Role = "assistant", Content = content };
        public static Message NewSystemMessage(string content) => new Message { Role = "system", Content = content };
    }

    public class ChatRequest
    {
        public string Model { get; set; }
        public List<Message> Messages { get; set; }
        public int? MaxTokens { get; set; }
        public double? Temperature { get; set; }
    }

    public class ChatResponse
    {
        public List<Choice> Choices { get; set; }
    }

    public class Choice
    {
        public int Index { get; set; }
        public Message Message { get; set; }
        public string FinishReason { get; set; }
    }

    // ---------- 配置文件模型 ----------
    internal class Config
    {
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
    }
}