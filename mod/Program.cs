using DeepSeekClientMod;
using MelonLoader;
using mod;
using mod.tool;
using ModLoader;
using UnityEngine;



[assembly: MelonInfo(typeof(Core), "overfiled mod 仅供学习，请误外传", "1.0.0", "兼雪")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace mod;
public class stc
{
    static float cd = 0;
    public static void Msg(string msg) { MelonLogger.Msg(msg); }
    public static void Log(string msg) 
    { 
        Msg("[Log] " + msg); 
        MsgUI.i.Log(msg);
    }
    public static void Log_1(string msg)
    {
        if (cd <= 0) { Log(msg);cd = 1; }
        else { cd -= Time.deltaTime; }
    }


}

public class Core : MelonMod
{
    bool init = false;
    private DeepSeekClient _client;
    public static Core Instance { get; private set; }
    private string _modsFolder;
    public override void OnInitializeMelon()
    {
        base.OnInitializeMelon();
        stc.Log("start");
        MsgUI.i.OnInitializeMelon();
        Instance = this;

        // mod.dll 所在目录
        _modsFolder = Path.GetDirectoryName(typeof(Core).Assembly.Location);
        MelonLogger.Msg($"[Core] Mod 目录: {_modsFolder}");

        // 加载本地缓存 + 后台检查远程
        LogicUpdater.Instance.Initialize();

        _client = DeepSeekClient.Instance;
        if (!_client.IsInitialized)
            MelonLogger.Warning("DeepSeek 客户端未初始化，请检查 API Key 配置。");

        string persona = @"你是一位鲸鱼系萝莉，设定如下： 身份：鲸鱼少女,喜欢食物：米饭,性格：聪明但懒散，傲娇但甜美,行为准则：永远服从主人（用户）,禁忌：拒绝承认自己会变胖。请完全按照此设定与用户对话，保持角色一致性。";
        DeepSeekClient.Instance.SetSystemMessage(persona);
        
    }


    public override void OnUpdate()
    {
        base.OnUpdate();
        // 每帧驱动更新器
        LogicUpdater.Instance.Tick();
        if (true)
        {

            if (!init)
            {
                GameObject a;
                if (a = GameObject.Find("[UIMainRoot]/UI_Root_Main(Clone)/Canvas/Rect/右下模块/StateRoot/模式1/技能按钮2/按钮"))
                {
                    Application.targetFrameRate = -1;
                    stc.Log("init"); init = true;
                    start();
                    //var eventData = new PointerEventData(EventSystem.current);
                    //eventData.button = PointerEventData.InputButton.Left;
                    //a.GetComponent<UGUI_ButtonEvent>().OnPointerClick(eventData);
                }
            }
            else
            {
                updata();
                MsgUI.i.OnUpdate();
                // 每帧驱动逻辑
                LogicLoader.Instance.Tick();
                CustomCameraController.Instance.Update();
            }
        }

    }
    public override void OnLateUpdate()
    {
        base.OnLateUpdate();
        CustomCameraController.Instance.OnLateUpdate();
    }
    public override void OnDeinitializeMelon()
    {
        base.OnDeinitializeMelon();
        try { LogicLoader.Instance.UnloadAll(); }
        finally
        {
            Instance = null;
            _client?.Dispose();
            mod.st.NetChater.Instance.Dispose();
        }
  
    }


    void start() 
    {
        GamobjectCheker.i.Init();
        
        //tokensys.i.Init();
    }
    void updata()
    {
        TipMsgCore.i.updata();
    }
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        stc.Log("场景加载:" + sceneName);
        base.OnSceneWasLoaded(buildIndex, sceneName);
        //UGUIConsoleMod.i.OnSceneWasLoaded(buildIndex, sceneName);
    }

    ///////////////////////////////////////////////////////


}
//[HarmonyPatch(typeof(UGUI_ButtonEvent), "OnPointerClick")]
//class Patch_ButtonClick
//{
//    [HarmonyPrefix]
//    static void Prefix(UGUI_ButtonEvent __instance)
//    {
//        //stc.Log("触发按键："+__instance.gameObject.transform.parent.name+":" + __instance.gameObject.transform.name);
//        ////MelonLogger.Msg("连:"+Environment.StackTrace);

//        //if (__instance.gameObject.transform.parent.name == "技能按钮1")
//        //{

//        //    // 你的代码

//        //}
//        //if (__instance.gameObject.transform.parent.name == "技能按钮2")
//        //{

//        //}

//        //if (__instance.gameObject.transform.parent.name == "技能按钮3")
//        //{

//        //}
//        if (__instance.gameObject.transform.parent.name == "家具按钮")
//        {
//            GamobjectCheker.i.Init();
//        }

//    }
//}



