using Chat.Client;
using DeepSeekClientMod;
using Il2Cpp;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using Il2CppTMPro;
using MelonLoader;
using mod.st;
using mod.tool;
using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using UnityEngine;
using UnityEngine.SceneManagement;
using NetChater = mod.st.NetChater;
namespace mod
{
    /// <summary>
    /// Unit.UnitEntitys//EntitysDefine.MoveEntity//UnitMoveEntity.StandGroundPosition
    /// </summary>
    public enum Stute_tokensys
    {
        None,
        
        Qizi5sta,
        Qizi5ing,
        SetQiPansta,
        SetQiPaning,
        ClearQiPaning,
        
        Boomsta,
        Boomsta0,
        Booming,

        HomeSeting,
        //AutoSetHomeitem
        emo,

        Scenseing,

            
    }
    public class TipMsgCore
    {
       
        public static TipMsgCore i = new();
        public static Stute_tokensys Stute = Stute_tokensys.None;
        bool usebot = false;
        private TipMsgCore() { if (i == null) i = this; }
        public Dictionary<string, Transform> players=new();
        List<string> playernames=new();
        public KinematicCharacterMotor mainplayer;
        public string mainplayername;
        int index = 0;
        public string Message;
        public string MsgName;
        public NetChater _chat => NetChater.Instance;
        public static event Action<string,string> Tip_actions;
        public void Init()
        {
            players.Clear();
            playernames.Clear();
            foreach (var name in PlayerSystem.Instance.PlayerEntities.Values)
            {
                playernames.Add(name.PlayerModel.NickName);
            }

            for (int i = 0; i < GamobjectCheker.i.obs[0].transform.childCount; i++)
            {
                var a = GamobjectCheker.i.obs[0].transform.GetChild(i).FindChild("ActiveRoot/DisplayRoot/OffsetRoot/StateRoot/名字文字").GetComponent<TextMeshProUGUI>().text;
                var b = GamobjectCheker.i.obs[0].transform.GetChild(i).GetComponent<UGUI_Follower>().field_Public_Transform_0;
                if (b == null) b = GamobjectCheker.i.obs[0].transform.GetChild(i).GetComponent<UGUI_Follower>().field_Public_Transform_1;
                if (b == null) b = GamobjectCheker.i.obs[0].transform.GetChild(i).GetComponent<UGUI_Follower>().field_Public_Transform_2;

                if (a == null) stc.Log("a null");
                if (b == null) stc.Log("b null");
                foreach (var c in playernames)
                {
                    if (b.root.GetComponent<KinematicCharacterMotor>() != null) b = b.root;
                    if (a == c) players.TryAdd(a, b);
                }
            }
            mainplayername = PlayerSystem.Instance.PlayerEntities[PlayerSystem.Instance.MainPlayerID].PlayerModel.NickName;
            if (mainplayer==null)mainplayer = players[mainplayername].root.GetComponent<KinematicCharacterMotor>();
            
        }
        bool isonce = false;
        void once()
        {
            _chat.BaseNick = mainplayername;

            // 订阅事件
            _chat.OnMessageReceived += OnChatMessage;
            _chat.OnChannelEvent += OnChatEvent;
            _chat.OnConnectionChanged += OnChatConnected;

            var previousRoot = NetPlayer.root;
            NetPlayer.Instance.start();
            if (previousRoot != null) UnityEngine.Object.Destroy(previousRoot);
            if (!_chat.IsConnected && !_chat.IsConnecting) _chat.Connect(nick: mainplayername);
            else _chat.RefreshRoster();
        }
        public void updata()
        { 
            _chat.Tick();
            if (UIChatSystem.Instance == null) return;
            if (UIChatSystem.Instance.ChatModel == null) return;
            if (PlayerSystem.Instance == null) return;
            if (SceneManager.GetActiveScene().name != "Home_Day_001_Modify") return;

            if (players.Count != PlayerSystem.Instance.PlayerEntities.Count || mainplayer == null)
            {
                Init();
                if (!isonce)
                {
                    once();
                    isonce = true;
                }
            }
            NetPlayer.Instance.updata();

            var text = mainplayer.transform.position;
            if (index != UIChatSystem.Instance.ChatModel.ChatMessages[eChatChannelType.Current].Count)
            {
                //UIChatSystem.
                index = UIChatSystem.Instance.ChatModel.ChatMessages[eChatChannelType.Current].Count;
                var a = UIChatSystem.Instance.ChatModel.ChatMessages[eChatChannelType.Current];
                if (index > 0)
                {
                    command(a[index - 1].PlayerNickName, a[index - 1].Message, a[index - 1].ChatMessageType);
                    Message = a[index - 1].Message;
                    MsgName = a[index - 1].PlayerNickName;
                    //test
                    stc.Log(MsgName + " " + Message + " " + a[index - 1].ChatMessageType);
                }
                if (index >= 499) { UIChatSystem.Instance.ChatModel.ChatMessages[eChatChannelType.Current].Clear(); }
            }
            updatecommand(MsgName, Message);
            Message = "";
            //if (IHomeSystem.sys.homeDataModel != null) IHomeSystem.sys.homeDataModel.FurnitureNum = 0;
        }
        public void updatecommand(string MsgName,string Message)
        {
            if (Stute == Stute_tokensys.Qizi5ing) Game_Qizi5.i.game.Updata();
            else if (Stute == Stute_tokensys.SetQiPaning) Game_Qizi5.i.SetQiPan();
            else if (Stute == Stute_tokensys.Scenseing) Sc_MoonAndBox.i.updata(MsgName, Message);
            else if (Stute == Stute_tokensys.emo) EmotionTool.i.updata(MsgName, Message);
            A_maintool.updata();
            Tip_actions?.Invoke(MsgName, Message);
        }

        public void command(string name, string msg,UI_Chat_Define.eMessageType type)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (name == mainplayername && msg[0] == '.')
            {
                Test.test(msg);
            }
            if(name == mainplayername)
            {
                A_maintool.updata(msg);
            }
            if (msg[0] == '~' && Stute == Stute_tokensys.None)
            {
                if (usebot)////////////////////
                {

                    if (msg[0] == '%' && type == UI_Chat_Define.eMessageType.Text)
                    {
                        var newmsg = msg.TrimStart('%');
                        MelonCoroutines.Start(AskAI(name ,newmsg));
                    }
                    if (msg == "~help")
                        Send("\n~help\n~五子棋\n~boom");

                    if (msg == "~五子棋")
                    {
                        Stute = Stute_tokensys.Qizi5sta;
                        Send("发送111参加游戏");

                    }

                    if (msg == "~boom") Stute = Stute_tokensys.Boomsta;
                    if (msg == "~this") mainplayer.SetPosition(players[name].position);
                }
                if (name == mainplayername)////////////////////////
                {
                    if (msg == "~setQiPan") Stute = Stute_tokensys.SetQiPansta;
                    if (msg == "~sc") 
                    { 
                        Stute = Stute_tokensys.Scenseing; 
                    }
                    if (msg == "~emo")
                    {
                        Stute = Stute_tokensys.emo;
                    }
                    if (msg == "~bot") usebot = true;
                    if (msg == "~nobot") usebot = false;
                    //if (msg == "~run") Stute = Stute_tokensys.AutoSetHomeitem;
                }
            }
            switch (Stute)
            {
                case Stute_tokensys.None: break;
                case Stute_tokensys.Qizi5sta:
                    if (msg == "111")
                    {
                        Game_Qizi5.i.game.SetPlayer(name);
                        Send(name + "加入游戏");
                        if (Game_Qizi5.i.getnum() >= 2)
                        {
                            Stute = Stute_tokensys.Qizi5ing;
                            Game_Qizi5.i.game.Start();
                        }
                    }
                    break;

                case Stute_tokensys.Qizi5ing:
                    Game_Qizi5.i.game.Setqizi(name, msg);
                    break;

                case Stute_tokensys.SetQiPansta:
                    Game_Qizi5.i.Start();

                    Stute = Stute_tokensys.SetQiPaning;
                    break;
                case Stute_tokensys.Boomsta:
                    Game_Boom.i.Start();
                    Send("发送114创建队伍，发送0721开始游戏");
                    Stute = Stute_tokensys.Boomsta0;
                    break;
                case Stute_tokensys.Boomsta0:
                    Game_Boom.i.Start0(name, msg);
                    // Send("游戏开始猜数字0~1000");
                    break;
                case Stute_tokensys.Booming:
                    Game_Boom.i.Updatamsg(name, msg);
                    break;

            }
            if (msg == "~stop")
            {
                if (name == mainplayername || usebot)
                {
                    Stute = Stute_tokensys.None;
                }
            }
        }

        bool ailocker = false;
        
        IEnumerator AskAI(string name, string msg)
        {
            if (ailocker) yield break;
            float aitimer = 30;
            ailocker = true;
            bool done = false;
            string result = null;
            Match match = Regex.Match(msg, @"\[q:([^\]]+)\](.*)");
            if (match.Success) {
                Task.Run(async () =>
                {
                    result = await DeepSeekClient.Instance.ChatAsync(match.Groups[1].Value + ':' + match.Groups[2].Value);
                    done = true;
                });
            }
            else
            {
                Task.Run(async () =>
                {
                    result = await DeepSeekClient.Instance.ChatAsync(name + ':' + msg);
                    done = true;
                });
            }
                while (!done)
                {
                    aitimer -= Time.deltaTime;
                    if (aitimer <= 0)
                    {
                        ailocker = false;
                        Send("ai响应超时或未设置配置文件");
                        yield break;
                    }
                    yield return null;
                }
            if (result != null)
            {
                MelonLogger.Msg($"AI: {result}");
                UIChatSystem.Instance.SendMessageData(result, 0, eChatChannelType.Current);
            }
            else
                MelonLogger.Error($"错误: {DeepSeekClient.Instance.LastError}");
            DeepSeekClient.Instance.SaveHistory();
            yield return new WaitForSeconds(0.5f);
            ailocker = false;
        }
        public void Send(string msg)
        {
            UIChatSystem.Instance.SendMessageData("msg:"+msg, 0, eChatChannelType.Current);
        }
        public void OK()
        {
            Send("OK");
        }
        public void ERR()
        {
            Send("Error");
        }
        public void sendComd(string inputStr)
        {
            command(mainplayername, inputStr, Il2Cpp.UI_Chat_Define.eMessageType.Text);
            updatecommand(mainplayername, inputStr);
        }
        public void sendchat(string context,int id,eChatChannelType c)
        {
            UIChatSystem._Instance.SendMessageData(context, id, c);
        }
        // ================== 事件回调（已在主线程） ==================
        private void OnChatMessage(Chat.Client.ChatMessage msg)
        {
            MelonLogger.Msg($"[收到] <{msg.From}> {msg.Text}");

            // 可以直接操作 Unity API（因为已经在主线程）
            // CenterTMPManager.SetText($"{msg.From}: {msg.Text}");
        }

        private void OnChatEvent(Chat.Client.ChannelEventInfo evt)
        {
            MelonLogger.Msg($"[频道事件] {evt.Nick} {evt.Type} #{evt.Channel}");
        }

        private void OnChatConnected(bool connected, string reason)
        {
            MelonLogger.Msg($"[连接状态] {(connected ? "已连接" : "已断开")} - {reason}");
        }
    }
    
}
