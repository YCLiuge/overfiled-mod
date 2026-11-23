using ChatGuardModule;
using Mono.Cecil;

if (args.Contains("--inspect"))
{
    using var asm = AssemblyDefinition.ReadAssembly("libs/Assembly-CSharp.dll");
    foreach (var t in asm.MainModule.Types.Where(t => t.Name is "UnitPropertyGetter" or "UnitStaticPropery" or "CharacterEntity" or "Unit"))
    {
        Console.WriteLine(t.FullName);
        foreach (var p in t.Properties) Console.WriteLine(" P " + p.FullName);
        foreach (var m in t.Methods.Where(m => !m.IsGetter && !m.IsSetter && (m.Name.Contains("Player") || m.Name.Contains("Main") || m.Name.Contains("Sync")))) Console.WriteLine(" M " + m.FullName);
    }
    return;
}
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
var options = new GuardOptions();
var guard = new GuardPolicy(options);
Check(guard.Allow(1, "你好", "玩家", 0, 10), "普通中文消息");
Check(!guard.Allow(1, "你好", "玩家", 0, 10.1), "重复内容丢弃");
Check(guard.Allow(1, "你好", "玩家", 0, 13.1), "重复窗口到期恢复");
guard.Reset(options);
int accepted = 0;
for (int i = 0; i < 100000; i++) if (guard.Allow(1, "spam" + (i % 10), "攻击者", 0, 20)) accepted++;
Check(accepted == 4, "十万条单人刷屏仅放行突发额度");
Check(guard.Allow(2, "正常消息", "其他玩家", 0, 20), "单人刷屏后其他玩家仍可聊天");
Check(guard.Allow(1, "恢复", "玩家", 0, 26), "静默期后令牌随单调时间恢复");
guard.Reset(options);
accepted = 0;
for (int i = 1; i <= 10000; i++) if (guard.Allow(i, "a", "b", 0, 30)) accepted++;
Check(accepted == options.GlobalBurst, "多玩家全局限流");
Check(guard.TrackedSenders == options.GlobalBurst, "全局限流后不再分配发送者状态");
Check(!guard.ValidContent(new string('a', 513), "name"), "超长正文过滤");
Check(!guard.ValidContent("ok", new string('a', 65)), "超长昵称过滤");
Check(!guard.ValidContent("<size=99999>x</size>", "name"), "危险 TMP 标签过滤");
Check(!guard.ValidContent("ok", "<sprite=1>"), "昵称标签过滤");
Check(!guard.ValidContent("a\0b", "name"), "控制字符过滤");
Check(!guard.ValidContent(string.Join('\n', Enumerable.Repeat("x", 9)), "name"), "过多行过滤");
Check(guard.ValidContent("I <3 you\n你好\t世界", "name"), "普通符号和允许空白");
guard.Reset(options);
Check(guard.Allow(1, "", "name", 1, 0), "纯表情可接收");
Check(!guard.Allow(1, "", "name", 1, 1), "重复表情过滤");
Check(guard.Allow(1, "", "name", 2, 1), "不同表情可接收");
var high = new GuardOptions { GlobalBurst = 500, GlobalPerSecond = 500 };
guard.Reset(high);
for (int i = 0; i < 5000; i++) guard.Allow(i, "x", "n", 0, i / 500.0);
Check(guard.TrackedSenders == GuardPolicy.MaxSenders, "发送者状态硬上限");
Check(guard.Allow(999999, "x", "n", 0, 200), "过期状态回收后接收新玩家");
Check(guard.TrackedSenders == 1, "过期发送者清理");
guard.Reset(new GuardOptions { Enabled = false });
Check(guard.Allow(1, new string('x', 9999), "name", 0, 1), "关闭配置透传");
guard.Reset(options);
Check(guard.TrackedSenders == 0, "热重载重置状态");
try { new GuardOptions { PlayerPerSecond = double.NaN }.Validate(); Check(false, "非法配置"); }
catch (ArgumentException) { Check(true, "拒绝 NaN 配置"); }
using var game = AssemblyDefinition.ReadAssembly("libs/Assembly-CSharp.dll");
var chat = game.MainModule.Types.Single(t => t.FullName == "Il2CppInutan.ChatSystem");
foreach (var (method, count) in new[] { ("OnChatMsgNotice", 1), ("ShowChatMessage", 4), ("OnChatMsgRecordInitNotice", 1) })
    Check(chat.Methods.Count(m => m.Name == method && m.Parameters.Count == count) == 1, "当前游戏补丁目标: " + method);
using var module = AssemblyDefinition.ReadAssembly("GameMods/Mods/ChatGuardModule.dll");
var entry = module.MainModule.Types.Single(t => t.FullName == "ChatGuardModule.Entry");
Check(entry.Interfaces.Any(i => i.InterfaceType.FullName == "ModLoader.ILogic"), "真实宿主 ILogic 契约");
Check(module.CustomAttributes.Any(a => a.AttributeType.Name == "AssemblyMetadataAttribute" && (string)a.ConstructorArguments[0].Value == "HotUpdate.ModuleId" && (string)a.ConstructorArguments[1].Value == "chat_guard"), "热更新模块 ID 元数据");
Check(entry.Methods.Single(m => m.Name == "OnUnload").Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "UnpatchSelf"), "卸载撤销自身补丁");
var skill = new SpellGuardPolicy();
var skillOptions = new SpellGuardOptions();
skill.Reset(skillOptions);
int skillAccepted = 0;
for (int i = 0; i < 100000; i++) if (skill.Allow(1, true, 10)) skillAccepted++;
Check(skillAccepted == 12, "十万次远端施法仅放行单位突发额度");
Check(skill.Allow(2, true, 10), "不同网络单位独立额度");
Check(skill.Allow(1, false, 10), "本地施法不受远端限流影响");
Check(skill.Allow(1, true, 11), "技能额度按时间恢复");
skill.Reset(skillOptions);
skillAccepted = 0;
for (int i = 0; i < 10000; i++) if (skill.Allow(i, true, 10)) skillAccepted++;
Check(skillAccepted == 80, "技能全局预算");
Check(skill.Tracked == 80, "技能全局拒绝后不分配状态");
skill.Reset(new SpellGuardOptions { BlockAllRemote = true });
Check(!skill.Allow(1, true, 10) && skill.Allow(1, false, 10), "全屏蔽开关只影响网络单位");
skill.Reset(new SpellGuardOptions { Enabled = false, BlockAllRemote = true });
Check(skill.Allow(1, true, 10), "关闭技能保护透传");
skill.Reset(new SpellGuardOptions { GlobalBurst = 2000, GlobalPerSecond = 2000 });
for (int i = 0; i < 5000; i++) skill.Allow(i, true, i / 2000.0);
Check(skill.Tracked == 4096, "技能单位状态硬上限");
Check(skill.Allow(99999, true, 200) && skill.Tracked == 1, "技能过期状态清理");
skill.Reset(skillOptions);
Check(skill.Tracked == 0, "热重载清空技能状态");
var oldConfig = System.Text.Json.JsonSerializer.Deserialize<GuardOptions>("{\"Enabled\":true}")!;
oldConfig.Validate();
Check(oldConfig.Spell.Enabled && !oldConfig.Spell.BlockAllRemote, "旧配置自动采用技能默认值");
try { new SpellGuardOptions { UnitPerSecond = double.NaN }.Validate(); Check(false, "技能非法配置"); }
catch (ArgumentException) { Check(true, "技能拒绝 NaN 配置"); }
var spellSystem = game.MainModule.Types.Single(t => t.FullName == "Il2Cpp.SpellSystem");
var cast = spellSystem.Methods.Single(m => m.Name == "ForceCastSpell" && m.Parameters.Count == 9);
Check(cast.Parameters[7].ParameterType.FullName == "System.Boolean" && cast.Parameters[8].ParameterType.FullName == "Il2Cpp.SpellData&" && cast.ReturnType.FullName == "System.Boolean", "技能底层重载 bool/out 契约");
Check(game.MainModule.Types.Single(t => t.FullName == "Il2Cpp.UnitPropertyGetter").Properties.Any(p => p.Name == "IsNetUnit" && p.PropertyType.FullName == "System.Boolean"), "网络单位判定属性");
Check(entry.Methods.Single(m => m.Name == "CastSpell").Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "Il2Cpp.Unit", "System.Boolean&", "Il2Cpp.SpellData&" }), "技能 Prefix 返回值和 out 引用");
guard.Reset(new GuardOptions());
int payloadReads = 0, emojiPass = 0;
for (int i = 0; i < 100000; i++)
    if (guard.AllowLazy(123, (uint)i, emotion => { payloadReads++; return ("", "攻击者", emotion); }, 10)) emojiPass++;
Check(emojiPass == 4 && payloadReads == 4, "十万条不同表情仅放行并读取四条内容");
Check(!guard.AllowLazy(123, 0, _ => throw new Exception("不能读取静默消息"), 14), "持续刷表情延长静默且不读内容");
Check(!guard.AllowLazy(123, 0, _ => throw new Exception("不能读取静默消息"), 18), "有额度仍需等待静默结束");
Check(guard.Allow(456, "正常聊天", "其他玩家", 0, 18), "静默不影响其他玩家");
Check(guard.Allow(123, "", "攻击者", 100001, 23.1), "停发五秒后恢复");
guard.Reset(new GuardOptions());
payloadReads = 0;
for (int i = 0; i < 1000; i++) guard.AllowLazy(77, 0, _ => { payloadReads++; return (new string('a', 513), "n", 0u); }, 10);
Check(payloadReads == 4, "非法内容也消耗读取预算避免无限转换");
var window = new TrafficWindow();
window.Reset(100);
Check(window.Sample(110, 100, 4, 20, 10).Contains("chatRx/s=10.0 chatPass/s=0.4"), "区间速率使用增量");
Check(window.Sample(140, 130, 7, 50, 40).Contains("window=30.0s chatRx/s=1.0 chatPass/s=0.1"), "卡顿后使用实际三十秒而非固定十秒");
Check(window.Sample(140, 130, 7, 50, 40) == "rates=pending", "零时间窗口不除零");
Console.WriteLine($"All {checks} checks passed. Runtime IL2CPP patch execution still requires in-game validation.");
