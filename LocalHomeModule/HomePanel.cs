using System.Globalization;
using mod;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LocalHomeModule;

// UGUI driven by the existing module update loop; no injected behaviour or hotkeys.
internal sealed partial class HomePanel : IDisposable
{
    private readonly HomeRuntime runtime;
    private GameObject? root, events, body;
    private RectTransform? window, title;
    private CanvasScaler? scaler;
    private Font? font;
    private bool ownsFont, minimized, dragging, cursorSaved, oldVisible;
    private CursorLockMode oldLock;
    private Vector2 mouseStart, windowStart;
    private Text status=null!, notice=null!, selection=null!;
    private InputField config=null!, x=null!, y=null!, z=null!, pitch=null!, yaw=null!, roll=null!, scale=null!, step=null!, angle=null!;
    private bool requested=true;
    internal void SetVisible(bool value)
    {
        requested=value;
        if(value && root!=null && minimized) SetMinimized(false);
        if(value && mechanismOpen && mechanismMinimized) MinimizeMechanisms();
        if(!value) { if(root!=null)root.SetActive(false); if(events!=null)events.SetActive(false); dragging=false; CancelMechanismTreeDrag(); CancelRotationPreview(); mechanismMarker.Hide(); trackMarker.Hide(); RestoreCursor(); }
    }
    private GameObject? catalogBody;
    private FurnitureCatalog? catalog;
    private InputField catalogId=null!, catalogName=null!, catalogSearch=null!;
    private string searchText="";
    private CatalogItem[] catalogResults=Array.Empty<CatalogItem>();
    private Button catalogPrevious=null!, catalogNext=null!;
    private Text catalogNotice=null!;
    private readonly List<Text> catalogLabels=new();
    private readonly List<Button> catalogUses=new(), catalogDeletes=new();
    private int catalogPage;
    private readonly List<Button> buttons=new();
    private readonly List<InputField> fields=new();
    private readonly List<Button> editing=new();
    private const int FurnitureRows=3;
    private readonly Button[] rows=new Button[FurnitureRows];
    private readonly Text[] rowTexts=new Text[FurnitureRows];
    private readonly HashSet<int> selectedIds=new();
    private bool multiSelect;
    private string selectionIdentity="";
    private Button selectionMode=null!;
    private Button toggle=null!, minimize=null!, previous=null!, next=null!, delete=null!, clear=null!;
    private int page, selected;
    private string stamp="", confirm="";
    private float confirmUntil, refreshAt;
    private RectTransform? draggedWindow;
    private readonly MechanismPositionMarker mechanismMarker=new();
    internal HomePanel(HomeRuntime runtime) { this.runtime=runtime; }
    internal void Tick()
    {
        bool show=requested && runtime.Available;
        if(show && root==null) Build();
        if(root==null) return;
        root.SetActive(show);
        if(!show) { if(events!=null) events.SetActive(false); dragging=false; CancelMechanismTreeDrag(); CancelRotationPreview(); mechanismMarker.Hide(); trackMarker.Hide(); RestoreCursor(); return; }
        EnsureEvents();
        if(!cursorSaved) { oldVisible=Cursor.visible; oldLock=Cursor.lockState; cursorSaved=true; }
        // Once minimized, allow the game's camera controls to change cursor state.
        if(eventOpen || trackOpen || rotationOpen || !minimized || (patternOpen && !patternMinimized) || (mechanismOpen && !mechanismMinimized)) { Cursor.visible=true; Cursor.lockState=CursorLockMode.None; }
        scaler!.scaleFactor=Mathf.Max(.01f,Mathf.Min(1f,Mathf.Min(Screen.width/1080f,Screen.height/954f)));
        var mouse=(Vector2)Input.mousePosition;
        var hit=WindowAt(mouse);
        if(Input.GetMouseButtonDown(0) && hit!=null)
        {
            hit.SetAsLastSibling();
            var header=hit==eventWindow?eventTitle:hit==trackWindow?trackTitle:hit==rotationWindow?rotationTitle:hit==mechanismWindow?mechanismTitle:hit==patternWindow?patternTitle:title;
            if(RectTransformUtility.RectangleContainsScreenPoint(header,mouse,null))
            { dragging=true; draggedWindow=hit; mouseStart=mouse; windowStart=hit.anchoredPosition; }
        }
        TickMechanismTree(mouse,hit==mechanismWindow && !dragging);
        if(!Input.GetMouseButton(0)) { dragging=false; draggedWindow=null; }
        if(dragging && draggedWindow!=null) draggedWindow.anchoredPosition=windowStart+(mouse-mouseStart)/scaler.scaleFactor;
        Clamp(); TickRotationPreview(); UpdateMechanismMarker();
        if(Time.unscaledTime>=refreshAt) { refreshAt=Time.unscaledTime+.15f; Refresh(); }
    }
    private void Clamp()
    {
        float w=Screen.width/scaler!.scaleFactor, h=Screen.height/scaler.scaleFactor;
        foreach(var active in new[]{window,patternWindow,mechanismWindow,rotationWindow,trackWindow,eventWindow})
        {
            if(active==null || !active.gameObject.activeSelf) continue;
            var p=active.anchoredPosition;
            active.anchoredPosition=new Vector2(Mathf.Clamp(p.x,0,Mathf.Max(0,w-active.sizeDelta.x)),Mathf.Clamp(p.y,Mathf.Min(0,active.sizeDelta.y-h),0));
        }
    }
    private void Build()
    {
        font=Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f=>f!=null && f.HasCharacter('家') && f.HasCharacter('园'));
        if(font==null) { font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",18); ownsFont=font!=null; }
        if(font==null) font=Resources.GetBuiltinResource<Font>("Arial.ttf");
        root=new GameObject("LocalHome_Panel"); Object.DontDestroyOnLoad(root);
        var canvas=root.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=22200;
        scaler=root.AddComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
        root.AddComponent<GraphicRaycaster>();
        EnsureEvents();
        window=Box(root.transform,"Window",0,0,1040,884,new Color(.055f,.075f,.10f,.98f)).GetComponent<RectTransform>();
        window.anchoredPosition=new Vector2(24,-64);
        title=Box(window,"DragTitle",0,0,340,42,new Color(.10f,.19f,.24f)).GetComponent<RectTransform>();
        Label(title,"本地家园  ·  拖动标题栏",12,0,320,42,19);
        mainPattern=Button(window,"图形编辑",350,5,140,32,OpenPattern);
        mainMechanisms=Button(window,"机关编辑",500,5,140,32,OpenMechanisms);
        mainUndo=Button(window,"撤销",650,5,138,32,()=>EditorAction(()=>runtime.Undo(),"已撤销",true));
        mainRedo=Button(window,"重做",796,5,120,32,()=>EditorAction(()=>runtime.Redo(),"已重做",true));
        minimize=Button(window,"最小化",936,5,98,32,()=>SetMinimized(!minimized));
        body=Box(window,"Body",0,44,520,840,new Color(0,0,0,0));
        var b=body.transform;
        status=Label(b,"",14,0,492,32,16);
        toggle=Button(b,"开启家园",14,35,118,34,()=>Run(runtime.Enabled?"off":"on"));
        Button(b,"立即同步",140,35,112,34,()=>Run("sync"),true);
        Button(b,"保存",260,35,112,34,()=>Run("save"),true);
        Button(b,"重新加载",380,35,126,34,()=>Confirm("reload",()=>Run("reload")),true);
        Label(b,"快速放置 · 输入 ID 或从右侧配置表选用",14,78,492,24,17);
        config=Field(b,14,107,200,"5043347");
        Button(b,"放在原点",224,107,282,34,()=> { var before=runtime.Items.Select(p=>p.Id).ToHashSet(); Run("add",config.text); var added=runtime.Items.FirstOrDefault(p=>!before.Contains(p.Id)); if(added!=null) Select(added); },true);
        Label(b,"我的家具 · 活动项用于下方单件编辑",14,150,480,24,17);
        selectionMode=Button(b,"单选模式",14,180,148,30,()=>
        {
            PruneSelection(); multiSelect=!multiSelect;
            if(!multiSelect) { selectedIds.Clear(); if(selected!=0) selectedIds.Add(selected); }
            confirm=""; Refresh();
        });
        Button(b,"全选（全部页）",172,180,164,30,()=>
        {
            PruneSelection(); multiSelect=true; selectedIds.Clear(); foreach(var item in runtime.Items) selectedIds.Add(item.Id);
            if(selected==0) selected=selectedIds.FirstOrDefault(); confirm=""; Refresh();
        },true);
        Button(b,"清空选择",346,180,160,30,()=> { ClearSelection(); Refresh(); });
        for(int i=0;i<FurnitureRows;i++)
        {
            int row=i; rows[i]=Button(b,"",14,217+i*32,492,29,()=> { var items=runtime.Items; int index=page*FurnitureRows+row; if(index<items.Length) ClickFurniture(items[index]); });
            rowTexts[i]=rows[i].GetComponentInChildren<Text>();
        }
        previous=Button(b,"上一页",14,347,100,30,()=> { page=Math.Max(0,page-1); Refresh(); });
        selection=Label(b,"",120,347,280,30,16);
        next=Button(b,"下一页",406,347,100,30,()=> { page++; Refresh(); });
        Label(b,"位置 X",14,387,112,22,15); Label(b,"位置 Y",134,387,112,22,15); Label(b,"位置 Z",254,387,112,22,15);
        x=Field(b,14,411,112,"0.00"); y=Field(b,134,411,112,"0.00"); z=Field(b,254,411,112,"0.00");
        Button(b,"复制",374,411,60,34,()=>CopyVector(new[]{x,y,z})); Button(b,"粘贴",440,411,66,34,()=>PasteVector(new[]{x,y,z}));
        Label(b,"旋转 X °",14,453,112,22,15); Label(b,"旋转 Y °",134,453,112,22,15); Label(b,"旋转 Z °",254,453,112,22,15);
        pitch=Field(b,14,477,112,"0.00"); yaw=Field(b,134,477,112,"0.00"); roll=Field(b,254,477,112,"0.00");
        Button(b,"复制",374,477,60,34,()=>CopyVector(new[]{pitch,yaw,roll})); Button(b,"粘贴",440,477,66,34,()=>PasteVector(new[]{pitch,yaw,roll}));
        Label(b,"统一缩放 ×",14,523,120,26,15); scale=Field(b,140,519,110,"1.00");
        Label(b,"相对原始比例 · 0.01～100",264,519,242,34,15);
        Button(b,"应用位置 / 旋转 / 缩放",14,573,240,34,Apply,true);
        Button(b,"移到角色位置",266,573,240,34,()=> { var t=TipMsgCore.i.mainplayer?.transform; if(t!=null) { x.text=F(t.position.x); y.text=F(t.position.y); z.text=F(t.position.z); Apply(); } },true);
        Label(b,"位移步长",14,617,80,30,15); step=Field(b,98,617,70,"0.5");
        Label(b,"旋转步长 °",274,617,105,30,15); angle=Field(b,386,617,120,"15");
        string[] names={"X −","X +","Y −","Y +","Z −","Z +"};
        for(int i=0;i<6;i++) { int axis=i/2, sign=i%2==0?-1:1; Button(b,"移 "+names[i],14+i*83,657,77,32,()=>Nudge(axis,sign),true); }
        for(int i=0;i<6;i++) { int axis=3+i/2, sign=i%2==0?-1:1; Button(b,"转 "+names[i],14+i*83,697,77,32,()=>Nudge(axis,sign),true); }
        delete=Button(b,"删除选中",14,741,240,34,()=>
        {
            PruneSelection(); var ids=selectedIds.OrderBy(id=>id).ToArray();
            if(ids.Length==0) return;
            Confirm("delete:"+string.Join(",",ids),()=>EditorAction(()=>runtime.DeleteGroup(ids),"已删除选中家具"));
        },true);
        clear=Button(b,"清空布局",266,741,240,34,()=>Confirm("clear",()=>Run("clear","yes")),true);
        notice=Label(b,"",14,785,492,47,15);
        notice.verticalOverflow=VerticalWrapMode.Truncate;
        BuildCatalog();
        LoadCatalog();
        Refresh();
    }
    private void LoadCatalog()
    {
        confirm="";
        try { catalog=new FurnitureCatalog(); RefreshCatalog(); }
        catch(Exception e) { catalog=null; RefreshCatalog(); catalogNotice.text="读取失败："+e.Message+"；修复后点击重新读取"; }
    }
    private void BuildCatalog()
    {
        catalogBody=Box(window!,"Catalog",520,44,520,840,new Color(.07f,.095f,.12f,1));
        var b=catalogBody.transform;
        Label(b,"家具配置表 · 本机保存",14,8,330,36,21);
        Button(b,"重新读取",362,8,144,36,LoadCatalog);
        Label(b,"配置 ID",14,60,160,24,16); Label(b,"自定义名称",194,60,300,24,16);
        catalogId=Field(b,14,90,168,config.text); catalogName=Field(b,194,90,312,""); catalogName.characterLimit=80;
        Button(b,"保存 / 更新同 ID",14,137,492,36,()=>CatalogAction(()=>catalog!.Put(catalogId.text,catalogName.text)));
        Label(b,"搜索 ID / 名称",14,183,140,34,16);
        catalogSearch=Field(b,156,183,260,""); catalogSearch.characterLimit=80;
        Button(b,"清除",426,183,80,34,()=> { catalogSearch.text=""; UpdateSearch(); });
        for(int i=0;i<8;i++)
        {
            int row=i;
            catalogLabels.Add(Label(b,"",14,230+i*55,294,46,16));
            catalogUses.Add(Button(b,"选用",314,236+i*55,90,34,()=> { var item=CatalogAt(row); if(item==null)return; config.text=item.Id.ToString(CultureInfo.InvariantCulture); catalogId.text=config.text; catalogName.text=item.Name; catalogNotice.text="已选用："+item.Name+"；点击左侧放在原点"; }));
            catalogDeletes.Add(Button(b,"删除",416,236+i*55,90,34,()=> { var item=CatalogAt(row); if(item==null)return; string key="catalog:"+item.Id; if(confirm==key && Time.unscaledTime<confirmUntil) { confirm=""; CatalogAction(()=>catalog!.Remove(item.Id)); } else { confirm=key; confirmUntil=Time.unscaledTime+4; catalogNotice.text="4 秒内再次点击相同行的删除按钮确认"; } }));
        }
        catalogPrevious=Button(b,"上一页",14,686,130,34,()=> { catalogPage=Math.Max(0,catalogPage-1); RefreshCatalog(); });
        catalogNext=Button(b,"下一页",376,686,130,34,()=> { if((catalogPage+1)*8<catalogResults.Length)catalogPage++; RefreshCatalog(); });
        catalogNotice=Label(b,"",14,730,492,48,16);
    }
    private void UpdateSearch()
    {
        string query=catalogSearch.text.Trim();
        if(query==searchText) return;
        searchText=query; catalogPage=0; confirm=""; RefreshCatalog();
    }
    private CatalogItem? CatalogAt(int row)
    { int index=catalogPage*8+row; return index<catalogResults.Length?catalogResults[index]:null; }
    private void CatalogAction(Action action)
    {
        try { if(catalog==null) throw new InvalidOperationException("配置表读取失败，请修复后重新读取"); action(); RefreshCatalog(); catalogNotice.text="已保存配置表"; }
        catch(Exception e) { catalogNotice.text=e.Message; }
    }
    private void RefreshCatalog()
    {
        var items=catalog?.Items ?? Array.Empty<CatalogItem>();
        catalogResults=items.Where(item=>searchText.Length==0 || item.Id.ToString(CultureInfo.InvariantCulture).Contains(searchText,StringComparison.OrdinalIgnoreCase) || item.Name.Contains(searchText,StringComparison.OrdinalIgnoreCase)).ToArray();
        catalogPage=Math.Clamp(catalogPage,0,Math.Max(0,(catalogResults.Length-1)/8));
        catalogPrevious.interactable=catalogPage>0;
        catalogNext.interactable=(catalogPage+1)*8<catalogResults.Length;
        for(int i=0;i<8;i++)
        {
            var item=CatalogAt(i); catalogLabels[i].text=item==null?"—":item.Id+"  "+item.Name;
            catalogUses[i].interactable=catalogDeletes[i].interactable=item!=null;
        }
        catalogNotice.text=(catalogResults.Length==0?"无匹配配置":"匹配 "+catalogResults.Length+" 条")+" / 共 "+items.Length+" 条 · 第 "+(catalogPage+1)+" / "+Math.Max(1,(catalogResults.Length+7)/8)+" 页";
    }
    private static string F(float value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
    private void ClearSelection()
    { selectedIds.Clear(); selected=0; stamp=""; confirm=""; }
    private void PruneSelection()
    {
        if(selectionIdentity!=runtime.EditorIdentity)
        { selectionIdentity=runtime.EditorIdentity; ClearSelection(); ReadOrigin(); }
        var valid=runtime.Items.Select(p=>p.Id).ToHashSet();
        selectedIds.RemoveWhere(id=>!valid.Contains(id));
        if(!valid.Contains(selected)) selected=selectedIds.FirstOrDefault();
    }
    private void Run(params string[] args) { PruneSelection(); runtime.Execute(args); Refresh(); }
    private void ClickFurniture(Placement p)
    {
        PruneSelection();
        if(multiSelect)
        {
            if(!selectedIds.Add(p.Id)) selectedIds.Remove(p.Id);
        }
        else { selectedIds.Clear(); selectedIds.Add(p.Id); }
        // The active item can remain editable even when toggled out of the group selection.
        SetActiveFurniture(p);
    }
    private void Select(Placement p)
    {
        PruneSelection(); if(!multiSelect) selectedIds.Clear(); selectedIds.Add(p.Id); SetActiveFurniture(p);
    }
    private void SetActiveFurniture(Placement p)
    { selected=p.Id; x.text=F(p.X); y.text=F(p.Y); z.text=F(p.Z); pitch.text=F(p.Pitch); yaw.text=F(p.Yaw); roll.text=F(p.Roll); scale.text=F(p.Scale); confirm=""; Refresh(); }
    private void Apply()
    {
        PruneSelection(); if(selected==0) return;
        Run("move",selected.ToString(CultureInfo.InvariantCulture),x.text,y.text,z.text,pitch.text,yaw.text,roll.text,scale.text);
        var item=runtime.Items.FirstOrDefault(p=>p.Id==selected); if(item!=null)SetActiveFurniture(item);
    }
    private void Nudge(int axis,int sign)
    {
        PruneSelection(); var p=runtime.Items.FirstOrDefault(p=>p.Id==selected); if(p==null)return;
        var field=axis>=3?angle:step;
        if(!float.TryParse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture,out float amount) || !float.IsFinite(amount) || amount<=0)
        { notice.text="步长请输入大于 0 的有限数字"; refreshAt=Time.unscaledTime+3; return; }
        float delta=sign*amount;
        Run("move",p.Id.ToString(CultureInfo.InvariantCulture),F(p.X+(axis==0?delta:0)),F(p.Y+(axis==1?delta:0)),F(p.Z+(axis==2?delta:0)),F(p.Pitch+(axis==3?delta:0)),F(p.Yaw+(axis==4?delta:0)),F(p.Roll+(axis==5?delta:0)));
        var changed=runtime.Items.FirstOrDefault(p=>p.Id==selected); if(changed!=null) SetActiveFurniture(changed);
    }
    private void Confirm(string action,Action execute)
    {
        if(confirm==action && Time.unscaledTime<confirmUntil) { confirm=""; execute(); }
        else { confirm=action; confirmUntil=Time.unscaledTime+4; notice.text="再次点击同一按钮确认操作（4 秒内）"; }
        Refresh();
    }
    private void SetMinimized(bool value)
    {
        minimized=value; dragging=false; body!.SetActive(!value); catalogBody!.SetActive(!value);
        mainPattern.gameObject.SetActive(!value); mainMechanisms.gameObject.SetActive(!value); mainUndo.gameObject.SetActive(!value); mainRedo.gameObject.SetActive(!value);
        title!.sizeDelta=new Vector2(value?170:340,42);
        title.GetComponentInChildren<Text>().text=value?"家园 · 拖动":"本地家园  ·  拖动标题栏";
        window!.sizeDelta=new Vector2(value?280:1040,value?44:884);
        minimize.GetComponent<RectTransform>().anchoredPosition=new Vector2(value?176:936,-5);
        minimize.GetComponentInChildren<Text>().text=value?"展开":"最小化";
        if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);
        Clamp();
    }
    private void Refresh()
    {
        if(root==null) return;
        UpdateSearch(); PruneSelection();
        var items=runtime.Items; var selectedItem=items.FirstOrDefault(p=>p.Id==selected);
        page=Math.Clamp(page,0,Math.Max(0,(items.Length-1)/FurnitureRows));
        status.text=runtime.Status;
        toggle.GetComponentInChildren<Text>().text=runtime.Enabled?"关闭家园":"开启家园";
        foreach(var button in editing) button.interactable=runtime.Enabled;
        previous.interactable=page>0; next.interactable=(page+1)*FurnitureRows<items.Length;
        selectionMode.GetComponentInChildren<Text>().text=multiSelect?"多选模式":"单选模式";
        selection.text="已选 "+selectedIds.Count+" · 活动 "+(selected==0?"无":"#"+selected)+" · "+(page+1)+"/"+Math.Max(1,(items.Length+FurnitureRows-1)/FurnitureRows);
        string current=selectedItem?.ToString() ?? "";
        if(stamp!=current)
        {
            stamp=current;
            if(selectedItem!=null) { x.text=F(selectedItem.X); y.text=F(selectedItem.Y); z.text=F(selectedItem.Z); pitch.text=F(selectedItem.Pitch); yaw.text=F(selectedItem.Yaw); roll.text=F(selectedItem.Roll); scale.text=F(selectedItem.Scale); }
        }
        for(int i=0;i<FurnitureRows;i++)
        {
            int index=page*FurnitureRows+i; bool exists=index<items.Length; rows[i].interactable=exists && runtime.Enabled;
            rowTexts[i].text=exists?(selectedIds.Contains(items[index].Id)?"☑ ":"□ ")+"#"+items[index].Id+(items[index].Id==selected?" *":"")+" 配置 "+items[index].Item+" ("+F(items[index].X)+", "+F(items[index].Y)+", "+F(items[index].Z)+")":"—";
            rows[i].GetComponent<Image>().color=exists && selectedIds.Contains(items[index].Id)?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        }
        delete.interactable=runtime.Enabled && selectedIds.Count>0;
        delete.GetComponentInChildren<Text>().text=confirm.StartsWith("delete:",StringComparison.Ordinal) && Time.unscaledTime<confirmUntil?"再次点击确认删除":"删除选中（"+selectedIds.Count+"）";
        clear.GetComponentInChildren<Text>().text=confirm=="clear" && Time.unscaledTime<confirmUntil?"再次点击确认清空":"清空布局";
        notice.text=Time.unscaledTime<editorMessageUntil?editorMessage:confirm!="" && Time.unscaledTime<confirmUntil?"再次点击同一按钮确认操作（4 秒内）":runtime.Notice;
        RefreshPattern(); RefreshMechanisms(); RefreshTrack(); RefreshMechanismEvents();
    }
    private GameObject Box(Transform parent,string name,float px,float py,float w,float h,Color color)
    {
        var obj=new GameObject(name); obj.transform.SetParent(parent,false);
        var rect=obj.AddComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(px,-py); rect.sizeDelta=new Vector2(w,h);
        var image=obj.AddComponent<Image>(); image.color=color; image.raycastTarget=true; return obj;
    }
    private Text Label(Transform parent,string text,float px,float py,float w,float h,int size)
    {
        var obj=new GameObject("Label"); obj.transform.SetParent(parent,false); var t=obj.AddComponent<Text>(); t.font=font; t.fontSize=size; t.color=new Color(.91f,.95f,.98f); t.text=text; t.raycastTarget=false; t.supportRichText=false; t.alignment=TextAnchor.MiddleLeft;
        var rect=obj.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(px,-py); rect.sizeDelta=new Vector2(w,h); return t;
    }
    private Button Button(Transform parent,string text,float px,float py,float w,float h,Action action,bool edit=false)
    {
        var obj=Box(parent,"Button",px,py,w,h,new Color(.13f,.20f,.26f)); var b=obj.AddComponent<Button>(); b.targetGraphic=obj.GetComponent<Image>(); b.onClick.AddListener(action); buttons.Add(b); if(edit)editing.Add(b);
        Label(obj.transform,text,0,0,w,h,16).alignment=TextAnchor.MiddleCenter; return b;
    }
    private InputField Field(Transform parent,float px,float py,float w,string value)
    {
        var obj=Box(parent,"Input",px,py,w,34,new Color(.09f,.13f,.18f)); var t=Label(obj.transform,value,8,0,w-16,34,17); t.horizontalOverflow=HorizontalWrapMode.Overflow;
        var f=obj.AddComponent<InputField>(); f.targetGraphic=obj.GetComponent<Image>(); f.textComponent=t; f.lineType=InputField.LineType.SingleLine; f.characterLimit=24; f.text=value; fields.Add(f); return f;
    }
    private void EnsureEvents()
    {
        bool borrowed=Resources.FindObjectsOfTypeAll<EventSystem>().Any(e=>e!=null && e.gameObject!=events && e.enabled && e.gameObject.activeInHierarchy);
        if(borrowed) { if(events!=null) events.SetActive(false); return; }
        if(events==null)
        { events=new GameObject("LocalHome_Events"); Object.DontDestroyOnLoad(events); events.AddComponent<EventSystem>(); events.AddComponent<StandaloneInputModule>(); }
        events.SetActive(true);
    }
    private void RestoreCursor()
    {
        if(!cursorSaved)return;
        if(Cursor.visible && Cursor.lockState==CursorLockMode.None) { Cursor.visible=oldVisible; Cursor.lockState=oldLock; }
        cursorSaved=false;
    }
    public void Dispose()
    {
        CancelRotationPreview(); mechanismMarker.Dispose(); trackMarker.Dispose(); CancelMechanismTreeDrag(); RestoreCursor(); foreach(var b in buttons) if(b!=null)b.onClick.RemoveAllListeners(); buttons.Clear();
        if(root!=null) { if(EventSystem.current!=null && EventSystem.current.currentSelectedGameObject!=null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(root.transform)) EventSystem.current.SetSelectedGameObject(null); root.SetActive(false); Object.Destroy(root); }
        if(events!=null)Object.Destroy(events); if(ownsFont && font!=null)Object.Destroy(font); root=null;
    }
}
