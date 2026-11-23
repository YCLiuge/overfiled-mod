using Il2Cpp;
using MelonLoader;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace mod.tool
{
    internal class EmotionTool
    {
        public static EmotionTool i = new EmotionTool();
        private EmotionTool() { if (i == null) i = this; }

        // ---------- 数据 ----------
        private Dictionary<int, List<EmotionItem>> groups = new Dictionary<int, List<EmotionItem>>();
        private int currentGroup = 1;                 // 当前操作的组（1~8）
        private bool loop = false;                   // 是否循环播放
        private float defaultInterval = 0.9f;        // 动作间隔默认值（当 time=0 时使用）
        private int defaultId = 3385;                // 默认动作 ID（组为空时播放）
        private bool isPlaying = false;
        private int playingGroup = -1;
        private object playingCoroutine = null;
        private bool stopRequested = false;

        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        // ---------- 外部依赖 ----------
        public static string GetEmotionActionPath(int id)
        {
            return UnitEmotionSystem.Instance.GetEmotionActionPath(id);
        }

        public static void SaveEmotionItem(string path, List<EmotionItem> list)
        {
            if (list == null) return;
            string json = JsonConvert.SerializeObject(list, _settings);
            var fileManager = new FileManager();
            fileManager.WriteText(path, json);
        }

        public static List<EmotionItem> LoadEmotionItem(string path)
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path)) return null;
            string json = fileManager.ReadText(path);
            return JsonConvert.DeserializeObject<List<EmotionItem>>(json, _settings);
        }

        // ---------- 存储/加载所有组 ----------
        public void SaveAllGroups(string path = ConstMsg.filepath + "emotion动作配置.json")
        {
            var data = new GroupsData { Groups = groups, DefaultInterval = defaultInterval, Loop = loop, DefaultId = defaultId };
            string json = JsonConvert.SerializeObject(data, _settings);
            var fileManager = new FileManager();
            fileManager.WriteText(path, json);
            stc.Log($"所有组已保存至 {path}");
        }

        public void LoadAllGroups(string path = ConstMsg.filepath + "emotion动作配置.json")
        {
            var fileManager = new FileManager();
            if (!fileManager.Exists(path))
            {
                stc.Log($"文件 {path} 不存在");
                return;
            }
            string json = fileManager.ReadText(path);
            var data = JsonConvert.DeserializeObject<GroupsData>(json, _settings);
            if (data != null)
            {
                groups = data.Groups ?? new Dictionary<int, List<EmotionItem>>();
                defaultInterval = data.DefaultInterval;
                loop = data.Loop;
                defaultId = data.DefaultId;
                stc.Log($"从 {path} 加载所有组成功");
            }
            else
            {
                stc.Log($"加载失败，数据为空");
            }
        }

        // ---------- 播放协程 ----------
        IEnumerator PlayGroupCoroutine(int groupIndex)
        {
            isPlaying = true;
            playingGroup = groupIndex;
            stopRequested = false;

            if (!groups.TryGetValue(groupIndex, out var items) || items.Count == 0)
            {
                stc.Log($"组 {groupIndex} 为空，无法播放");
                isPlaying = false;
                playingGroup = -1;
                yield break;
            }

            do
            {
                foreach (var item in items)
                {
                    if (stopRequested)
                    {
                        stopRequested = false;
                        isPlaying = false;
                        playingGroup = -1;
                        yield break;
                    }

                    // 播放动作
                    Unit player = TipMsgCore.i.mainplayer?.GetComponent<Unit>();
                    if (player != null)
                    {
                        UnitEmotionSystem.Instance.PlayEmotionAction(player, item.id, true);
                    }

                    // 等待间隔（若 time<=0 则使用默认间隔）
                    float waitTime = item.time > 0 ? item.time : defaultInterval;
                    yield return new WaitForSeconds(waitTime);
                }
            } while (loop);

            isPlaying = false;
            playingGroup = -1;
        }

        public void PlayGroup(int groupIndex)
        {
            if (groupIndex < 1 || groupIndex > 8)
            {
                stc.Log("组号必须在 1~8 之间");
                return;
            }

            // 如果已经在播放同一组，则忽略
            if (isPlaying && playingGroup == groupIndex)
            {
                stc.Log($"组 {groupIndex} 正在播放，忽略重复触发");
                return;
            }

            // 停止当前播放
            StopPlaying();

            // 检查组是否有内容
            if (!groups.TryGetValue(groupIndex, out var items) || items.Count == 0)
            {
                // 播放默认动作（单次）
                Unit player = TipMsgCore.i.mainplayer?.GetComponent<Unit>();
                if (player != null)
                {
                    UnitEmotionSystem.Instance.PlayEmotionAction(player, defaultId, true);
                    stc.Log($"组 {groupIndex} 为空，播放默认动作 ID={defaultId}");
                }
                return;
            }

            // 正常播放组
            playingCoroutine = MelonCoroutines.Start(PlayGroupCoroutine(groupIndex));
            stc.Log($"播放组 {groupIndex}");
        }

        public void StopPlaying()
        {
            if (playingCoroutine != null)
            {
                stopRequested = true;
                MelonCoroutines.Stop(playingCoroutine);
                playingCoroutine = null;
            }
            isPlaying = false;
            playingGroup = -1;
        }

        // ---------- 主更新循环 ----------
        public void updata(string name, string msg)
        {
            // 命令处理仅当消息来自自己时执行
            if (name != TipMsgCore.i.mainplayername) msg = "0";

            var parts = msg.Split(' ');
            if (parts.Length == 0) return;

            switch (parts[0])
            {
                case "-select":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -select <组号> (1~8)");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (int.TryParse(parts[1], out int g) && g >= 1 && g <= 8)
                    {
                        currentGroup = g;
                        stc.Log($"当前操作组切换至 {currentGroup}");
                        TipMsgCore.i.OK();
                    }
                    else
                    {
                        stc.Log($"无效组号 '{parts[1]}'，请输入 1~8");
                        TipMsgCore.i.ERR();
                    }
                    break;

                case "-set":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -set id1 [time1] id2 [time2] ...");
                        TipMsgCore.i.ERR();
                        break;
                    }

                    if (!groups.ContainsKey(currentGroup))
                        groups[currentGroup] = new List<EmotionItem>();

                    for (int i = 1; i < parts.Length;)
                    {
                        if (!int.TryParse(parts[i], out int id))
                        {
                            stc.Log($"忽略无效ID '{parts[i]}'");
                            i++;
                            continue;
                        }

                        float time = 0f;
                        if (i + 1 < parts.Length && float.TryParse(parts[i + 1], out float t))
                        {
                            time = t;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }

                        // 检查动作有效性（仅警告）
                        string path = GetEmotionActionPath(id);
                        if (path == "Assets/ResBundle/.prefab")
                        {
                            stc.Log($"警告：ID {id} 动作路径无效，仍将添加");
                        }

                        groups[currentGroup].Add(new EmotionItem { id = id, time = time });
                        stc.Log($"添加动作 ID={id}, time={time}");
                    }
                    TipMsgCore.i.OK();
                    break;

                case "-settime":
                    if (parts.Length < 3)
                    {
                        stc.Log("用法: -settime <id> <time>");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (!int.TryParse(parts[1], out int setId) || !float.TryParse(parts[2], out float setTime))
                    {
                        stc.Log("参数格式错误");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (!groups.TryGetValue(currentGroup, out var list))
                    {
                        stc.Log($"当前组 {currentGroup} 为空");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    var item = list.Find(x => x.id == setId);
                    if (item == null)
                    {
                        stc.Log($"组 {currentGroup} 中没有 ID {setId}");
                        TipMsgCore.i.ERR();
                    }
                    else
                    {
                        item.time = setTime;
                        stc.Log($"ID {setId} 的间隔已设置为 {setTime}");
                        TipMsgCore.i.OK();
                    }
                    break;

                case "-remove":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -remove <id>");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (!int.TryParse(parts[1], out int remId))
                    {
                        stc.Log("无效ID");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (!groups.TryGetValue(currentGroup, out var remList))
                    {
                        stc.Log($"当前组 {currentGroup} 为空");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    int removed = remList.RemoveAll(x => x.id == remId);
                    if (removed > 0)
                        stc.Log($"从组 {currentGroup} 移除了 {removed} 个 ID {remId}");
                    else
                        stc.Log($"组 {currentGroup} 中没有 ID {remId}");
                    TipMsgCore.i.OK();
                    break;

                case "-list":
                    if (!groups.TryGetValue(currentGroup, out var showList) || showList.Count == 0)
                    {
                        stc.Log($"当前组 {currentGroup} 为空");
                    }
                    else
                    {
                        stc.Log($"当前组 {currentGroup} 共 {showList.Count} 个动作：");
                        foreach (var it in showList)
                            stc.Log($"  ID={it.id}, time={it.time}");
                    }
                    TipMsgCore.i.OK();
                    break;

                case "-reset":
                    if (!groups.ContainsKey(currentGroup))
                    {
                        stc.Log($"组 {currentGroup} 已经是空的");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    groups[currentGroup].Clear();
                    stc.Log($"已清空组 {currentGroup}");
                    TipMsgCore.i.OK();
                    break;

                case "-loop":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -loop <0|1|on|off|true|false>");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    string val = parts[1].ToLower();
                    bool newLoop;
                    if (val == "1" || val == "on" || val == "true")
                        newLoop = true;
                    else if (val == "0" || val == "off" || val == "false")
                        newLoop = false;
                    else
                    {
                        stc.Log($"无法识别参数 '{parts[1]}'");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    loop = newLoop;
                    stc.Log($"循环模式: {(loop ? "开启" : "关闭")}");
                    TipMsgCore.i.OK();
                    break;

                case "-time":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -time <秒数> (默认间隔)");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (float.TryParse(parts[1], out float defTime))
                    {
                        if (defTime < 0.1f) defTime = 0.1f;
                        defaultInterval = defTime;
                        stc.Log($"默认间隔已设置为 {defaultInterval} 秒");
                        TipMsgCore.i.OK();
                    }
                    else
                    {
                        stc.Log($"无效数字 '{parts[1]}'");
                        TipMsgCore.i.ERR();
                    }
                    break;

                case "-setdefault":
                    if (parts.Length < 2)
                    {
                        stc.Log("用法: -setdefault <id>");
                        TipMsgCore.i.ERR();
                        break;
                    }
                    if (int.TryParse(parts[1], out int newDefault))
                    {
                        defaultId = newDefault;
                        stc.Log($"默认动作 ID 已设置为 {defaultId}");
                        TipMsgCore.i.OK();
                    }
                    else
                    {
                        stc.Log("无效的 ID");
                        TipMsgCore.i.ERR();
                    }
                    break;

                case "-sv":
                    SaveAllGroups();
                    TipMsgCore.i.OK();
                    break;

                case "-rd":
                    LoadAllGroups();
                    TipMsgCore.i.OK();
                    break;

                case "-msg":
                    int total = 0;
                    foreach (var kv in groups) total += kv.Value.Count;
                    string status = isPlaying ? $"正在播放组 {playingGroup}" : "空闲";
                    stc.Log($"动作组总数: {groups.Count}\n总动作数: {total}\n当前组: {currentGroup}\n循环: {loop}\n默认间隔: {defaultInterval}\n默认动作ID: {defaultId}\n状态: {status}");
                    TipMsgCore.i.OK();
                    break;
                case "-p":
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[1],out int id))
                        {
                            string path = GetEmotionActionPath(id);
                            if (path == "Assets/ResBundle/.prefab")
                            {
                                stc.Log($"警告：ID {id} 动作路径无效，仍将添加");
                            }
                            else
                            {
                                UnitEmotionSystem.Instance.PlayEmotionAction(TipMsgCore.i.mainplayer?.GetComponent<Unit>(), id, false);
                            }
                        }
                    }
                    if (parts.Length == 3)
                    {
                        if (int.TryParse(parts[1], out int id))
                        {
                            string path = GetEmotionActionPath(id);
                            if (path == "Assets/ResBundle/.prefab")
                            {
                                stc.Log($"警告：ID {id} 动作路径无效，仍将添加");
                            }
                            else
                            {
                                UnitEmotionSystem.Instance.PlayEmotionAction(TipMsgCore.i.mainplayer?.GetComponent<Unit>(), id, true);
                            }
                        }
                    }
                    break;
                default:
                    break;
            }

            // ---------- 键盘按键处理 ----------
            for (int key = 1; key <= 8; key++)
            {
                KeyCode code = KeyCode.Alpha0 + key; // Alpha1 ~ Alpha8
                if (Input.GetKeyDown(code))
                {
                    PlayGroup(key);
                    break;
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                StopPlaying();
                stc.Log("已停止播放");
            }
        }

        // ---------- 辅助数据结构 ----------
        [System.Serializable]
        private class GroupsData
        {
            public Dictionary<int, List<EmotionItem>> Groups;
            public float DefaultInterval;
            public bool Loop;
            public int DefaultId;
        }
    }

    public class EmotionItem
    {
        public int id;
        public float time; // 0 表示使用默认间隔
        public EmotionItem() { }
        public EmotionItem(int id, float time = 0f)
        {
            this.id = id;
            this.time = time;
        }
    }
}