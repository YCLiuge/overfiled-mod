using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace LocalHomeModule;

// Editor overlay only: never spawns furniture or intercepts scene input.
internal sealed class TrackPathMarker : IDisposable
{
    private GameObject? root;
    private Camera? camera;
    private float retry;
    private readonly List<Image> dots=new(), lines=new(), badges=new();
    private readonly List<Text> labels=new();
    internal string Status { get; private set; }="";
    internal void Hide() { root?.SetActive(false); Status=""; }
    private static bool Usable(Camera? c)=>c!=null && c.isActiveAndEnabled && c.targetTexture==null && c.targetDisplay==0 && c.pixelRect.width>32 && c.pixelRect.height>32;
    private static Image Graphic(Transform parent,string name)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        var r=go.AddComponent<RectTransform>();r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);
        var image=go.AddComponent<Image>();image.raycastTarget=false;return image;
    }
    private static Vector2 ScreenPoint(Vector3 p)=>new(p.x-Screen.width*.5f,p.y-Screen.height*.5f);
    private void Build(int count,Font font)
    {
        if(root==null) { root=new GameObject("LocalHome_TrackPathOverlay");Object.DontDestroyOnLoad(root);var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=22191; }
        while(dots.Count<count)
        {
            dots.Add(Graphic(root.transform,"TrackPoint"));
            var badge=Graphic(root.transform,"TrackNumberBackground");badges.Add(badge);
            var go=new GameObject("TrackNumber");go.transform.SetParent(badge.transform,false);
            var text=go.AddComponent<Text>();text.font=font;text.fontSize=16;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.supportRichText=false;
            var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(52,24);labels.Add(text);
        }
        while(lines.Count<Math.Max(0,count-1)) { var line=Graphic(root.transform,"TrackSegment");line.transform.SetAsFirstSibling();lines.Add(line); }
    }
    internal void Update(MechanismPoint[] points,int selected,Font font)
    {
        if(points.Length==0 || points.Length>256 || points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||Math.Abs(p.X)>100000||Math.Abs(p.Y)>100000||Math.Abs(p.Z)>100000)) { Hide();return; }
        if(Time.unscaledTime>=retry)
        {
            retry=Time.unscaledTime+1;camera=Camera.main;
            if(!Usable(camera)||camera!.orthographic) camera=Resources.FindObjectsOfTypeAll<Camera>().Where(Usable).OrderBy(c=>c.orthographic).ThenByDescending(c=>c.pixelRect.width*c.pixelRect.height).ThenBy(c=>c.depth).FirstOrDefault();
        }
        if(!Usable(camera)) { Hide();Status="轨道标记：等待场景相机";return; }
        Build(points.Length,font);root!.SetActive(true);
        var screen=points.Select(p=>camera!.WorldToScreenPoint(new Vector3(p.X,p.Y,p.Z))).ToArray();int visible=0;
        for(int i=0;i<dots.Count;i++)
        {
            bool show=i<screen.Length && screen[i].z>.01f && float.IsFinite(screen[i].x) && float.IsFinite(screen[i].y) && screen[i].x>=camera!.pixelRect.xMin && screen[i].x<=camera.pixelRect.xMax && screen[i].y>=camera.pixelRect.yMin && screen[i].y<=camera.pixelRect.yMax;
            dots[i].gameObject.SetActive(show);badges[i].gameObject.SetActive(show);if(!show)continue;visible++;
            bool active=i==selected;var color=active?new Color(1,.8f,.1f,1):new Color(.2f,1,.9f,1);
            dots[i].color=color;var r=dots[i].GetComponent<RectTransform>();r.anchoredPosition=ScreenPoint(screen[i]);r.sizeDelta=new Vector2(active?15:10,active?15:10);r.localRotation=Quaternion.Euler(0,0,45);
            var b=badges[i].GetComponent<RectTransform>();b.anchoredPosition=ScreenPoint(screen[i])+new Vector2(28,19);b.sizeDelta=new Vector2(52,24);badges[i].color=new Color(.02f,.04f,.06f,.85f);
            labels[i].text=(i+1).ToString()+(active?" *":"");labels[i].color=color;
        }
        for(int i=0;i<lines.Count;i++)
        {
            bool show=i+1<screen.Length && screen[i].z>.01f && screen[i+1].z>.01f;var line=lines[i];
            if(show)
            {
                var a=ScreenPoint(screen[i]);var b=ScreenPoint(screen[i+1]);var d=b-a;
                show=float.IsFinite(d.x)&&float.IsFinite(d.y)&&d.magnitude<=Mathf.Max(Screen.width,Screen.height)*8;
                if(show) { var r=line.GetComponent<RectTransform>();r.anchoredPosition=(a+b)*.5f;r.sizeDelta=new Vector2(Mathf.Max(1,d.magnitude),i==selected||i+1==selected?3:2);r.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg);line.color=i==selected||i+1==selected?new Color(1,.8f,.1f,.9f):new Color(.2f,1,.9f,.7f); }
            }
            line.gameObject.SetActive(show);
        }
        Status=$"轨道点 {visible}/{points.Length} 在视野内；黄色为选中点";
    }
    public void Dispose() { if(root!=null) { root.SetActive(false);Object.Destroy(root);root=null; }camera=null;retry=0;dots.Clear();lines.Clear();badges.Clear();labels.Clear();Status=""; }
}
