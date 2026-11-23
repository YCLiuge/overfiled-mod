namespace LocalHomeModule { internal record MechanismPoint(float X,float Y,float Z); }
namespace UnityEngine
{
    public class Object
    {
        public bool destroyed;
        public static void DontDestroyOnLoad(Object obj) { }
        public static void Destroy(Object obj) { obj.destroyed=true; if(obj is GameObject go) foreach(var c in go.transform.children.ToArray()) Destroy(c.gameObject); }
    }
    public class Component:Object
    {
        public GameObject gameObject=null!;
        public Transform transform=>gameObject.transform;
        public T GetComponent<T>() where T:Component => gameObject.GetComponent<T>();
    }
    public class Transform:Component
    {
        public List<Transform> children=new();
        public void SetParent(Transform parent,bool world) { parent.children.Add(this); }
        public void SetAsFirstSibling() { }
    }
    public class RectTransform:Transform
    {
        public Vector2 anchorMin,anchorMax,pivot,anchoredPosition,sizeDelta;
        public Quaternion localRotation;
    }
    public class GameObject:Object
    {
        public static List<GameObject> All=new();
        public string name; public bool activeSelf=true;
        public Transform transform;
        readonly List<Component> components=new();
        public GameObject(string name) { this.name=name; transform=new RectTransform{gameObject=this}; components.Add(transform); All.Add(this); }
        public T AddComponent<T>() where T:Component,new()
        {
            if(typeof(T)==typeof(RectTransform)) return (T)(Component)transform;
            var component=new T{gameObject=this}; components.Add(component); return component;
        }
        public T GetComponent<T>() where T:Component => components.OfType<T>().First();
        public void SetActive(bool value) { activeSelf=value; }
    }
    public class Font:Object { }
    public enum TextAnchor { MiddleCenter }
    public enum RenderMode { ScreenSpaceOverlay }
    public class Canvas:Component { public RenderMode renderMode; public int sortingOrder; }
    public class Camera:Component
    {
        public static Camera? main;
        public bool isActiveAndEnabled=true;
        public object? targetTexture;
        public float depth;
        public int targetDisplay;
        public bool orthographic;
        public Rect pixelRect=new(0,0,1000,800);
        public Func<Vector3,Vector3> Projection=p=>new(Screen.width/2f+p.x*100/p.z,Screen.height/2f+p.y*100/p.z,p.z);
        public Vector3 WorldToScreenPoint(Vector3 p)=>Projection(p);
    }
    public static class Resources
    {
        public static List<Camera> Cameras=new();
        public static T[] FindObjectsOfTypeAll<T>() => Cameras.OfType<T>().ToArray();
    }
    public static class Time { public static float unscaledTime; }
    public static class Screen { public static int width=1000,height=800; }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height) { this.x=x; this.y=y; this.width=width; this.height=height; }
        public float xMin=>x; public float xMax=>x+width;
        public float yMin=>y; public float yMax=>y+height;
    }
    public struct Vector2
    {
        public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; }
        public float magnitude=>MathF.Sqrt(x*x+y*y);
        public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
    }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
    }
    public struct Quaternion { public static Quaternion Euler(float x,float y,float z)=>new(); }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white=>new(1,1,1,1);
        public static Color Lerp(Color a,Color b,float f)=>new(a.r+(b.r-a.r)*f,a.g+(b.g-a.g)*f,a.b+(b.b-a.b)*f,a.a+(b.a-a.a)*f);
    }
    public static class Mathf
    {
        public const float PI=MathF.PI,Rad2Deg=180/MathF.PI;
        public static float Max(float a,float b)=>MathF.Max(a,b);
        public static float Abs(float a)=>MathF.Abs(a);
        public static float Sqrt(float a)=>MathF.Sqrt(a);
        public static float Cos(float a)=>MathF.Cos(a);
        public static float Sin(float a)=>MathF.Sin(a);
        public static float Atan2(float a,float b)=>MathF.Atan2(a,b);
    }
}
namespace UnityEngine.UI
{
    public class Text:UnityEngine.Component { public UnityEngine.Font font=null!;public int fontSize;public UnityEngine.TextAnchor alignment;public bool raycastTarget,supportRichText;public string text="";public UnityEngine.Color color; }
    public class Image:UnityEngine.Component { public bool raycastTarget=true; public UnityEngine.Color color; }
}
