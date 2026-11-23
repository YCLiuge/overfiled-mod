

namespace mod
{
    internal class Game_Boom
    {
        public static Game_Boom i = new();
        Game_Boom() { if (i == null) i = this; }
        int num = 0;
        int minn = 0;
        int maxn = 1000;
        public void Start()
        {
            num = (int)UnityEngine.Random.Range(0,1000);
            maxn = 1000;
            minn = 0;
            plys.Clear();
        }
        List<string> plys = new();
        public void Start0(string name,string msg)
        {
            
            if (plys.Count==0 && msg == "114")
            {
                plys.Add(name);
                TipMsgCore.i.Send("发送514加入队伍");
            }
            else
            {
                if (msg == "514"&& plys.Count != 0)
                {
                    bool boo = false;
                    foreach (string s in plys) { if (s == name) { boo = true; break; } }
                    if (boo) return;

                    plys.Add(name);
                    string n = "";
                    foreach (string s in plys)
                    {
                        n += (s + ",");
                    }
                    TipMsgCore.i.Send("当前队伍：" + n);
                }
                if(msg == "0721" && plys.Count != 0)
                {
                    if (name == plys[0])
                    {
                        TipMsgCore.Stute = Stute_tokensys.Booming;
                        TipMsgCore.i.Send("游戏开始猜数字0~1000");
                    }
                }
            }

        }
        int index = 0;
        public void Updatamsg(string name,string msg)
        {
            bool boo = false;
            foreach (string s in plys) { if (s == name) { boo = true;break; } }

            if(int.TryParse(msg,out int a)&&boo)
            {
                if (plys[index] == name)
                {
                    index++;
                    index %= plys.Count;
                }
                else
                {
                    TipMsgCore.i.Send("当前轮该" + plys[index] + "进行");
                    return;
                }
                if (a == num)
                {
                    TipMsgCore.i.Send("恭喜!"+name+"!? 爆炸了");
                    TipMsgCore.Stute = Stute_tokensys.None;
                    index = 0;
                }
                else
                {
                    if (a <= minn || a >= maxn)
                    {
                        TipMsgCore.i.Send("超出范围！");
                        if (index != 0) index--;
                        else index = plys.Count - 1;
                        return;
                    }
                    if (a > num) maxn = a;
                    if (a < num) minn = a;
                    TipMsgCore.i.Send("当前范围" + minn + "~" + maxn);
                }
            }
        }
    }
}
