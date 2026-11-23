using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mod.st
{
    public static class Tips
    {
        public static List<md> a = new()
{
    new("~bot", "bot模式（频道权限）"),
    new("~boom", "数字炸弹"),
    new("~五子棋", "五子棋（家园）"),
    new("~nobot", "退出bot模式"),
    new("~setQiPan", "布置五子棋棋盘(需要在家园内放置一个方块)"),
    new("~emo", "动作模式（个人权限）"),
    new("-select <组号>", "切换当前操作的组（1~8）"),
    new("-set id1 [time1] id2 [time2] ...", "添加动作到当前组，可指定每个动作的间隔（秒），不指定则使用默认间隔"),
    new("-settime <id> <time>", "修改当前组中指定 ID 的动作间隔"),
    new("-remove <id>", "从当前组移除指定 ID"),
    new("-list", "列出当前组所有动作"),
    new("-reset", "清空当前组"),
    new("-loop <0|1|on|off>", "切换循环播放模式"),
    new("-time <秒数>", "设置默认间隔（用于 time=0 的动作）"),
    new("-sv", "保存所有组到文件（emotion_groups.json）"),
    new("-rd", "从文件加载所有组"),
    new("-msg", "显示当前状态信息"),
    new("-p <id> [?]", "播放动作"),
    new("~home", "家园模式（个人权限）"),
    new("-fk", "方块编辑模式（待完善，部分可用）"),
    new("-fl 其他player名字", "移动平台（待完善，不可用）"),
    new("-cc", "复制当前场景所有家具"),
    new("-cc part", "复制部分家具"),
    new("-set", "设置固定角"),
    new("-cp <0|1>", "放置家具(是否以player为起点)"),
    new("-cp <0|1> t", "放置家具(是否以player为起点)(忽略数量不够的家具种类)"),
    new("-sv 文件名", "保存家具预设"),
    new("-sv Y 文件名", "保存地面相对高度预设"),
    new("-rd 文件名", "读取家具预设"),
    new("-rd Y 文件名", "读取地面相对高度预设"),
    new("-end", "退出小状态"),
    new("~sc", ""),
    new("-getmoon", ""),
    new("-getbox", ""),
    new("~stop", "恢复默认状态"),
    new("tp <x y z|名字|数字>", "移动到指定位置（坐标|人|列表序号）"),
    new("f [速度]", "飞高（配合蹦床可以抬人）"),
    new("m", "家具移动模式"),
    new("r <x y z>", "旋转(欧拉角)"),
    new("setcd <0|1>", "未启用"),
    new("dcmx [玩家名字]", "导出人物模型 （默认自己的模型）"),
    new("put <x y z ry>", "放置手上家具到指定位置"),
    new("gp cl_list", "清除并记录场景家具（卡公频家具bug）"),
    new("gp cl_all", "清除bug家具"),
    new("gp reput", "重放bug家具(显示bug家具,需要自己先放一个物品)"),
    new("look <1|2|3>", "改人称视角"),
    new("look fov <数字>", "改fov"),
    new("title", "名字显示"),
};

        public class md
        {
            public md(string tip, string js)
            {
                this.tip = tip;
                this.js = js;
            }
            public string tip;
            public string js;
        }
    }
}
