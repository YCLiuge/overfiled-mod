using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? mechanismWindow, mechanismTitle;
    private GameObject? mechanismBody, mechanismForm, triggerForm;
    private bool mechanismOpen, mechanismMinimized, triggerTab;
    private Button mainMechanisms=null!, mechanismMinimize=null!, mechanismDebug=null!;
    private readonly List<Button> mechanismRows=new(), kindButtons=new(), modeButtons=new();
    private readonly Dictionary<string,GameObject> kindPanels=new();
    private Text mechanismListPage=null!, mechanismLive=null!, mechanismNotice=null!, mechanismIdLabel=null!, mechanismParentHint=null!;
    private InputField mechanismName=null!, mechanismParent=null!, mechanismFurniture=null!, mechanismTrack=null!;
    private InputField mechanismSpeed=null!, mechanismDuration=null!, mechanismDelay=null!, mechanismScale=null!;
    private InputField[] mechanismPosition=Array.Empty<InputField>(), mechanismRotation=Array.Empty<InputField>(), mechanismGravity=Array.Empty<InputField>(), mechanismVelocity=Array.Empty<InputField>();
    private InputField triggerName=null!, triggerRadius=null!, triggerTargets=null!;
    private InputField[] triggerPosition=Array.Empty<InputField>();
    private Button triggerEnabledButton=null!;
    private bool triggerEnabled=true;
    private string mechanismId="", triggerId="", mechanismKind="track", mechanismMode="once";
    private int mechanismPage;
    private const int MechanismRows=7;
    private void MechanismAction(Action action,string message) => EditorAction(action,message);
    private void OpenMechanisms()
    {
        MechanismAction(()=>
        {
            if(mechanismWindow==null) BuildMechanisms();
            mechanismOpen=true; mechanismWindow!.gameObject.SetActive(true);
            mechanismWindow.SetAsLastSibling();
            ClearEditorFocus(); Clamp();
        },"机关调试仅本地；只有本地角色进入触发器才触发");
    }
    private void CloseMechanisms()
    {
        CloseMechanismEvents(); CloseTrack(); CloseRotation(); CancelMechanismTreeDrag(); mechanismOpen=false; mechanismWindow?.gameObject.SetActive(false);
        ClearEditorFocus();
    }
    private void MinimizeMechanisms()
    {
        CancelMechanismTreeDrag(); mechanismMinimized=!mechanismMinimized; dragging=false;
        mechanismBody!.SetActive(!mechanismMinimized);
        mechanismWindow!.sizeDelta=new Vector2(mechanismMinimized?420:1040,mechanismMinimized?44:884);
        mechanismMinimize.GetComponent<RectTransform>().anchoredPosition=new Vector2(mechanismMinimized?176:790,-5);
        mechanismMinimize.GetComponentInChildren<Text>().text=mechanismMinimized?"展开":"最小化";
        mechanismTitle!.sizeDelta=new Vector2(mechanismMinimized?170:570,42);
        // Return remains reachable in compact mode.
        mechanismClose.GetComponent<RectTransform>().anchoredPosition=new Vector2(mechanismMinimized?292:914,-5);
        ClearEditorFocus(); Clamp();
    }
    private Button mechanismClose=null!;
    private InputField MechanismField(Transform p,float x,float y,float w,string text,int max=128)
    { var field=Field(p,x,y,w,text); field.characterLimit=max; return field; }
    private InputField[] MechanismVector(Transform p,float top,string label,string initial="0")
    {
        Label(p,label,12,top,150,34,15); var values=new InputField[3];
        for(int i=0;i<3;i++) { Label(p,new[]{"X","Y","Z"}[i],164+i*134,top,20,34,15); values[i]=MechanismField(p,186+i*134,top,106,initial,24); }
        Button(p,"复制",572,top,60,34,()=>CopyVector(values));
        Button(p,"粘贴",638,top,66,34,()=>PasteVector(values));
        return values;
    }
    private static MechanismPoint ReadMechanismPoint(InputField[] f) => new(EditorNumber(f[0]),EditorNumber(f[1]),EditorNumber(f[2]));
    private static void WriteMechanismPoint(InputField[] f,MechanismPoint p)
    { f[0].text=F(p.X); f[1].text=F(p.Y); f[2].text=F(p.Z); }
    private static string MechanismPointText(MechanismPoint p) => F(p.X)+","+F(p.Y)+","+F(p.Z);
    private static string[] MechanismTokens(string value) => value.Split(new[]{',',';',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToArray();
    private static MechanismPoint[] ParseMechanismTrack(string text)
    {
        if(string.IsNullOrWhiteSpace(text)) return Array.Empty<MechanismPoint>();
        return text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(part=>
        {
            var xyz=part.Split(',',StringSplitOptions.TrimEntries);
            if(xyz.Length!=3) throw new ArgumentException("轨道格式为 x,y,z;x,y,z");
            var v=xyz.Select(s=>float.Parse(s,NumberStyles.Float,CultureInfo.InvariantCulture)).ToArray();
            if(v.Any(f=>!float.IsFinite(f))) throw new ArgumentException("轨道必须是有限数字");
            return new MechanismPoint(v[0],v[1],v[2]);
        }).ToArray();
    }
    private void BuildMechanisms()
    {
        mechanismWindow=Box(root!.transform,"MechanismWindow",0,0,1040,884,new Color(.055f,.075f,.10f,.98f)).GetComponent<RectTransform>();
        mechanismWindow.anchoredPosition=window!.anchoredPosition+new Vector2(28,-28);
        mechanismTitle=Box(mechanismWindow,"MechanismDrag",0,0,570,42,new Color(.10f,.19f,.24f)).GetComponent<RectTransform>();
        Label(mechanismTitle,"机关编辑 · 拖动标题栏",12,0,550,42,19);
        mechanismMinimize=Button(mechanismWindow,"最小化",790,5,112,32,MinimizeMechanisms);
        mechanismClose=Button(mechanismWindow,"关闭 / 返回",914,5,116,32,CloseMechanisms);
        mechanismBody=Box(mechanismWindow,"MechanismBody",0,44,1040,840,new Color(0,0,0,0)); var p=mechanismBody.transform;
        Button(p,"机关",12,8,142,34,()=>SwitchMechanismTab(false));
        Button(p,"触发器",166,8,142,34,()=>SwitchMechanismTab(true));
        Button(p,"全局启动",324,8,132,34,()=>MechanismAction(()=>runtime.StartMechanisms(),"已全局启动；触发器绑定机关及其后代保持原状态"));
        Button(p,"结束（保持）",468,8,148,34,()=>MechanismAction(()=>runtime.EndMechanisms(),"已结束，保持当前变换"));
        Button(p,"重置（还原）",628,8,148,34,()=>MechanismAction(()=>runtime.ResetMechanisms(),"已重置，还原初始变换"));
        mechanismDebug=Button(p,"",788,8,240,34,()=>MechanismAction(()=>runtime.SetMechanismDebug(!runtime.MechanismDebug),"已切换本地调试显示"));
        for(int i=0;i<MechanismRows;i++)
        {
            int row=i;
            mechanismRows.Add(Button(p,"",44,56+i*54,254,48,()=> { if(MechanismTreeClickBlocked) return; MechanismAction(()=>SelectMechanismRow(row),"已读取选中项"); }));
            mechanismTreeExpand.Add(Button(p,"",12,56+i*54,28,48,()=>ToggleMechanismTreeRow(row)));
        }
        Button(p,"上一页",12,444,82,32,()=> { if(mechanismTreeSource.Length>0 || MechanismTreeClickBlocked) return; mechanismPage=Math.Max(0,mechanismPage-1); RefreshMechanisms(); });
        mechanismListPage=Label(p,"",100,444,106,32,15);
        Button(p,"下一页",212,444,86,32,()=> { if(mechanismTreeSource.Length>0 || MechanismTreeClickBlocked) return; mechanismPage++; RefreshMechanisms(); });
        Button(p,"新增",12,488,138,34,()=>MechanismAction(()=> { if(triggerTab) ReadTrigger(new(Guid.NewGuid().ToString("N"),"新触发器",new(runtime.Origin.X,runtime.Origin.Y,runtime.Origin.Z),2,Array.Empty<string>())); else ReadMechanism(new MechanismDefinition(Guid.NewGuid().ToString("N"),"新机关","track",new(),null,Array.Empty<int>(),Array.Empty<MechanismPoint>()) with {RotationInput="angle"}); },"新建草稿，请保存"));
        Button(p,"删除",160,488,138,34,()=>Confirm("mechanism-delete:"+(triggerTab?triggerId:mechanismId),()=>MechanismAction(()=>
        {
            if(triggerTab) { if(triggerId.Length==0) throw new InvalidOperationException("请选择触发器"); runtime.DeleteTrigger(triggerId); triggerId=""; }
            else { if(mechanismId.Length==0) throw new InvalidOperationException("请选择机关"); CloseMechanismEvents(); CloseTrack(); CloseRotation(); runtime.DeleteMechanism(mechanismId); mechanismId=""; LoadTrackDraft(Array.Empty<MechanismPoint>()); }
        },"已删除选中项")));
        mechanismRootDrop=Box(p,"MechanismRootDrop",12,536,286,42,new Color(.10f,.24f,.28f)).GetComponent<RectTransform>();
        mechanismRootDropText=Label(mechanismRootDrop,"拖到这里 → 取消父机关",6,0,274,42,14);
        mechanismTreeHint=Label(p,"",12,586,286,226,14);
        mechanismForm=Box(p,"MechanismForm",312,52,716,666,new Color(0,0,0,0)); BuildMechanismForm(mechanismForm.transform);
        triggerForm=Box(p,"TriggerForm",312,52,716,666,new Color(0,0,0,0)); BuildTriggerForm(triggerForm.transform);
        Label(p,"坐标步长",324,714,100,28,14);
        mechanismPositionStep=MechanismField(p,426,710,114,"0.5",24);
        Label(p,"XYZ ± 调整当前草稿；保存后生效",550,714,478,28,14);
        mechanismLive=Label(p,"",324,750,704,34,14);
        mechanismNotice=Label(p,"",324,790,704,40,14);
        RefreshMechanismKind();
        SwitchMechanismTab(false);
    }
    private void BuildMechanismForm(Transform p)
    {
        mechanismIdLabel=Label(p,"先新增或选择机关",12,0,692,30,15);
        Label(p,"名称",12,36,90,34,16); mechanismName=MechanismField(p,106,36,598,"");
        mechanismPosition=MechanismVector(p,80,"机关坐标");
        string[] kinds={"track","rotate","scale","gravity"}, names={"轨道","旋转","缩放","重力"};
        BuildMechanismPositionNudges(p,120);
        for(int i=0;i<kinds.Length;i++) { string kind=kinds[i]; kindButtons.Add(Button(p,names[i],12+i*176,160,164,32,()=> { if(mechanismKind!=kind) { CancelRotationPreview(); if(kind!="rotate") CloseRotation(); } mechanismKind=kind; RefreshMechanismKind(); })); }
        Label(p,"父 ID / 高级",12,200,104,34,14); mechanismParent=MechanismField(p,120,200,466,"");
        Button(p,"清除父",594,200,110,34,()=> { mechanismParent.text=""; });
        mechanismParentHint=Label(p,"",12,240,692,28,14);
        Label(p,"挂载家具 IDs",12,276,110,34,15); mechanismFurniture=MechanismField(p,130,276,574,"",8192);
        Button(p,"挂载主面板选中",12,316,216,30,()=>MechanismAction(()=> { PruneSelection(); mechanismFurniture.text=string.Join(",",selectedIds.OrderBy(id=>id)); },"已读取多选家具，保存后挂载"));
        Label(p,"逗号分隔编号，可手动编辑",240,316,464,30,15);
        Label(p,"延迟 / 秒",12,352,100,34,15); mechanismDelay=MechanismField(p,116,352,130,"0",24);
        Label(p,"时长 / 秒",272,352,110,34,15); mechanismDuration=MechanismField(p,390,352,130,"5",24);
        string[] modes={"once","loop","pingpong"}, modeNames={"单次","循环","往返"};
        for(int i=0;i<modes.Length;i++) { string mode=modes[i]; modeButtons.Add(Button(p,modeNames[i],12+i*234,390,224,32,()=> { mechanismMode=mode; RefreshMechanismKind(); })); }
        foreach(string kind in kinds) kindPanels[kind]=Box(p,kind+"Parameters",0,430,716,166,new Color(0,0,0,0));
        var track=kindPanels["track"].transform;
        trackSummary=Label(track,"世界坐标路径 · 0 / 256 个点（保存需 2～256 点）",12,0,692,28,15);
        mechanismTrack=MechanismField(track,12,32,692,"",16384);
        mechanismTrack.gameObject.SetActive(false); // Retain the established parser/persistence source.
        Label(track,"速度",12,42,100,34,15); mechanismSpeed=MechanismField(track,116,42,220,"1",24);
        Button(track,"编辑轨道点列表",360,42,344,34,()=>MechanismAction(OpenTrack,"已打开世界坐标轨道列表"));
        Label(track,"路径点在世界中显示；先保存机关再启动",12,94,692,44,15);
        mechanismRotation=MechanismVector(kindPanels["rotate"].transform,8,"旋转速度 °/秒");
        mechanismRotationCaption=kindPanels["rotate"].transform.GetChild(0).GetComponent<Text>();
        Button(kindPanels["rotate"].transform,"旋转高级编辑 / 预览",12,56,336,34,()=>MechanismAction(OpenRotation,"已打开旋转高级编辑"));
        Label(kindPanels["rotate"].transform,"中心、轴向、方式、角度与独立预览",12,100,692,44,15);
        var scaling=kindPanels["scale"].transform; Label(scaling,"目标缩放倍率",12,8,164,34,15); mechanismScale=MechanismField(scaling,184,8,220,"2",24);
        Label(scaling,"时长使用上方通用参数",12,56,692,32,15);
        mechanismGravity=MechanismVector(kindPanels["gravity"].transform,0,"重力加速度");
        mechanismVelocity=MechanismVector(kindPanels["gravity"].transform,48,"初始速度");
        Button(p,"保存机关",12,612,220,38,()=>MechanismAction(SaveMechanism,"机关已保存"));
        Button(p,"启动此机关",244,612,220,38,()=>MechanismAction(()=> { if(mechanismId.Length==0) throw new InvalidOperationException("请选择并保存机关"); runtime.StartMechanisms(mechanismId); },"已启动选中机关（已保存配置）"));
        Button(p,"结束/循环联动",476,612,228,38,()=>OpenMechanismEvents());
    }
    private void BuildTriggerForm(Transform p)
    {
        Label(p,"触发器：本地角色进入范围后启动目标机关",12,0,692,30,17);
        triggerIdLabel=Label(p,"先新增或选择触发器",12,38,692,30,14);
        Label(p,"名称",12,80,90,34,16); triggerName=MechanismField(p,106,80,598,"");
        triggerPosition=MechanismVector(p,128,"触发器坐标");
        BuildMechanismPositionNudges(p,168);
        Label(p,"触发半径",12,212,140,34,15); triggerRadius=MechanismField(p,164,212,220,"2",24);
        triggerEnabledButton=Button(p,"启用",408,212,296,34,()=> { triggerEnabled=!triggerEnabled; RefreshTriggerEnabled(); });
        Label(p,"目标机关 IDs（逗号或分号分隔）",12,260,692,28,16);
        triggerTargets=MechanismField(p,12,294,692,"",16384);
        Button(p,"添加当前机关",12,344,336,34,()=>MechanismAction(()=>
        { if(mechanismId.Length==0) throw new InvalidOperationException("先在机关页选择机关"); triggerTargets.text=string.Join(",",MechanismTokens(triggerTargets.text).Append(mechanismId).Distinct()); },"已添加目标，保存后生效"));
        Button(p,"取原点坐标",360,344,344,34,()=> { var o=runtime.Origin; WriteMechanismPoint(triggerPosition,new(o.X,o.Y,o.Z)); });
        Label(p,"机关页选择的活动机关在切页后保留。\n目标填写完整机关 ID，可添加多个。\n手动触发使用已保存的触发器配置。\n修改任意输入后，先保存再运行。",12,400,692,120,16);
        Button(p,"保存触发器",12,612,336,38,()=>MechanismAction(()=>
        { if(triggerId.Length==0) throw new InvalidOperationException("请新增或选择触发器"); runtime.UpsertTrigger(new(triggerId,triggerName.text,ReadMechanismPoint(triggerPosition),EditorNumber(triggerRadius),MechanismTokens(triggerTargets.text),triggerEnabled)); },"触发器已保存"));
        Button(p,"手动触发",360,612,344,38,()=>MechanismAction(()=> { if(triggerId.Length==0) throw new InvalidOperationException("请选择并保存触发器"); runtime.FireTrigger(triggerId); },"已手动触发"));
    }
    private Text triggerIdLabel=null!;
    private void SwitchMechanismTab(bool triggers)
    {
        CloseMechanismEvents(); CloseTrack();
        if(triggers) CloseRotation(); else CancelRotationPreview();
        CancelMechanismTreeDrag(); triggerTab=triggers; mechanismPage=0;
        mechanismForm!.SetActive(!triggers); triggerForm!.SetActive(triggers); RefreshMechanisms(); ClearEditorFocus();
    }
    private void SelectMechanismRow(int row)
    {
        int index=mechanismPage*MechanismRows+row;
        if(triggerTab) { var list=runtime.Triggers; if(index<list.Length) ReadTrigger(list[index]); }
        else if(row>=0 && row<mechanismTreeVisible.Count) { var id=mechanismTreeVisible[row].Id; var d=runtime.Mechanisms.FirstOrDefault(n=>n.Id==id); if(d!=null) ReadMechanism(d); }
    }
    private void ReadMechanism(MechanismDefinition d)
    {
        CancelRotationPreview();
        rotationSpace=d.RotationSpace; rotationReference=d.RotationReference; rotationStyle=d.RotationStyle; rotationInput=d.RotationInput;
        mechanismId=d.Id; mechanismIdLabel.text="ID: "+d.Id; mechanismName.text=d.Name;
        WriteMechanismPoint(mechanismPosition,d.Position); mechanismKind=d.Kind; mechanismParent.text=d.ParentId??"";
        mechanismFurniture.text=string.Join(",",d.FurnitureIds); LoadTrackDraft(d.Track);
        ReadMechanismEvents(d);
        mechanismSpeed.text=F(d.Speed); mechanismDuration.text=F(d.Duration); mechanismDelay.text=F(d.Delay);
        mechanismMode=d.Mode; mechanismScale.text=F(d.Scale); WriteMechanismPoint(mechanismRotation,new(d.RotationX,d.RotationY,d.RotationZ));
        WriteMechanismPoint(mechanismGravity,d.Gravity??new(0,-9.81f,0)); WriteMechanismPoint(mechanismVelocity,d.Velocity??new());
        RefreshMechanismKind();
    }
    private void ReadTrigger(MechanismTrigger t)
    {
        CloseMechanismEvents(); CloseTrack();
        triggerId=t.Id; triggerIdLabel.text="ID: "+t.Id; triggerName.text=t.Name; WriteMechanismPoint(triggerPosition,t.Position);
        triggerRadius.text=F(t.Radius); triggerTargets.text=string.Join(",",t.Targets); triggerEnabled=t.Enabled; RefreshTriggerEnabled();
    }
    private void SaveMechanism()
    {
        if(mechanismKind=="track") EnsureTrackInput();
        var draft=ReadMechanismDraft(); CancelRotationPreview(); runtime.UpsertMechanism(draft);
    }
    private MechanismDefinition ReadMechanismDraft()
    {
        if(mechanismId.Length==0) throw new InvalidOperationException("请新增或选择机关");
        if(mechanismKind=="rotate" && rotationSpace=="local" && rotationReference==null) throw new InvalidOperationException("请先从活动家具获取本地轴参考");
        var rotation=ReadMechanismPoint(mechanismRotation);
        return new MechanismDefinition(mechanismId,mechanismName.text,mechanismKind,ReadMechanismPoint(mechanismPosition),
            string.IsNullOrWhiteSpace(mechanismParent.text)?null:mechanismParent.text.Trim(),MechanismTokens(mechanismFurniture.text).Select(int.Parse).ToArray(),
            mechanismKind=="track"?ParseMechanismTrack(mechanismTrack.text):Array.Empty<MechanismPoint>(),EditorNumber(mechanismSpeed),EditorNumber(mechanismDuration),EditorNumber(mechanismDelay),mechanismMode,
            rotation.X,rotation.Y,rotation.Z,EditorNumber(mechanismScale),ReadMechanismPoint(mechanismGravity),ReadMechanismPoint(mechanismVelocity))
            with {RotationSpace=rotationSpace,RotationReference=rotationReference,RotationStyle=rotationStyle,RotationInput=rotationInput,
                OnCompleteTargets=ReadMechanismEventTargets(false),OnCycleTargets=ReadMechanismEventTargets(true)};
    }
    private void RefreshMechanismKind()
    {
        string[] kinds={"track","rotate","scale","gravity"}, modes={"once","loop","pingpong"};
        for(int i=0;i<kindButtons.Count;i++) kindButtons[i].GetComponent<Image>().color=mechanismKind==kinds[i]?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        for(int i=0;i<modeButtons.Count;i++) modeButtons[i].GetComponent<Image>().color=mechanismMode==modes[i]?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        foreach(var panel in kindPanels) panel.Value.SetActive(panel.Key==mechanismKind);
        if(mechanismRotationCaption!=null) mechanismRotationCaption.text=rotationInput=="angle"?"总角度 °":"旋转速度 °/秒";
        if(trackOpen && mechanismKind!="track") CloseTrack();
        if(rotationOpen && mechanismKind!="rotate") CloseRotation();
        RefreshRotationControls();
    }
    private void RefreshTriggerEnabled() => triggerEnabledButton.GetComponentInChildren<Text>().text=triggerEnabled?"已启用（点击停用）":"已停用（点击启用）";
    private void RefreshMechanisms()
    {
        if(mechanismWindow==null) return;
        var defs=runtime.Mechanisms; var triggers=runtime.Triggers;
        if(!triggerTab) RefreshMechanismTreeRows(defs);
        else
        {
            int count=triggers.Length;
            mechanismPage=Math.Clamp(mechanismPage,0,Math.Max(0,(count-1)/MechanismRows));
            mechanismListPage.text=(mechanismPage+1)+" / "+Math.Max(1,(count+MechanismRows-1)/MechanismRows);
            for(int i=0;i<mechanismRows.Count;i++)
            {
                int n=mechanismPage*MechanismRows+i; var row=mechanismRows[i]; row.gameObject.SetActive(n<count); row.interactable=true;
                mechanismTreeExpand[i].gameObject.SetActive(false);
                if(n>=count) continue;
                var t=triggers[n]; var text=row.GetComponentInChildren<Text>(); text.fontSize=13; text.text=t.Name+"\n"+(t.Enabled?"启用":"停用")+" · "+t.Id;
                row.GetComponent<Image>().color=t.Id==triggerId?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
            }
            mechanismRootDrop!.gameObject.SetActive(false);
            mechanismTreeHint.text="触发器从范围外进入时触发。\n可设置多个目标机关。\n\n坐标 XYZ ± 修改当前草稿，\n先保存再使用手动触发。";
        }
        mechanismDebug.GetComponentInChildren<Text>().text="本地调试："+(runtime.MechanismDebug?"开":"关");
        var parent=defs.FirstOrDefault(n=>n.Id==mechanismParent.text.Trim());
        mechanismParentHint.text=string.IsNullOrWhiteSpace(mechanismParent.text)?"父机关：根（可拖动左侧行建立父子关系）":"父机关："+(parent?.Name??"ID不存在，请检查")+" · 草稿保存后生效";
        mechanismLive.text=runtime.MechanismStatus+" · "+(!triggerTab && mechanismKind=="track"?trackMarker.Status:mechanismMarker.Status);
        mechanismNotice.text=confirm.StartsWith("mechanism-delete:",StringComparison.Ordinal) && Time.unscaledTime<confirmUntil?"再次点击删除确认（4 秒内）":Time.unscaledTime<editorMessageUntil?editorMessage:"调试不联网 · 结束保持 / 重置还原 · 先保存再启动";
        // Only list and status text refresh here. Draft inputs change on explicit selection/new actions.
    }
}
