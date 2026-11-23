using System;
using System.Collections.Generic;
using System.Linq;
using ChineseChessModule.Core;
using Il2Cpp;
using Il2CppInutan;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace ChineseChessModule;

// Unity units are metres: tabletop 1.64 x 1.82 m, grid pitch .17 m, discs Ø.14 x .034 m.
internal sealed class ChessPresentation : IDisposable
{
    private readonly ChessRuntime _game;
    private GameObject? _ui, _events;
    private Font? _font;
    private bool _ownsFont;
    private Font ResolveFont()
    {
        if (_font != null) return _font;
        _font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f != null && f.HasCharacter('棋') && f.HasCharacter('帅'));
        if (_font != null) return _font;
        try { _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 64); _ownsFont = _font != null; } catch { }
        return _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
    private Text? _status;
    private readonly Text[] _cells = new Text[90];
    private readonly Image[] _images = new Image[90];
    private readonly List<Button> _buttons = new();
    private readonly Dictionary<string, Model> _models = new();
    private readonly List<Material> _materials = new();
    private Material? _wood, _ivory, _ink, _red, _gold;
    private bool _visible, _cursorSaved, _oldVisible;
    private CursorLockMode _oldLock;
    private float _nextRefresh;
    private sealed class Model { public GameObject Root = null!, Pieces = null!; public string Stamp = ""; }
    public ChessPresentation(ChessRuntime game) { _game = game; }
    public void Update()
    {
        if (_game.PanelRequested) { _game.PanelRequested = false; Show(true); }
        if (Input.GetKeyDown(KeyCode.F9)) Show(!_visible);
        if (_visible && Input.GetKeyDown(KeyCode.Escape)) Show(false);
        if (_visible) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        if (Time.unscaledTime >= _nextRefresh)
        { _nextRefresh = Time.unscaledTime + .15f; Refresh(); SyncModels(); }
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (!_visible && Input.GetMouseButtonDown(0))
        {
            // Alt bypasses the game HUD pointer guard, but must hit a chess table.
            if (alt)
            {
                try
                {
                    var owner = PickTable(true);
                    if (owner != null)
                    {
                        _game.OpenTable(owner);
                        _game.PanelRequested = false;
                        Show(true);
                    }
                }
                catch (Exception e) { MelonLoader.MelonLogger.Warning("[ChineseChess] Alt点击选桌失败：" + e.Message); }
            }
            else if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                var owner = PickTable(false);
                if (owner != null)
                {
                    _game.OpenTable(owner);
                    _game.PanelRequested = false;
                    Show(true);
                }
            }
        }
    }
    private string? PickTable(bool explicitShortcut)
    {
        // This game's rendering camera need not carry Unity's MainCamera tag.
        var camera = CameraSystem.Instance?.MainCamera;
        if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
        if (camera == null) return null;
        var point = Cursor.lockState == CursorLockMode.Locked
            ? new Vector3(Screen.width * .5f, Screen.height * .5f, 0) : Input.mousePosition;
        var ray = camera.ScreenPointToRay(point);
        if (!explicitShortcut)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore) || hit.transform == null) return null;
            foreach (var pair in _models)
                if (pair.Value.Root != null && (hit.transform == pair.Value.Root.transform || hit.transform.IsChildOf(pair.Value.Root.transform)))
                    return pair.Key;
            return null;
        }
        // Test only our tabletop volumes: player/scene colliders, global trigger
        // settings and delayed physics transform updates cannot swallow Alt+click.
        string? selected = null;
        float nearest = 100f;
        var tabletop = new Bounds(new Vector3(0, .84f, 0), new Vector3(1.64f, .24f, 1.82f));
        foreach (var pair in _models)
        {
            if (pair.Value.Root == null || !pair.Value.Root.activeInHierarchy) continue;
            var root = pair.Value.Root.transform;
            var localRay = new Ray(root.InverseTransformPoint(ray.origin), root.InverseTransformDirection(ray.direction));
            if (tabletop.IntersectRay(localRay, out float distance) && distance >= 0 && distance < nearest)
            { nearest = distance; selected = pair.Key; }
        }
        return selected;
    }
    private void Show(bool value)
    {
        if (value && _ui == null) Build();
        if (value && !_cursorSaved) { _oldVisible = Cursor.visible; _oldLock = Cursor.lockState; _cursorSaved = true; }
        _visible = value; if (_ui != null) _ui.SetActive(value);
        if (!value && _cursorSaved) { Cursor.visible = _oldVisible; Cursor.lockState = _oldLock; _cursorSaved = false; }
        Refresh();
    }
    private void Run(Action action) { try { action(); } catch (Exception e) { _game.Notice = e.Message; } Refresh(); }
    private void Build()
    {
        ResolveFont();
        _ui = new GameObject("ChineseChess_UI"); Object.DontDestroyOnLoad(_ui);
        var canvas = _ui.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 22100;
        var scaler = _ui.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1200, 800);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _ui.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) { _events = new GameObject("ChineseChess_Events"); Object.DontDestroyOnLoad(_events); _events.AddComponent<EventSystem>(); _events.AddComponent<StandaloneInputModule>(); }
        var panel = Box(_ui.transform, "Panel", 0, 0, 1100, 744, new Color(.08f, .10f, .12f, .98f)).transform;
        Label(panel, "中国象棋", -170, 331, 560, 42, 30, new Color(.95f, .8f, .5f));
        var board = Box(panel, "Board", -170, 0, 558, 612, new Color(.78f, .57f, .30f)).transform;
        // Grid has a gap through the river except along the two outside files.
        for (int y = 0; y < 10; y++) Box(board, "Rank", 0, 270-y*60, 480, 2, Color.black);
        for (int x = 0; x < 9; x++)
        {
            if (x == 0 || x == 8) Box(board, "File", -240+x*60, 0, 2, 540, Color.black);
            else { Box(board, "File", -240+x*60, 150, 2, 240, Color.black); Box(board, "File", -240+x*60, -150, 2, 240, Color.black); }
        }
        foreach (int sign in new[] {-1,1}) foreach (int angle in new[] {-45,45})
        { var diagonal = Box(board, "Palace", 0, sign*210, 170, 2, Color.black); diagonal.transform.localRotation = Quaternion.Euler(0,0,angle); }
        Label(board, "楚 河          汉 界", 0, 0, 420, 50, 27, new Color(.25f,.11f,.04f));
        for (int i = 0; i < 90; i++)
        {
            int cell = i; var button = Button(board, "", -240+i%9*60, 270-i/9*60, 48, 48, () => Run(() => _game.Click(cell)));
            _images[i] = button.GetComponent<Image>(); _cells[i] = button.GetComponentInChildren<Text>(); _cells[i].fontSize = 29;
        }
        _status = Label(panel, "", 350, 185, 290, 280, 19, Color.white);
        Button(panel, "放置棋盘 / 创建房间", 350, -30, 260, 40, () => Run(_game.Place));
        Button(panel, "切换频道棋桌", 350, -100, 260, 40, () => Run(() => { var keys = _game.Tables.Keys.ToArray(); if (keys.Length > 0) _game.Select(keys[(Array.IndexOf(keys, _game.Selected)+1)%keys.Length]); }));
        Button(panel, "加入黑方", 280, -150, 125, 40, () => Run(_game.Join));
        Button(panel, "离开棋桌", 420, -150, 125, 40, () => Run(_game.Leave));
        Button(panel, "认输", 280, -200, 125, 40, () => Run(_game.Resign));
        Button(panel, "新局（房主）", 420, -200, 125, 40, () => Run(_game.Reset));
        Button(panel, "关闭 · F9", 350, -257, 260, 40, () => Show(false));
        Label(panel, "先点棋子，再点落点 · 绿色为合法落点\n联机前用 net 连接宿主网络，同频道可见棋桌\n隐藏面板后 Alt＋左键点击棋盘加入", -90, -337, 830, 50, 16, new Color(.75f,.78f,.8f));
    }
    private void Refresh()
    {
        if (_ui == null || !_visible) return;
        var table = _game.Current; var chess = table?.Game;
        _status!.text = "中国象棋 · " + (table == null ? "尚未选择棋桌" : table.Local ? "本地双人" : table.Name + "的棋桌") +
            "\n\n" + (chess?.Status ?? "放置棋盘即创建房间，点击棋盘加入") + "\n" + (table == null || table.Local ? "" : "红方：房主\n黑方：" + (table.Black.Length == 0 ? "等待加入" : "已入座")) + "\n\n" + _game.Notice;
        int[] targets = chess != null && _game.SelectedIndex >= 0 ? chess.LegalTargets(_game.SelectedIndex) : Array.Empty<int>();
        for (int i = 0; i < 90; i++)
        {
            var p = chess?.Board[i]; _cells[i].text = p == null ? "" : Glyph(p);
            _cells[i].color = p?.Side == Side.Red ? new Color(.7f,.05f,.04f) : new Color(.08f,.08f,.09f);
            _images[i].color = i == _game.SelectedIndex ? new Color(1,.85f,.25f) : targets.Contains(i) ? new Color(.4f,.8f,.45f,.85f) : p == null ? new Color(0,0,0,0) : new Color(.99f,.88f,.65f);
        }
    }
    internal static string Glyph(Piece p) => p.Kind switch
    { 'K' => p.Side == Side.Red ? "帅" : "将", 'A' => p.Side == Side.Red ? "仕" : "士", 'B' => p.Side == Side.Red ? "相" : "象", 'N' => "马", 'R' => "车", 'C' => "炮", _ => p.Side == Side.Red ? "兵" : "卒" };
    private GameObject Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var obj = new GameObject(name); obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = new Vector2(x,y); rect.sizeDelta = new Vector2(w,h);
        var image = obj.AddComponent<Image>(); image.color = color; image.raycastTarget = name.StartsWith("Button", StringComparison.Ordinal) || name == "Panel";
        return obj;
    }
    private Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color)
    {
        var obj = new GameObject("Label"); obj.transform.SetParent(parent, false); var label = obj.AddComponent<Text>();
        label.font = _font; label.text = text; label.fontSize = size; label.color = color; label.supportRichText = false; label.raycastTarget = false; label.alignment = TextAnchor.MiddleCenter;
        var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = new Vector2(x,y); rect.sizeDelta = new Vector2(w,h); return label;
    }
    private Button Button(Transform parent, string text, float x, float y, float w, float h, Action action)
    {
        var obj = Box(parent, "Button_"+text, x,y,w,h,new Color(.23f,.30f,.33f)); var button = obj.AddComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>();
        button.onClick.AddListener(action); _buttons.Add(button); Label(obj.transform, text,0,0,w,h,18,Color.white); return button;
    }
    private Material Mat(string name, Color color)
    {
        var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard") ?? throw new InvalidOperationException("找不到棋桌着色器");
        var mat = new Material(shader) { name = "ChineseChess_"+name, color = color }; _materials.Add(mat); return mat;
    }
    private void EnsureMaterials()
    {
        if (_wood != null) return;
        _wood = Mat("Rosewood", new Color(.22f,.09f,.045f)); _ivory = Mat("Ivory",new Color(.86f,.67f,.39f)); _ink = Mat("Ink",new Color(.07f,.065f,.06f)); _red = Mat("Cinnabar",new Color(.72f,.05f,.035f)); _gold = Mat("Gold",new Color(.95f,.73f,.25f));
    }
    private GameObject Part(Transform root, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(root,false); obj.transform.localPosition = pos; obj.transform.localScale = scale; obj.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var collider = obj.GetComponent<Collider>(); if (collider != null) Object.Destroy(collider); return obj;
    }
    private void SyncModels()
    {
        foreach (var key in _models.Keys.Where(k => !_game.Tables.ContainsKey(k)).ToArray()) { Object.Destroy(_models[key].Root); _models.Remove(key); }
        foreach (var pair in _game.Tables)
        {
            var table = pair.Value;
            EnsureMaterials();
            if (!_models.TryGetValue(pair.Key,out var model))
            {
                model = new Model { Root = new GameObject("ChineseChess_Table_"+pair.Key) }; Object.DontDestroyOnLoad(model.Root); _models[pair.Key] = model;
                var root = model.Root.transform;
                Part(root,"Frame",PrimitiveType.Cube,new Vector3(0,.78f,0),new Vector3(1.64f,.10f,1.82f),_wood!);
                Part(root,"Board",PrimitiveType.Cube,new Vector3(0,.838f,0),new Vector3(1.52f,.016f,1.70f),_ivory!);
                foreach (int sx in new[] {-1,1}) foreach (int sz in new[] {-1,1}) Part(root,"Leg",PrimitiveType.Cube,new Vector3(sx*.64f,.365f,sz*.72f),new Vector3(.09f,.73f,.09f),_wood!);
                for(int y=0;y<10;y++) Line(root,new Vector3(-.68f,.85f,(4.5f-y)*.17f),new Vector3(.68f,.85f,(4.5f-y)*.17f));
                for(int x=0;x<9;x++)
                {
                    float px=(x-4)*.17f;
                    if(x==0||x==8) Line(root,new Vector3(px,.85f,-.765f),new Vector3(px,.85f,.765f));
                    else foreach(int s in new[]{-1,1}) Line(root,new Vector3(px,.85f,s*.085f),new Vector3(px,.85f,s*.765f));
                }
                foreach(int s in new[]{-1,1}) { Line(root,new Vector3(-.17f,.85f,s*.765f),new Vector3(.17f,.85f,s*.425f)); Line(root,new Vector3(.17f,.85f,s*.765f),new Vector3(-.17f,.85f,s*.425f)); }
                Text3D(root,"楚 河",new Vector3(-.34f,.854f,0),.40f,.065f,_ink!);
                Text3D(root,"汉 界",new Vector3(.34f,.854f,0),.40f,.065f,_ink!);
                var hitbox=model.Root.AddComponent<BoxCollider>(); hitbox.center=new Vector3(0,.80f,0); hitbox.size=new Vector3(1.64f,.16f,1.82f);
            }
            model.Root.transform.position = new Vector3(table.X,table.Y,table.Z); model.Root.transform.rotation=Quaternion.Euler(0,table.Yaw,0);
            string stamp=table.Instance+"/"+table.Revision+"/"+table.Game.Revision;
            if (stamp == model.Stamp) continue;
            model.Stamp=stamp; if(model.Pieces!=null) { model.Pieces.SetActive(false); Object.Destroy(model.Pieces); }
            model.Pieces=new GameObject("Pieces"); model.Pieces.transform.SetParent(model.Root.transform,false);
            for(int i=0;i<90;i++)
            {
                var p=table.Game.Board[i]; if(p==null)continue; var at=new Vector3((i%9-4)*.17f,.867f,(4.5f-i/9)*.17f);
                Part(model.Pieces.transform,"Disc_"+i,PrimitiveType.Cylinder,at,new Vector3(.14f,.017f,.14f),_wood!);
                Part(model.Pieces.transform,"Face_"+i,PrimitiveType.Cylinder,at+Vector3.up*.017f,new Vector3(.128f,.002f,.128f),_ivory!);
                Text3D(model.Pieces.transform,Glyph(p),at+Vector3.up*.021f,.090f,.090f,p.Side==Side.Red?_red!:_ink!);
            }
        }
    }
    private void Line(Transform parent, Vector3 a, Vector3 b)
    { var obj=Part(parent,"Grid",PrimitiveType.Cube,(a+b)/2,new Vector3(.003f,.002f,Vector3.Distance(a,b)),_ink!); obj.transform.localRotation=Quaternion.LookRotation(b-a); }
    private void Text3D(Transform parent,string text,Vector3 at,float maxWidth,float maxHeight,Material mat)
    {
        var font = ResolveFont();
        // Measure at identity before parenting/rotation: TextMesh characterSize is
        // font-dependent and is not a glyph width in metres.
        var obj=new GameObject("Glyph_"+text);
        var mesh=obj.AddComponent<TextMesh>(); mesh.font=font; mesh.fontSize=64; mesh.characterSize=.01f;
        mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=TextAlignment.Center; mesh.color=mat.color; mesh.text=text;
        var renderer=obj.GetComponent<MeshRenderer>(); renderer.sharedMaterial=font.material;
        var bounds=renderer.bounds;
        float width=bounds.size.x, height=bounds.size.y;
        float scale=1f;
        if(width > .00001f && height > .00001f)
            scale=Mathf.Min(maxWidth/width,maxHeight/height);
        // Keep the initial small size if Unity has not generated text geometry yet.
        obj.transform.SetParent(parent,false); obj.transform.localPosition=at;
        obj.transform.localRotation=Quaternion.Euler(90,0,0); obj.transform.localScale=Vector3.one*scale;
    }
    public void Dispose()
    {
        Show(false); foreach(var button in _buttons) if(button!=null)button.onClick.RemoveAllListeners(); _buttons.Clear();
        if(_ui!=null)Object.Destroy(_ui); if(_events!=null)Object.Destroy(_events);
        foreach(var model in _models.Values)if(model.Root!=null)Object.Destroy(model.Root); _models.Clear();
        foreach(var mat in _materials)if(mat!=null)Object.Destroy(mat); _materials.Clear();
        if (_ownsFont && _font != null) Object.Destroy(_font); _font = null;
    }
}
