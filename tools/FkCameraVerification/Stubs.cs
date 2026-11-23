namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static void Destroy(Object obj) { obj.destroyed = true; if (obj is GameObject go) go.transform.destroyed = true; }
        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a,b) || ((ReferenceEquals(a,null) || a.destroyed) && (ReferenceEquals(b,null) || b.destroyed));
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject : Object
    {
        public string name;
        public Transform transform;
        public static readonly List<GameObject> Created = new();
        public GameObject(string name) { this.name=name; transform=new Transform(this); Created.Add(this); }
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 eulerAngles { get => new(0,rotation.yaw,0); set => rotation=Quaternion.Euler(value.x,value.y,value.z); }
        public Transform(GameObject obj) { gameObject=obj; }
    }
    public class Camera : Object { public Transform transform = new GameObject("Camera").transform; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public static Vector3 forward => new(0,0,1);
        public static Vector3 right => new(1,0,0);
        public static Vector3 up => new(0,1,0);
        public Vector3 normalized { get { float n=MathF.Sqrt(x*x+y*y+z*z); return n>0?this*(1/n):zero; } }
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float f) => new(a.x*f,a.y*f,a.z*f);
        public override string ToString() => $"({x},{y},{z})";
    }
    public struct Quaternion
    {
        internal float yaw;
        public static Quaternion Euler(float x,float y,float z) => new() { yaw=y };
        public static Vector3 operator *(Quaternion q,Vector3 v) { float r=q.yaw*MathF.PI/180; return new(v.x*MathF.Cos(r)+v.z*MathF.Sin(r),v.y,-v.x*MathF.Sin(r)+v.z*MathF.Cos(r)); }
    }
    public enum KeyCode { UpArrow,DownArrow,LeftArrow,RightArrow,PageUp,PageDown,W,A,S,D }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Keys = new();
        public static bool GetKey(KeyCode key) => Keys.Contains(key);
    }
    public static class Time { public static float deltaTime=0.2f; }
}
namespace Il2Cpp
{
    public class CameraSystem
    {
        public static CameraSystem Instance = new();
        public UnityEngine.Camera MainCamera = new();
        public UnityEngine.Transform? FollowTarget;
        public readonly List<UnityEngine.GameObject> Calls = new();
        public void SetCameraFollowUnit(UnityEngine.GameObject target) { Calls.Add(target); FollowTarget=target.transform; }
    }
}
namespace mod
{
    public class TipMsgCore
    {
        public static TipMsgCore i = new();
        public UnityEngine.GameObject mainplayer = new("Player");
        public readonly List<string> Messages = new();
        public void Send(string text) => Messages.Add(text);
    }
}
namespace mod.tool
{
    public class CustomCameraController
    {
        public static CustomCameraController Instance = new();
        public bool IsActive;
        public UnityEngine.Transform? Target;
        public void SetTarget(UnityEngine.Transform? target) => Target=target;
    }
}
namespace HomeEditorModule.Core
{
    public static class CenterTMPManager { public static void SetText(string text) { } }
}
