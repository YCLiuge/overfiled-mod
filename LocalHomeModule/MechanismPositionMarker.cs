using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace LocalHomeModule;

// World coordinates projected to an input-transparent overlay. Independent of scene
// shader stripping, camera culling masks and furniture depth; no game objects spawned.
internal sealed class MechanismPositionMarker : IDisposable
{
    private GameObject? root;
    private Camera? camera;
    private readonly List<Image> segments=new(),orb=new(),axes=new();
    private float cameraRetry;
    internal string Status { get; private set; }="";
    internal void Hide() { if(root!=null) root.SetActive(false); Status=""; }
    private static Image Image(Transform parent,string name)
    {
        var obj=new GameObject(name); obj.transform.SetParent(parent,false);
        var rect=obj.AddComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        var image=obj.AddComponent<Image>(); image.raycastTarget=false; return image;
    }
    private void Build()
    {
        root=new GameObject("LocalHome_MechanismPositionOverlay"); Object.DontDestroyOnLoad(root);
        var canvas=root.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=22190;
        for(int i=0;i<3*64;i++) segments.Add(Image(root.transform,"RangeSegment"));
        for(int i=0;i<25;i++) orb.Add(Image(root.transform,"LightBall"));
        for(int i=0;i<3;i++) axes.Add(Image(root.transform,"RotationAxis"));
    }
    private static Vector2 ScreenPoint(Vector3 p) => new(p.x-Screen.width*.5f,p.y-Screen.height*.5f);
    private void Line(Image image,Vector3 a,Vector3 b,Color color)
    {
        if(a.z<=.01f || b.z<=.01f || !float.IsFinite(a.x) || !float.IsFinite(b.x) || !float.IsFinite(a.y) || !float.IsFinite(b.y))
        { image.gameObject.SetActive(false); return; }
        var pa=ScreenPoint(a); var pb=ScreenPoint(b); var delta=pb-pa;
        // Avoid enormous offscreen rects around the near plane.
        if(delta.magnitude>Mathf.Max(Screen.width,Screen.height)*8) { image.gameObject.SetActive(false); return; }
        image.gameObject.SetActive(true); image.color=color;
        var rect=image.GetComponent<RectTransform>(); rect.anchoredPosition=(pa+pb)*.5f;
        rect.sizeDelta=new Vector2(Mathf.Max(1,delta.magnitude),2);
        rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
    }
    private static bool SceneCamera(Camera? candidate) => candidate!=null && candidate.isActiveAndEnabled &&
        candidate.targetTexture==null && candidate.targetDisplay==0 && candidate.pixelRect.width>32 && candidate.pixelRect.height>32;
    internal void Update(MechanismPoint position,bool trigger,float radius,bool showAxes=false,MechanismPoint? axisReference=null)
    {
        if(!SceneCamera(camera) || Time.unscaledTime>=cameraRetry)
        {
            if(Time.unscaledTime>=cameraRetry)
            {
                cameraRetry=Time.unscaledTime+1;
                camera=Camera.main;
                if(!SceneCamera(camera) || camera!.orthographic)
                    camera=Resources.FindObjectsOfTypeAll<Camera>().Where(c=>SceneCamera(c)).OrderBy(c=>c.orthographic).ThenByDescending(c=>c.pixelRect.width*c.pixelRect.height).ThenBy(c=>c.depth).FirstOrDefault();
            }
        }
        if(!SceneCamera(camera)) { Hide(); Status="位置标记：等待场景相机"; return; }
        if(root==null) Build(); root!.SetActive(true);
        var center=new Vector3(position.X,position.Y,position.Z);
        var point=camera!.WorldToScreenPoint(center);
        var viewport=camera.pixelRect;
        bool visible=point.z>.01f && point.x>=viewport.xMin && point.x<=viewport.xMax && point.y>=viewport.yMin && point.y<=viewport.yMax;
        Status=visible?"位置标记："+(trigger?"橙色触发球 / 范围":"青色机关球"):"位置标记在视野外，可点取角色坐标";
        var color=trigger?new Color(1,.65f,.08f,1):new Color(.1f,1,1,1);
        // A filled disc with a bright center reads as a light ball without any scene shader.
        for(int i=0;i<orb.Count;i++)
        {
            var image=orb[i]; image.gameObject.SetActive(visible); if(!visible) continue;
            float y=i-12, half=Mathf.Sqrt(Mathf.Max(0,144-y*y));
            var rect=image.GetComponent<RectTransform>(); rect.anchoredPosition=ScreenPoint(point)+new Vector2(0,y);
            rect.sizeDelta=new Vector2(Mathf.Max(1,half*2),1.2f);
            image.color=Color.Lerp(Color.white,color,Mathf.Abs(y)/12f);
        }
        var reference=axisReference??new MechanismPoint(0,0,0);
        float radians=MathF.PI/180;
        var q=System.Numerics.Quaternion.CreateFromYawPitchRoll(reference.Y*radians,reference.X*radians,reference.Z*radians);
        var colors=new[]{new Color(1,.2f,.2f,1),new Color(.2f,1,.2f,1),new Color(.25f,.55f,1,1)};
        for(int i=0;i<3;i++)
        {
            axes[i].gameObject.SetActive(showAxes); if(!showAxes) continue;
            var direction=System.Numerics.Vector3.Transform(i==0?System.Numerics.Vector3.UnitX:i==1?System.Numerics.Vector3.UnitY:System.Numerics.Vector3.UnitZ,q);
            var end=center+new Vector3(direction.X,direction.Y,direction.Z);
            Line(axes[i],point,camera.WorldToScreenPoint(end),colors[i]);
        }
        float r=trigger && float.IsFinite(radius) && radius>0 && radius<=100000?radius:.25f;
        for(int axis=0;axis<3;axis++)
            for(int i=0;i<64;i++)
            {
                Vector3 Ring(float angle)
                {
                    float a=Mathf.Cos(angle)*r,b=Mathf.Sin(angle)*r;
                    return center+(axis==0?new Vector3(a,b,0):axis==1?new Vector3(a,0,b):new Vector3(0,a,b));
                }
                var a=camera.WorldToScreenPoint(Ring(i*Mathf.PI*2/64));
                var b=camera.WorldToScreenPoint(Ring((i+1)*Mathf.PI*2/64));
                Line(segments[axis*64+i],a,b,color);
            }
    }
    public void Dispose()
    {
        if(root!=null) { root.SetActive(false); Object.Destroy(root); root=null; }
        segments.Clear(); orb.Clear(); axes.Clear(); camera=null; Status="";
    }
}
