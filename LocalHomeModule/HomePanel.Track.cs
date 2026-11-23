using System.Globalization;
using mod;
using UnityEngine;
using UnityEngine.UI;

namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? trackWindow, trackTitle;
    private bool trackOpen, trackWriting, trackInvalid;
    private int trackSelected=-1, trackPage;
    private const int TrackRows=7;
    private readonly List<MechanismPoint> trackPoints=new();
    private readonly List<Button> trackRows=new();
    private InputField[] trackPosition=Array.Empty<InputField>();
    private Text trackSummary=null!, trackPageLabel=null!, trackNotice=null!, trackSelectionLabel=null!;
    private Button trackPrevious=null!, trackNext=null!;
    private string trackSource="";

    // The hidden text remains the persistence/network source. Never throw from the marker tick.
    private MechanismPoint[] TrackMarkerPoints()
    {
        if(mechanismTrack==null) return Array.Empty<MechanismPoint>();
        if(trackSource!=mechanismTrack.text)
        {
            try
            {
                var points=ParseMechanismTrack(mechanismTrack.text);
                if(points.Length>256) return trackPoints.ToArray();
                trackPoints.Clear(); trackPoints.AddRange(points); trackSource=mechanismTrack.text;
            }
            catch { }
        }
        return trackPoints.ToArray();
    }
    private static string TrackNumber(float value) => value.ToString("G9",CultureInfo.InvariantCulture);
    private void LoadTrackDraft(MechanismPoint[] points)
    {
        CloseTrack(); trackPoints.Clear(); trackPoints.AddRange(points);
        trackSelected=points.Length>0?0:-1; trackPage=0; trackInvalid=false;
        SyncTrack(); LoadTrackSelection();
    }
    private void SyncTrack()
    {
        trackSource=string.Join(";",trackPoints.Select(p=>TrackNumber(p.X)+","+TrackNumber(p.Y)+","+TrackNumber(p.Z)));
        mechanismTrack.text=trackSource;
        RefreshTrack();
    }
    private void OpenTrack()
    {
        if(triggerTab || mechanismKind!="track") return;
        if(mechanismId.Length==0) throw new InvalidOperationException("请先新增或选择轨道机关");
        TrackMarkerPoints();
        if(trackWindow==null) { BuildTrack(); LoadTrackSelection(); }
        trackOpen=true; trackWindow!.gameObject.SetActive(true); trackWindow.SetAsLastSibling();
        RefreshTrack(); ClearEditorFocus(); Clamp();
    }
    private void CloseTrack()
    {
        trackOpen=false; trackWindow?.gameObject.SetActive(false);
        if(draggedWindow==trackWindow) { dragging=false; draggedWindow=null; }
        ClearEditorFocus();
    }
    private void BuildTrack()
    {
        trackWindow=Box(root!.transform,"TrackWindow",0,0,800,740,new Color(.055f,.075f,.10f,.99f)).GetComponent<RectTransform>();
        trackWindow.anchoredPosition=mechanismWindow!.anchoredPosition+new Vector2(40,-28);
        trackTitle=Box(trackWindow,"TrackDrag",0,0,654,42,new Color(.10f,.19f,.24f)).GetComponent<RectTransform>();
        Label(trackTitle,"轨道点编辑 · 世界坐标 · 拖动标题栏",12,0,630,42,19);
        Button(trackWindow,"关闭",674,5,114,32,CloseTrack);
        Label(trackWindow,"点为世界参考坐标；家具按点间位移运动，父机关变换仍会叠加。",16,48,768,32,16);
        Label(trackWindow,"编号           X                         Y                         Z（点击选择）",16,82,768,28,15);
        for(int i=0;i<TrackRows;i++)
        {
            int row=i;
            trackRows.Add(Button(trackWindow,"",16,114+i*42,768,38,()=>SelectTrackPoint(trackPage*TrackRows+row)));
        }
        trackPrevious=Button(trackWindow,"上一页",16,414,120,32,()=> { trackPage--; RefreshTrack(); });
        trackPageLabel=Label(trackWindow,"",150,414,480,32,15);
        trackNext=Button(trackWindow,"下一页",664,414,120,32,()=> { trackPage++; RefreshTrack(); });
        trackSelectionLabel=Label(trackWindow,"",16,452,768,28,16);
        trackPosition=MechanismVector(trackWindow,484,"选中点 XYZ");
        foreach(var field in trackPosition) field.onValueChanged.AddListener(new Action<string>(_=>TrackInputChanged()));
        Button(trackWindow,"新增末尾",16,530,144,34,()=>MechanismAction(()=>AddTrackPoint(false),"已新增轨道草稿点；保存机关后生效"));
        Button(trackWindow,"插入选中前",172,530,144,34,()=>MechanismAction(()=>AddTrackPoint(true),"已插入轨道草稿点"));
        Button(trackWindow,"删除点",328,530,144,34,()=>MechanismAction(DeleteTrackPoint,"已删除轨道草稿点"));
        Button(trackWindow,"上移",484,530,144,34,()=>MechanismAction(()=>MoveTrackPoint(-1),"已调整轨道点顺序"));
        Button(trackWindow,"下移",640,530,144,34,()=>MechanismAction(()=>MoveTrackPoint(1),"已调整轨道点顺序"));
        Button(trackWindow,"取角色位置",16,576,240,34,()=>MechanismAction(()=>
        {
            var t=TipMsgCore.i.mainplayer?.transform ?? throw new InvalidOperationException("角色尚未就绪");
            SetTrackEditor(new(t.position.x,t.position.y,t.position.z));
        },"已读取角色世界坐标；有效输入即时更新草稿"));
        Button(trackWindow,"取活动家具位置",272,576,240,34,()=>MechanismAction(()=>
        {
            PruneSelection(); var p=runtime.Items.FirstOrDefault(p=>p.Id==selected) ?? throw new InvalidOperationException("请在主面板选择活动家具");
            SetTrackEditor(new(p.X,p.Y,p.Z));
        },"已读取活动家具世界坐标"));
        Button(trackWindow,"更新选中点",528,576,256,34,()=>MechanismAction(()=>
        {
            if(trackSelected<0) throw new InvalidOperationException("请先新增或选择路径点");
            TrackInputChanged(); EnsureTrackInput();
        },"已更新选中轨道草稿点"));
        trackNotice=Label(trackWindow,"",16,618,768,64,15);
        Button(trackWindow,"保存机关",16,690,496,36,()=>MechanismAction(SaveMechanism,"机关已保存"));
        Button(trackWindow,"关闭",528,690,256,36,CloseTrack);
        RefreshTrack();
    }
    private void SetTrackEditor(MechanismPoint p)
    {
        trackWriting=true;
        trackPosition[0].text=TrackNumber(p.X); trackPosition[1].text=TrackNumber(p.Y); trackPosition[2].text=TrackNumber(p.Z);
        trackWriting=false; TrackInputChanged();
    }
    private bool TryTrackInput(out MechanismPoint point)
    {
        point=new(); if(trackPosition.Length!=3) return false;
        var values=new float[3];
        for(int i=0;i<3;i++)
            if(!float.TryParse(trackPosition[i].text,NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]) || !float.IsFinite(values[i]) || Math.Abs(values[i])>100000) return false;
        point=new(values[0],values[1],values[2]); return true;
    }
    private void TrackInputChanged()
    {
        if(trackWriting) return;
        trackInvalid=!TryTrackInput(out var p);
        if(!trackInvalid && trackSelected>=0 && trackSelected<trackPoints.Count) { trackPoints[trackSelected]=p; SyncTrack(); }
        else RefreshTrack();
    }
    private void EnsureTrackInput()
    {
        if(trackInvalid) throw new ArgumentException("选中点 XYZ 输入未完成或非法，尚未更新列表；请修正为±100000内三个有限数字再保存");
    }
    private void LoadTrackSelection()
    {
        if(trackPosition.Length==0) { RefreshTrack(); return; }
        trackInvalid=false; SetTrackEditor(trackSelected>=0 && trackSelected<trackPoints.Count?trackPoints[trackSelected]:new());
    }
    private void SelectTrackPoint(int index)
    {
        if(index<0 || index>=trackPoints.Count) return;
        if(trackInvalid) { RefreshTrack(); return; }
        trackSelected=index; LoadTrackSelection();
    }
    private void AddTrackPoint(bool insert)
    {
        EnsureTrackInput();
        if(trackPoints.Count>=256) throw new InvalidOperationException("轨道最多 256 个点");
        var p=TryTrackInput(out var input)?input:new MechanismPoint();
        int index=insert && trackSelected>=0?trackSelected:trackPoints.Count;
        trackPoints.Insert(index,p); trackSelected=index; trackPage=index/TrackRows;
        SyncTrack(); LoadTrackSelection();
    }
    private void DeleteTrackPoint()
    {
        if(trackSelected<0 || trackSelected>=trackPoints.Count) throw new InvalidOperationException("请先选择路径点");
        trackPoints.RemoveAt(trackSelected); trackSelected=Math.Min(trackSelected,trackPoints.Count-1);
        SyncTrack(); LoadTrackSelection();
    }
    private void MoveTrackPoint(int delta)
    {
        EnsureTrackInput(); int target=trackSelected+delta;
        if(trackSelected<0 || target<0 || target>=trackPoints.Count) return;
        (trackPoints[trackSelected],trackPoints[target])=(trackPoints[target],trackPoints[trackSelected]);
        trackSelected=target; trackPage=target/TrackRows; SyncTrack(); LoadTrackSelection();
    }
    private void RefreshTrack()
    {
        if(trackSummary!=null) trackSummary.text="世界坐标路径 · "+trackPoints.Count+" / 256 个点（保存需 2～256 点）";
        if(trackWindow==null) return;
        trackPage=Math.Clamp(trackPage,0,Math.Max(0,(trackPoints.Count-1)/TrackRows));
        trackPageLabel.text="第 "+(trackPage+1)+" / "+Math.Max(1,(trackPoints.Count+TrackRows-1)/TrackRows)+" 页 · 共 "+trackPoints.Count+" 点";
        trackPrevious.interactable=trackPage>0; trackNext.interactable=(trackPage+1)*TrackRows<trackPoints.Count;
        for(int i=0;i<trackRows.Count;i++)
        {
            int n=trackPage*TrackRows+i; var row=trackRows[i]; row.gameObject.SetActive(n<trackPoints.Count);
            if(n>=trackPoints.Count) continue;
            var p=trackPoints[n]; row.GetComponentInChildren<Text>().text=(n==trackSelected?"▶ ":"   ")+(n+1)+"    X "+TrackNumber(p.X)+"     Y "+TrackNumber(p.Y)+"     Z "+TrackNumber(p.Z);
            row.GetComponent<Image>().color=n==trackSelected?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        }
        trackSelectionLabel.text=trackSelected>=0?"正在编辑第 "+(trackSelected+1)+" 点（世界坐标）":"暂无路径点；可输入 XYZ 后点击新增末尾";
        trackNotice.text=trackInvalid?"XYZ 输入未完成或非法：列表及世界标记保留上次合法值，当前输入尚未更新。\n请修正为±100000内的数字后再切换点或保存；删除点可放弃当前输入。":"合法 XYZ 输入即时更新草稿及世界标记；取位置修改选中点。\n草稿允许 0～256 点；保存机关要求 2～256 点，保存后运行生效。";
        if(!trackInvalid && Time.unscaledTime<editorMessageUntil) trackNotice.text+="\n"+editorMessage;
    }
}
