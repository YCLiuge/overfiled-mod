using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? patternWindow, patternTitle;
    private bool patternOpen, movePatternOrigin, patternMinimized;
    private Button patternMinimize=null!, patternClose=null!;
    private Button mainPattern=null!, mainUndo=null!, mainRedo=null!, patternUndo=null!, patternRedo=null!, followOrigin=null!;
    private Text patternNotice=null!, patternSummary=null!;
    private InputField patternConfig=null!, patternCount=null!, patternSpacing=null!, patternRadius=null!;
    private InputField[] originFields=Array.Empty<InputField>(), transformFields=Array.Empty<InputField>();
    private readonly List<Button> shapeButtons=new(), scopeButtons=new();
    private string patternShape="line", patternScope="recent", editorMessage="";
    private float editorMessageUntil;
    private int shapePage;
    private Text shapePageLabel=null!, shapeHint=null!;
    private InputField originStep=null!;
    private static readonly string[] ShapeIds={"line","circle","sphere","grid","cube","disk","ball","cylinder","cone","helix","spiral","triangle","square","pentagon","hexagon","star","ellipse","rectangle","octagon","torus","capsule","doublehelix","heart","wave"};
    private static readonly string[] ShapeNames={"直线","圆环","球面","网格","立方体","圆盘","实心球","圆柱","圆锥","螺旋线","平面螺线","三角形","正方形","五边形","六边形","星形","椭圆","长方形","八边形","圆环体","胶囊体","双螺旋","心形","波浪线"};

    private void OpenPattern()
    {
        EditorAction(()=>
        {
            if(patternWindow==null) BuildPattern();
            patternConfig.text=config.text;
            ReadOrigin();
            dragging=false; patternOpen=true;
            if(patternMinimized) MinimizePattern();
            patternWindow!.gameObject.SetActive(true); patternWindow.SetAsLastSibling();
            ClearEditorFocus(); Clamp();
        },"图形围绕已保存的原点生成；修改原点输入后请先点设置原点");
    }
    private void ClosePattern()
    {
        if(patternWindow!=null)
        {
            config.text=patternConfig.text;
            patternWindow.gameObject.SetActive(false);
        }
        patternOpen=false; dragging=false;
        ClearEditorFocus(); Clamp();
    }
    private void MinimizePattern()
    {
        patternMinimized=!patternMinimized; dragging=false;
        for(int i=0;i<patternWindow!.childCount;i++)
        {
            var child=patternWindow.GetChild(i);
            if(child==patternTitle || child==patternMinimize.transform || child==patternClose.transform) continue;
            child.gameObject.SetActive(!patternMinimized);
        }
        patternWindow.sizeDelta=new Vector2(patternMinimized?420:840,patternMinimized?44:884);
        patternTitle!.sizeDelta=new Vector2(patternMinimized?170:570,42);
        patternMinimize.GetComponent<RectTransform>().anchoredPosition=new Vector2(patternMinimized?176:580,-5);
        patternClose.GetComponent<RectTransform>().anchoredPosition=new Vector2(patternMinimized?302:710,-5);
        patternMinimize.GetComponentInChildren<Text>().text=patternMinimized?"展开":"最小化";
        ClearEditorFocus(); Clamp();
    }
    private static void ClearEditorFocus()
    {
        if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);
    }
    private void BuildPattern()
    {
        patternWindow=Box(root!.transform,"PatternWindow",0,0,840,884,new Color(.055f,.075f,.10f,.98f)).GetComponent<RectTransform>();
        patternWindow.anchoredPosition=window!.anchoredPosition;
        patternTitle=Box(patternWindow,"PatternDrag",0,0,570,42,new Color(.10f,.19f,.24f)).GetComponent<RectTransform>();
        Label(patternTitle,"图形编辑  ·  拖动标题栏",12,0,550,42,19);
        patternMinimize=Button(patternWindow,"最小化",580,5,120,32,MinimizePattern);
        patternClose=Button(patternWindow,"关闭",710,5,120,32,ClosePattern);
        var p=patternWindow.transform;
        Label(p,"编辑原点 · 世界位置 / 旋转（角度）",16,50,800,28,18);
        originFields=EditorVectorRows(p,84,"原点位置", "原点旋转", "0.00");
        Button(p,"设置原点",16,172,252,34,()=>EditorAction(()=>
            runtime.SetOrigin(new EditorOrigin(EditorNumber(originFields[0]),EditorNumber(originFields[1]),EditorNumber(originFields[2]),EditorNumber(originFields[3]),EditorNumber(originFields[4]),EditorNumber(originFields[5]))),"已设置原点",true),true);
        Button(p,"取角色位置 / 朝向",280,172,264,34,()=>EditorAction(()=>runtime.OriginAtPlayer(),"原点已取角色变换",true),true);
        Button(p,"取选中家具变换",556,172,268,34,()=>EditorAction(()=>
        {
            if(selected==0) throw new InvalidOperationException("请先返回主面板选择一件家具");
            runtime.OriginAtFurniture(selected);
        },"原点已取选中家具变换",true),true);
        Label(p,"步长",16,214,60,32,16); originStep=Field(p,76,214,106,"0.50");
        string[] axes={"X −","X +","Y −","Y +","Z −","Z +"};
        for(int i=0;i<6;i++)
        {
            int axis=i/2, sign=i%2==0?-1:1;
            Button(p,axes[i],192+i*106,214,100,34,()=>EditorAction(()=>
            {
                float amount=EditorNumber(originStep);
                if(amount<=0) throw new ArgumentException("原点步长须大于 0");
                runtime.MoveOrigin(axis==0?sign*amount:0,axis==1?sign*amount:0,axis==2?sign*amount:0);
            },"已移动已保存原点，家具保持原位",true),true);
        }
        Label(p,"图形 · 使用已保存原点",16,254,400,28,18);
        Button(p,"上一页",440,254,112,28,()=> { shapePage=Math.Max(0,shapePage-1); RefreshPattern(); });
        shapePageLabel=Label(p,"",564,254,120,28,16);
        Button(p,"下一页",696,254,128,28,()=> { shapePage=Math.Min((ShapeIds.Length-1)/8,shapePage+1); RefreshPattern(); });
        for(int i=0;i<8;i++)
        {
            int slot=i;
            shapeButtons.Add(Button(p,"",16+i%4*204,286+i/4*36,196,32,()=>
            { patternShape=ShapeIds[shapePage*8+slot]; RefreshPattern(); }));
        }
        shapeHint=Label(p,"",16,360,808,28,15);
        Label(p,"家具配置 ID",16,394,160,28,16);
        patternConfig=Field(p,180,392,220,config.text);
        Label(p,"数量",416,394,100,28,16); patternCount=Field(p,520,392,304,"12");
        Label(p,"间距",16,434,160,28,16); patternSpacing=Field(p,180,432,220,"1.00");
        Label(p,"半径",416,434,100,28,16); patternRadius=Field(p,520,432,304,"3.00");
        Button(p,"生成图形（一次操作）",16,474,808,34,()=>EditorAction(()=>
        {
            uint item=uint.Parse(patternConfig.text,NumberStyles.Integer,CultureInfo.InvariantCulture);
            int count=int.Parse(patternCount.text,NumberStyles.Integer,CultureInfo.InvariantCulture);
            runtime.GeneratePattern(patternShape,item,count,EditorNumber(patternSpacing),EditorNumber(patternRadius));
            config.text=patternConfig.text; patternScope="recent";
        },"图形已生成；当前范围已切换至最近生成组"),true);
        Label(p,"组变换 · 围绕已保存原点旋转及缩放",16,512,800,28,18);
        string[] scopes={"recent","selected","all"}, scopeNames={"最近生成组","当前多选","全部我的家具"};
        for(int i=0;i<scopes.Length;i++)
        {
            string scope=scopes[i];
            scopeButtons.Add(Button(p,scopeNames[i],16+i*272,548,264,34,()=> { patternScope=scope; RefreshPattern(); }));
        }
        transformFields=EditorVectorRows(p,590,"位移 Δ", "旋转 Δ", "0.00");
        Label(p,"缩放倍率",16,680,150,28,16);
        var groupScale=Field(p,180,678,220,"1.00");
        followOrigin=Button(p,"原点随平移：否",416,678,408,34,()=>
        { movePatternOrigin=!movePatternOrigin; RefreshPattern(); });
        Button(p,"应用组变换（一次操作）",16,722,808,34,()=>EditorAction(()=>
        {
            int[] ids=PatternIds();
            if(ids.Length==0) throw new InvalidOperationException("所选范围没有家具，请先生成或选择家具");
            runtime.TransformGroup(ids,EditorNumber(transformFields[0]),EditorNumber(transformFields[1]),EditorNumber(transformFields[2]),EditorNumber(transformFields[3]),EditorNumber(transformFields[4]),EditorNumber(transformFields[5]),EditorNumber(groupScale),movePatternOrigin);
            foreach(var field in transformFields) field.text="0.00";
            groupScale.text="1.00";
        },"已应用组变换；增量已归零，缩放已恢复为 1.00",true),true);
        patternUndo=Button(p,"撤销",16,764,396,34,()=>EditorAction(()=>runtime.Undo(),"已撤销",true));
        patternRedo=Button(p,"重做",428,764,396,34,()=>EditorAction(()=>runtime.Redo(),"已重做",true));
        patternSummary=Label(p,"",16,804,808,28,15);
        patternNotice=Label(p,"",16,836,808,40,15);
        patternNotice.verticalOverflow=VerticalWrapMode.Truncate;
        patternWindow.gameObject.SetActive(false);
    }
    private InputField[] EditorVectorRows(Transform parent,float top,string position,string rotation,string initial)
    {
        var result=new InputField[6];
        for(int row=0;row<2;row++)
        {
            Label(parent,row==0?position:rotation,16,top+row*44,144,34,16);
            for(int axis=0;axis<3;axis++)
            {
                float left=164+axis*170;
                Label(parent,new[]{"X","Y","Z"}[axis],left,top+row*44,24,34,16);
                result[row*3+axis]=Field(parent,left+28,top+row*44,132,initial);
            }
            int start=row*3;
            Button(parent,"复制",684,top+row*44,64,34,()=>CopyVector(result.Skip(start).Take(3).ToArray()));
            Button(parent,"粘贴",756,top+row*44,68,34,()=>PasteVector(result.Skip(start).Take(3).ToArray()));
        }
        return result;
    }
    private static float EditorNumber(InputField field)
    {
        if(!float.TryParse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value) || !float.IsFinite(value))
            throw new ArgumentException("请输入有效的有限数字，小数点使用 .");
        return value;
    }
    private int[] PatternIds()
    {
        PruneSelection(); var items=runtime.Items;
        if(patternScope=="all") return items.Select(p=>p.Id).ToArray();
        if(patternScope=="selected") return selectedIds.OrderBy(id=>id).ToArray();
        var existing=items.Select(p=>p.Id).ToHashSet();
        return runtime.LastGroup.Where(existing.Contains).Distinct().ToArray();
    }
    private void ReadOrigin()
    {
        if(originFields.Length!=6) return;
        var o=runtime.Origin;
        float[] values={o.X,o.Y,o.Z,o.Pitch,o.Yaw,o.Roll};
        for(int i=0;i<6;i++) originFields[i].text=F(values[i]);
    }
    private void EditorAction(Action action,string success,bool readOrigin=false)
    {
        try { PruneSelection(); action(); if(readOrigin) ReadOrigin(); editorMessage=success; }
        catch(Exception e) { editorMessage="操作失败："+e.Message; }
        editorMessageUntil=Time.unscaledTime+5;
        Refresh();
    }
    private static string ShapeParameterHint(string shape) => shape switch
    {
        "line" => "间距控制相邻距离，沿 X 轴；仅 Y 朝向原点",
        "grid" => "间距控制 XZ 网格；仅 Y 朝向原点",
        "cube" => "边长=半径×2；仅 Y 沿面内法线，上下面投影为零时取原点 Y",
        "disk" => "半径控制圆盘；仅 Y 朝向原点",
        "sphere" or "ball" => "半径控制球大小；三维旋转朝向原点",
        "cylinder" or "cone" => "高度=半径×2；仅 Y 朝向原点",
        "helix" or "doublehelix" => "半径+高度间距；每支每圈12点；仅 Y 朝向原点",
        "spiral" => "半径控制外缘，平面三圈；仅 Y 朝向原点",
        "circle" => "半径控制圆环；仅 Y 朝向原点",
        "ellipse" => "长短半轴=半径、半径÷2；仅 Y 朝向原点",
        "rectangle" => "长宽=半径×2、半径；仅 Y 沿边内法线",
        "torus" => "主半径=半径，管半径=半径×0.3；仅 Y 朝向原点",
        "capsule" => "半径控制宽，总高=半径×4；仅 Y 朝向原点",
        "heart" => "半径控制心形大小；仅 Y 朝向原点",
        "wave" => "间距控制 X，半径控制振幅；仅 Y 朝向原点",
        _ => "半径控制外接圆；仅 Y 沿所在边向内法线"
    };
    private void RefreshPattern()
    {
        mainUndo.interactable=runtime.Enabled && runtime.CanUndo;
        mainRedo.interactable=runtime.Enabled && runtime.CanRedo;
        if(patternWindow==null) return;
        patternUndo.interactable=runtime.Enabled && runtime.CanUndo;
        patternRedo.interactable=runtime.Enabled && runtime.CanRedo;
        string[] scopes={"recent","selected","all"};
        for(int i=0;i<shapeButtons.Count;i++)
        {
            int index=shapePage*8+i;
            shapeButtons[i].GetComponentInChildren<Text>().text=ShapeNames[index];
            shapeButtons[i].GetComponent<Image>().color=patternShape==ShapeIds[index]?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        }
        int activeShape=Array.IndexOf(ShapeIds,patternShape);
        shapePageLabel.text=(shapePage+1)+" / "+((ShapeIds.Length+7)/8);
        shapeHint.text=ShapeNames[activeShape]+" · "+ShapeParameterHint(patternShape);
        for(int i=0;i<scopeButtons.Count;i++) scopeButtons[i].GetComponent<Image>().color=patternScope==scopes[i]?new Color(.12f,.42f,.46f):new Color(.13f,.20f,.26f);
        followOrigin.GetComponentInChildren<Text>().text="原点随平移："+(movePatternOrigin?"是":"否");
        patternSummary.text="范围 "+PatternIds().Length+" 件 · 已选 "+selectedIds.Count+" · 活动 "+(selected==0?"无":"#"+selected)+" · 世界轴位移 / 绕原点变换";
        patternNotice.text=Time.unscaledTime<editorMessageUntil?editorMessage:runtime.Notice;
        // Origin input is deliberately only populated on open or an explicit origin/history action.
        // The 0.15s refresh must never overwrite a partially typed value.
    }
}
