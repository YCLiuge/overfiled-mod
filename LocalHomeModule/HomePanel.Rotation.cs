using UnityEngine;
using UnityEngine.UI;

namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? rotationWindow,rotationTitle;
    private bool rotationOpen,rotationPreviewActive,rotationPlaying,rotationSync;
    private string rotationSpace="world",rotationStyle="group",rotationInput="speed",rotationError="";
    private MechanismPoint? rotationReference;
    private int rotationAxis=1;
    private float rotationProgress,rotationLastTime;
    private Slider rotationSlider=null!;
    private Text mechanismRotationCaption=null!,rotationBasisText=null!,rotationCenterText=null!,rotationValueText=null!,rotationStatus=null!;
    private InputField[] rotationAngles=Array.Empty<InputField>();
    private InputField rotationDuration=null!;
    private readonly Dictionary<string,Button> rotationOptions=new();
    private Button rotationPlay=null!;
    private MechanismPoint? RotationMarkerReference => rotationSpace=="local"?rotationReference:null;

    private void OpenRotation()
    {
        if(mechanismKind!="rotate" || mechanismId.Length==0) throw new InvalidOperationException("请先新增或选择旋转机关");
        if(rotationWindow==null) BuildRotation();
        rotationOpen=true; rotationWindow!.gameObject.SetActive(true); rotationWindow.SetAsLastSibling();
        RefreshRotationControls(); ClearEditorFocus(); Clamp();
    }
    private void CloseRotation()
    {
        CancelRotationPreview(); rotationOpen=false; rotationWindow?.gameObject.SetActive(false); ClearEditorFocus();
    }
    private void CancelRotationPreview()
    {
        rotationPreviewActive=false; rotationPlaying=false; rotationProgress=0; rotationError="";
        runtime.ClearRotationPreview();
        if(rotationSlider!=null) { rotationSync=true; rotationSlider.value=0; rotationSync=false; }
    }
    private void RotationAction(Action action,string message) => MechanismAction(()=> { rotationError=""; action(); RefreshRotationControls(); },message);
    private void RotationOption(Transform p,string key,string text,float x,float y,float w,Action action)
        => rotationOptions[key]=Button(p,text,x,y,w,34,()=>RotationAction(action,"旋转草稿已更新；保存后生效"));
    private void BuildRotation()
    {
        rotationWindow=Box(root!.transform,"RotationWindow",0,0,800,740,new Color(.055f,.075f,.10f,.98f)).GetComponent<RectTransform>();
        rotationWindow.anchoredPosition=mechanismWindow!.anchoredPosition+new Vector2(44,-28);
        rotationTitle=Box(rotationWindow,"RotationDrag",0,0,650,42,new Color(.10f,.19f,.24f)).GetComponent<RectTransform>();
        Label(rotationTitle,"旋转高级编辑 · 拖动标题栏",12,0,626,42,19);
        Button(rotationWindow,"关闭",666,5,122,32,CloseRotation);
        var p=rotationWindow.transform;
        Label(p,"旋转中心（直接更新机关坐标草稿）",12,48,776,28,16);
        Button(p,"活动家具中心",12,82,246,34,()=>RotationAction(()=>SetRotationCenter(false),"已取活动家具中心"));
        Button(p,"多选家具中心",276,82,246,34,()=>RotationAction(()=>SetRotationCenter(true),"已取多选家具中心"));
        Button(p,"取角色坐标",540,82,248,34,()=>RotationAction(UsePlayerMechanismPosition,"已取角色坐标"));
        rotationCenterText=Label(p,"",12,122,776,28,15);
        RotationOption(p,"world","世界轴",12,156,170,()=>rotationSpace="world");
        RotationOption(p,"local","参考家具本地轴",194,156,220,()=>
        {
            if(rotationReference==null) throw new InvalidOperationException("请先点击从活动家具取轴向");
            rotationSpace="local";
        });
        Button(p,"从活动家具取轴向",426,156,362,34,()=>RotationAction(()=>
        {
            PruneSelection(); var item=runtime.Items.FirstOrDefault(f=>f.Id==selected)??throw new InvalidOperationException("请在主面板选择活动家具");
            rotationReference=new(item.Pitch,item.Yaw,item.Roll); rotationSpace="local";
        },"已保存参考姿态快照；之后家具角度变化不会改变此轴向"));
        rotationBasisText=Label(p,"",12,196,776,28,14);
        RotationOption(p,"group","整体旋转",12,230,246,()=>rotationStyle="group");
        RotationOption(p,"orbit","绕中心 / 保持朝向",276,230,246,()=>rotationStyle="orbit");
        RotationOption(p,"spin","各家具原地自转",540,230,248,()=>rotationStyle="spin");
        RotationOption(p,"angle","总角度 + 用时",12,274,378,()=>SetRotationInput("angle"));
        RotationOption(p,"speed","角速度 °/秒",408,274,380,()=>SetRotationInput("speed"));
        for(int i=0;i<3;i++)
        {
            int axis=i; RotationOption(p,"axis"+i,new[]{"X轴（红）","Y轴（绿）","Z轴（蓝）"}[i],12+i*264,316,246,()=>rotationAxis=axis);
        }
        float[] amounts={-90,-15,15,90};
        for(int i=0;i<amounts.Length;i++)
        {
            float amount=amounts[i];
            Button(p,(amount>0?"+":"")+F(amount),12+i*198,358,182,32,()=>RotationAction(()=>
            {
                float result=EditorNumber(mechanismRotation[rotationAxis])+amount;
                if(!float.IsFinite(result) || Math.Abs(result)>100000) throw new ArgumentException("旋转数值须在±100000范围内");
                mechanismRotation[rotationAxis].text=F(result); SyncRotationAngles();
            },"已调整选定轴旋转数值"));
        }
        rotationAngles=MechanismVector(p,400,"旋转 XYZ");
        for(int i=0;i<3;i++)
        {
            int axis=i;
            rotationAngles[i].onValueChanged.AddListener(new Action<string>(value=>
            {
                if(rotationSync) return; mechanismRotation[axis].text=value;
            }));
            mechanismRotation[i].onValueChanged.AddListener(new Action<string>(value=>
            {
                if(rotationSync) return; rotationSync=true; rotationAngles[axis].text=value; rotationSync=false;
            }));
        }
        rotationValueText=Label(p,"",12,442,556,34,15);
        Label(p,"用时 秒",580,442,86,34,15); rotationDuration=MechanismField(p,674,442,114,mechanismDuration.text,24);
        rotationDuration.onValueChanged.AddListener(new Action<string>(value=> { if(!rotationSync) mechanismDuration.text=value; }));
        mechanismDuration.onValueChanged.AddListener(new Action<string>(value=> { if(rotationSync) return; rotationSync=true; rotationDuration.text=value; rotationSync=false; }));
        Label(p,"进度预览：忽略延迟和循环，1.0 固定为终点；不发送网络",12,486,776,28,15);
        BuildRotationSlider(p);
        for(int i=0;i<5;i++)
        {
            float progress=i*.25f;
            Button(p,(i*25)+"%",12+i*158,556,144,32,()=>SetRotationProgress(progress));
        }
        rotationPlay=Button(p,"播放",12,600,246,34,()=>
        {
            if(rotationPlaying) rotationPlaying=false;
            else { if(rotationProgress>=1) SetRotationProgress(0); rotationPreviewActive=true; rotationPlaying=true; rotationLastTime=Time.unscaledTime; rotationError=""; }
            RefreshRotationControls();
        });
        Button(p,"恢复起点",276,600,246,34,()=>SetRotationProgress(0));
        Button(p,"退出预览",540,600,248,34,()=> { CancelRotationPreview(); RefreshRotationControls(); });
        Button(p,"保存机关草稿",12,644,378,34,()=>RotationAction(SaveMechanism,"旋转机关已保存，预览已退出"));
        Button(p,"关闭高级窗口",408,644,380,34,CloseRotation);
        rotationStatus=Label(p,"",12,686,776,44,14);
    }
    private void BuildRotationSlider(Transform p)
    {
        var go=Box(p,"RotationProgress",12,522,776,22,new Color(.09f,.13f,.18f));
        rotationSlider=go.AddComponent<Slider>(); rotationSlider.minValue=0; rotationSlider.maxValue=1; rotationSlider.wholeNumbers=false;
        var fill=Box(go.transform,"Fill",0,0,776,22,new Color(.1f,.6f,.6f));
        var fillRect=fill.GetComponent<RectTransform>(); fillRect.anchorMin=Vector2.zero; fillRect.anchorMax=Vector2.one; fillRect.offsetMin=fillRect.offsetMax=Vector2.zero;
        var handle=Box(go.transform,"Handle",0,0,18,28,new Color(.8f,1,1));
        var handleRect=handle.GetComponent<RectTransform>(); handleRect.anchorMin=handleRect.anchorMax=new Vector2(0,.5f); handleRect.pivot=new Vector2(.5f,.5f); handleRect.anchoredPosition=Vector2.zero;
        rotationSlider.fillRect=fillRect; rotationSlider.handleRect=handleRect; rotationSlider.targetGraphic=handle.GetComponent<Image>();
        rotationSlider.direction=Slider.Direction.LeftToRight;
        rotationSlider.onValueChanged.AddListener(new Action<float>(value=>
        {
            if(rotationSync) return; rotationProgress=value; rotationPreviewActive=true; rotationPlaying=false; rotationError="";
        }));
    }
    private void SetRotationInput(string mode)
    {
        if(rotationInput==mode) return;
        float duration=EditorNumber(mechanismDuration);
        if(!float.IsFinite(duration) || duration<=0) throw new ArgumentException("切换输入方式前请设置大于0的用时");
        var values=mechanismRotation.Select(EditorNumber).Select(v=>mode=="angle"?v*duration:v/duration).ToArray();
        if(values.Any(v=>!float.IsFinite(v) || Math.Abs(v)>100000)) throw new ArgumentException("换算后的旋转数值超出±100000范围");
        for(int i=0;i<3;i++) mechanismRotation[i].text=F(values[i]);
        rotationInput=mode;
    }
    private void SetRotationCenter(bool multiple)
    {
        PruneSelection(); var items=runtime.Items.Where(f=>multiple?selectedIds.Contains(f.Id):f.Id==selected).ToArray();
        if(items.Length==0) throw new InvalidOperationException(multiple?"请在主面板多选家具":"请在主面板选择活动家具");
        WriteMechanismPoint(mechanismPosition,new((float)items.Average(f=>(double)f.X),(float)items.Average(f=>(double)f.Y),(float)items.Average(f=>(double)f.Z)));
    }
    private void SyncRotationAngles()
    {
        if(rotationAngles.Length!=3) return;
        rotationSync=true; for(int i=0;i<3;i++) rotationAngles[i].text=mechanismRotation[i].text; rotationDuration.text=mechanismDuration.text; rotationSync=false;
    }
    private void RefreshRotationControls()
    {
        if(rotationWindow==null) return;
        SyncRotationAngles();
        foreach(var option in rotationOptions)
        {
            bool active=option.Key==rotationSpace || option.Key==rotationStyle || option.Key==rotationInput || option.Key=="axis"+rotationAxis;
            option.Value.GetComponent<Image>().color=active?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        }
        if(mechanismRotationCaption!=null) mechanismRotationCaption.text=rotationInput=="angle"?"总角度 °":"旋转速度 °/秒";
        rotationBasisText.text=rotationSpace=="world"?"轴向：世界 XYZ；本地轴需明确从活动家具获取姿态快照":"参考姿态快照（俯仰/偏航/翻滚）："+(rotationReference==null?"未设置":MechanismPointText(rotationReference));
        rotationCenterText.text="中心 XYZ："+string.Join(", ",mechanismPosition.Select(f=>f.text));
        rotationValueText.text=(rotationInput=="angle"?"XYZ为总角度（°），":"XYZ为角速度（°/秒），")+"当前步进轴 "+new[]{"X","Y","Z"}[rotationAxis];
        rotationPlay.GetComponentInChildren<Text>().text=rotationPlaying?"暂停":"播放";
        rotationStatus.text=rotationError.Length>0?rotationError:rotationPreviewActive?"本地草稿预览 "+Mathf.RoundToInt(rotationProgress*100)+"% · 输入变更实时反映，不改存档或联网时间":"预览未开启；点击进度或播放开始。所有设置为草稿，保存后生效。";
    }
    private void SetRotationProgress(float value)
    {
        rotationProgress=Mathf.Clamp01(value); rotationPreviewActive=true; rotationPlaying=false; rotationError="";
        rotationSync=true; rotationSlider.value=rotationProgress; rotationSync=false; RefreshRotationControls();
    }
    private void TickRotationPreview()
    {
        if(!rotationOpen || triggerTab || mechanismKind!="rotate") { if(rotationPreviewActive) CancelRotationPreview(); return; }
        if(!rotationPreviewActive) return;
        try
        {
            var node=ReadMechanismDraft();
            if(rotationPlaying)
            {
                if(node.Duration<=0 || !float.IsFinite(node.Duration)) throw new ArgumentException("用时须大于0");
                float now=Time.unscaledTime; rotationProgress=Mathf.Clamp01(rotationProgress+Mathf.Max(0,now-rotationLastTime)/node.Duration); rotationLastTime=now;
                if(rotationProgress>=1) rotationPlaying=false;
            }
            runtime.SetRotationPreview(node,rotationProgress);
            rotationSync=true; rotationSlider.value=rotationProgress; rotationSync=false; RefreshRotationControls();
        }
        catch(Exception error)
        {
            CancelRotationPreview(); rotationError="预览已停止："+error.Message;
            EditorAction(()=>throw new InvalidOperationException(rotationError),""); RefreshRotationControls();
        }
    }
}
