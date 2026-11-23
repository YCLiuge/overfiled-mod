using UnityEngine;
using UnityEngine.UI;

namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private InputField mechanismPositionStep=null!;
    private readonly List<Button> mechanismTreeExpand=new();
    private readonly HashSet<string> mechanismTreeCollapsed=new(StringComparer.Ordinal);
    private readonly List<MechanismTree.Row> mechanismTreeVisible=new();
    private RectTransform? mechanismRootDrop;
    private Text mechanismRootDropText=null!,mechanismTreeHint=null!;
    private string mechanismTreeSource="",mechanismTreeTarget="";
    private Vector2 mechanismTreeDown;
    private bool mechanismTreeDragging,mechanismTreeRootHover;
    private float mechanismTreeClickUntil;
    private bool MechanismTreeClickBlocked => mechanismTreeDragging || Time.unscaledTime<mechanismTreeClickUntil ||
        (mechanismTreeSource.Length>0 && ((Vector2)Input.mousePosition-mechanismTreeDown).magnitude>=8f);
    private void BuildMechanismPositionNudges(Transform p,float top)
    {
        for(int axis=0;axis<3;axis++)
        {
            int a=axis;
            Button(p,new[]{"X−","Y−","Z−"}[axis],12+axis*170,top,76,30,()=>MechanismAction(()=>NudgeMechanismPosition(a,-1),"坐标草稿已步进，保存后生效"));
            Button(p,new[]{"X+","Y+","Z+"}[axis],94+axis*170,top,76,30,()=>MechanismAction(()=>NudgeMechanismPosition(a,1),"坐标草稿已步进，保存后生效"));
        }
        Button(p,"取角色坐标",532,top,172,30,()=>MechanismAction(UsePlayerMechanismPosition,"已读取角色坐标，保存后生效"));
    }
    private void ToggleMechanismTreeRow(int row)
    {
        if(triggerTab || MechanismTreeClickBlocked || mechanismTreeSource.Length>0 || row>=mechanismTreeVisible.Count) return;
        var node=mechanismTreeVisible[row]; if(!node.HasChildren) return;
        if(!mechanismTreeCollapsed.Add(node.Id)) mechanismTreeCollapsed.Remove(node.Id);
        RefreshMechanisms();
    }
    private string MechanismTreeHit(Vector2 mouse)
    {
        for(int i=0;i<mechanismTreeVisible.Count;i++)
            if(mechanismRows[i].gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(mechanismRows[i].GetComponent<RectTransform>(),mouse,null)) return mechanismTreeVisible[i].Id;
        return "";
    }
    private void CancelMechanismTreeDrag()
    {
        if(mechanismTreeSource.Length==0 && !mechanismTreeDragging && mechanismTreeTarget.Length==0 && !mechanismTreeRootHover) return;
        if(mechanismTreeDragging) mechanismTreeClickUntil=Time.unscaledTime+.25f;
        mechanismTreeSource=""; mechanismTreeTarget=""; mechanismTreeDragging=false; mechanismTreeRootHover=false;
        if(mechanismWindow!=null) PaintMechanismTreeDrag();
    }
    private void TickMechanismTree(Vector2 mouse,bool pointerAllowed)
    {
        if(!mechanismOpen || mechanismMinimized || triggerTab || mechanismWindow==null || !mechanismWindow.gameObject.activeInHierarchy)
        { CancelMechanismTreeDrag(); return; }
        if(Input.GetMouseButtonDown(0) && pointerAllowed && !MechanismTreeClickBlocked)
        {
            mechanismTreeSource=MechanismTreeHit(mouse); mechanismTreeDown=mouse;
        }
        if(mechanismTreeSource.Length==0) return;
        if((Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) && (mouse-mechanismTreeDown).magnitude>=8f)
        { mechanismTreeDragging=true; mechanismTreeClickUntil=Time.unscaledTime+.25f; }
        if(mechanismTreeDragging)
        {
            mechanismTreeTarget=pointerAllowed?MechanismTreeHit(mouse):"";
            mechanismTreeRootHover=pointerAllowed && mechanismRootDrop!=null && RectTransformUtility.RectangleContainsScreenPoint(mechanismRootDrop,mouse,null);
            PaintMechanismTreeDrag();
        }
        if(Input.GetMouseButtonUp(0))
        {
            string source=mechanismTreeSource,target=mechanismTreeTarget; bool rootDrop=mechanismTreeRootHover;
            bool commit=mechanismTreeDragging && pointerAllowed && (rootDrop || target.Length>0) && target!=source;
            CancelMechanismTreeDrag();
            if(commit) MechanismAction(()=>
            {
                var saved=runtime.Mechanisms.FirstOrDefault(n=>n.Id==source)??throw new InvalidOperationException("拖动源机关已不存在");
                string? parent=rootDrop?null:target;
                // The only saved change is hierarchy. Every other form field remains a draft.
                runtime.UpsertMechanism(saved with {ParentId=parent});
                if(mechanismId==source) mechanismParent.text=parent??"";
                if(parent!=null) mechanismTreeCollapsed.Remove(parent);
                RefreshMechanisms();
            },"拖拽仅保存层级；其他未保存输入保持不变");
        }
        else if(!Input.GetMouseButton(0)) CancelMechanismTreeDrag();
    }
    private void PaintMechanismTreeDrag()
    {
        for(int i=0;i<mechanismRows.Count;i++)
        {
            string id=i<mechanismTreeVisible.Count?mechanismTreeVisible[i].Id:"";
            mechanismRows[i].GetComponent<Image>().color=mechanismTreeDragging && id==mechanismTreeTarget?new Color(.42f,.36f,.10f):id==mechanismId?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        }
        if(mechanismRootDrop!=null) mechanismRootDrop.GetComponent<Image>().color=mechanismTreeRootHover?new Color(.42f,.36f,.10f):new Color(.10f,.24f,.28f);
        if(mechanismTreeHint==null) return;
        mechanismTreeHint.text=mechanismTreeDragging?"正在拖动机关\n放到另一行：设为其子机关\n放到根区域：取消父机关\n放到空白：取消操作\n\n拖拽仅保存已保存机关的层级，\n其他草稿输入保持。":"点击左侧 + / − 展开或折叠。\n拖动行到另一行建立父子关系，\n拖到根区域取消父机关。\n\n只保存层级，草稿内容保持。\n运行中请先重置再改层级。\n翻页前请结束本次拖动。\n\n家具挂载沿用主面板多选。";
    }
    private void RefreshMechanismTreeRows(MechanismDefinition[] defs)
    {
        // Freeze row IDs and page while a press/drag is in progress.
        if(mechanismTreeSource.Length>0) { PaintMechanismTreeDrag(); return; }
        var flat=MechanismTree.Flatten(defs.Select(n=>new MechanismTree.Node(n.Id,n.Name,n.ParentId)),mechanismTreeCollapsed);
        mechanismPage=Math.Clamp(mechanismPage,0,Math.Max(0,(flat.Length-1)/MechanismRows));
        mechanismListPage.text=(mechanismPage+1)+" / "+Math.Max(1,(flat.Length+MechanismRows-1)/MechanismRows);
        mechanismTreeVisible.Clear(); mechanismTreeVisible.AddRange(flat.Skip(mechanismPage*MechanismRows).Take(MechanismRows));
        for(int i=0;i<MechanismRows;i++)
        {
            bool exists=i<mechanismTreeVisible.Count; mechanismRows[i].gameObject.SetActive(exists); mechanismTreeExpand[i].gameObject.SetActive(exists);
            if(!exists) continue;
            var node=mechanismTreeVisible[i];
            var rowText=mechanismRows[i].GetComponentInChildren<Text>(); rowText.fontSize=13; rowText.alignment=TextAnchor.MiddleLeft;
            rowText.text=new string(' ',Math.Min(node.Depth,6)*2)+(node.Depth>0?"└ ":"")+node.Name+"\n"+runtime.MechanismState(node.Id)+(node.Depth>6?" · 层级"+node.Depth:"");
            mechanismTreeExpand[i].GetComponentInChildren<Text>().text=node.HasChildren?(node.Expanded?"−":"+"):"·";
        }
        mechanismRootDrop!.gameObject.SetActive(true); PaintMechanismTreeDrag();
    }
}
