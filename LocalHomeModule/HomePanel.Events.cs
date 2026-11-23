using UnityEngine;
using UnityEngine.UI;
namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? eventWindow,eventTitle;
    private InputField? eventComplete,eventCycle;
    private string eventCompleteDraft="",eventCycleDraft="";
    private bool eventOpen,eventCycleTab;
    private int eventPage;
    private Text eventSummary=null!,eventHint=null!;
    private readonly List<Button> eventTargets=new();
    private void ReadMechanismEvents(MechanismDefinition d)
    {
        CloseMechanismEvents();eventCompleteDraft=string.Join(",",d.OnCompleteTargets??Array.Empty<string>());eventCycleDraft=string.Join(",",d.OnCycleTargets??Array.Empty<string>());
        if(eventComplete!=null)eventComplete.text=eventCompleteDraft;if(eventCycle!=null)eventCycle.text=eventCycleDraft;
    }
    private string[] ReadMechanismEventTargets(bool cycle)=>MechanismTokens(cycle?(eventCycle?.text??eventCycleDraft):(eventComplete?.text??eventCompleteDraft)).Distinct(StringComparer.Ordinal).ToArray();
    private void CloseMechanismEvents() { eventOpen=false;eventWindow?.gameObject.SetActive(false); }
    private void OpenMechanismEvents()=>MechanismAction(()=>
    {
        if(mechanismId.Length==0)throw new InvalidOperationException("请先新增或选择机关");
        if(eventWindow==null)BuildMechanismEvents();eventOpen=true;eventPage=0;eventWindow!.gameObject.SetActive(true);eventWindow.SetAsLastSibling();RefreshMechanismEvents();Clamp();
    },"联动目标保存后生效，草稿预览不会触发联动");
    private void BuildMechanismEvents()
    {
        eventWindow=Box(root!.transform,"MechanismEvents",180,100,760,650,new Color(.07f,.095f,.12f,1)).GetComponent<RectTransform>();
        eventTitle=Label(eventWindow,"机关结束 / 循环联动（拖动标题）",12,0,610,44,19).GetComponent<RectTransform>();
        Button(eventWindow,"关闭",650,5,98,34,CloseMechanismEvents);
        Label(eventWindow,"单次模式自然结束时启动；手动结束不触发",12,54,736,30,16);
        eventComplete=MechanismField(eventWindow,12,88,736,eventCompleteDraft,8192);
        Label(eventWindow,"循环每轮 / 往返完整来回一次后启动",12,132,736,30,16);
        eventCycle=MechanismField(eventWindow,12,166,736,eventCycleDraft,8192);
        Button(eventWindow,"选择结束目标",12,214,360,34,()=>{eventCycleTab=false;RefreshMechanismEvents();});
        Button(eventWindow,"选择循环目标",388,214,360,34,()=>{eventCycleTab=true;RefreshMechanismEvents();});
        eventSummary=Label(eventWindow,"",12,260,736,30,15);
        for(int i=0;i<6;i++) { int row=i;eventTargets.Add(Button(eventWindow,"",12,298+i*38,736,34,()=>ToggleEventTarget(row))); }
        Button(eventWindow,"上一页",12,536,160,34,()=>{eventPage=Math.Max(0,eventPage-1);RefreshMechanismEvents();});
        Button(eventWindow,"下一页",184,536,160,34,()=>{int count=runtime.Mechanisms.Count(d=>d.Id!=mechanismId);eventPage=Math.Min(Math.Max(0,(count-1)/6),eventPage+1);RefreshMechanismEvents();});
        Button(eventWindow,"清空当前目标",356,536,184,34,()=>{if(eventCycleTab)eventCycle!.text="";else eventComplete!.text="";RefreshMechanismEvents();});
        Button(eventWindow,"保存机关",552,536,196,34,()=>MechanismAction(()=>{SaveMechanism();RefreshMechanismEvents();},"机关联动已保存"));
        eventHint=Label(eventWindow,"",12,582,736,60,15);
    }
    private void ToggleEventTarget(int row)=>MechanismAction(()=>
    {
        var candidates=runtime.Mechanisms.Where(d=>d.Id!=mechanismId).ToArray();int index=eventPage*6+row;if(index>=candidates.Length)return;
        var selected=ReadMechanismEventTargets(eventCycleTab).ToList();string id=candidates[index].Id;if(!selected.Remove(id))selected.Add(id);
        (eventCycleTab?eventCycle!:eventComplete!).text=string.Join(",",selected);RefreshMechanismEvents();
    },"目标选择已改为草稿，保存机关后生效");
    private void RefreshMechanismEvents()
    {
        if(!eventOpen)return;
        eventHint.text=(Time.unscaledTime<editorMessageUntil?editorMessage:"点击列表勾选多个目标；也可填逗号分隔的机关ID。")+"\n全局启动预留联动目标及其子树，等待来源机关启动。";
        var candidates=runtime.Mechanisms.Where(d=>d.Id!=mechanismId).ToArray();var selected=ReadMechanismEventTargets(eventCycleTab).ToHashSet(StringComparer.Ordinal);
        eventPage=Math.Clamp(eventPage,0,Math.Max(0,(candidates.Length-1)/6));eventSummary.text=$"{(eventCycleTab?"循环一轮":"自然结束")}目标 · 第 {eventPage+1}/{Math.Max(1,(candidates.Length+5)/6)} 页 · 可选多个";
        for(int i=0;i<eventTargets.Count;i++) { int index=eventPage*6+i;var button=eventTargets[i];button.gameObject.SetActive(index<candidates.Length);if(index>=candidates.Length)continue;var d=candidates[index];button.GetComponentInChildren<Text>().text=(selected.Contains(d.Id)?"[√] ":"[ ] ")+d.Name+" ("+d.Id+")"; }
    }
}
