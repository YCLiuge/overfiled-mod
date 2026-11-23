using HomeEditorModule.Core;
using Il2Cpp;
using mod;
using mod.tool;
using UnityEngine;

int passed=0;
void Check(bool ok,string name) { if(!ok) throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
void Near(Vector3 actual,Vector3 expected,string name) => Check(MathF.Abs(actual.x-expected.x)<0.0001f && MathF.Abs(actual.y-expected.y)<0.0001f && MathF.Abs(actual.z-expected.z)<0.0001f,name+" "+actual);
void Reset()
{
    FkCamera.Stop(); Input.Keys.Clear(); CameraSystem.Instance=new(); TipMsgCore.i=new(); CustomCameraController.Instance=new();
    CameraSystem.Instance.FollowTarget=TipMsgCore.i.mainplayer.transform;
    TipMsgCore.i.mainplayer.transform.position=new(2,3,4);
}
Reset();
var system=CameraSystem.Instance;
var original=new GameObject("OriginalTarget"); original.transform.position=new(10,20,30); original.transform.rotation=Quaternion.Euler(0,42,0);
system.FollowTarget=original.transform;
FkCamera.SetEnabled(true);
var anchor=system.FollowTarget!.gameObject;
Check(FkCamera.Active && anchor!=original,"activation creates distinct follow anchor");
Near(anchor.transform.position,original.transform.position,"anchor starts at original target");
Check(anchor.transform.rotation.yaw==42,"anchor starts at original target rotation");
FkCamera.SetEnabled(true);
Check(system.Calls.Count==1 && system.FollowTarget==anchor.transform,"duplicate enable keeps original capture");
FkCamera.SetEnabled(false);
Check(!FkCamera.Active && system.FollowTarget==original.transform && anchor==null,"disable restores original and destroys anchor");
int calls=system.Calls.Count;
FkCamera.Stop(); Check(system.Calls.Count==calls,"duplicate stop is idempotent");
for(int n=0;n<3;n++) { FkCamera.SetEnabled(true); FkCamera.Stop(); }
Check(system.FollowTarget==original.transform && !FkCamera.Active,"repeated cycles restore same target");

Reset(); system=CameraSystem.Instance;
FkCamera.SetEnabled(true); anchor=system.FollowTarget!.gameObject;
var other=new GameObject("OtherCameraOwner"); system.SetCameraFollowUnit(other); calls=system.Calls.Count;
FkCamera.Tick();
Check(!FkCamera.Active && system.FollowTarget==other.transform && system.Calls.Count==calls && anchor==null,"external follow takeover preserved");

Reset(); system=CameraSystem.Instance;
FkCamera.SetEnabled(true); anchor=system.FollowTarget!.gameObject;
var playerPos=TipMsgCore.i.mainplayer.transform.position;
Input.Keys.Add(KeyCode.UpArrow); FkCamera.Tick();
Near(anchor.transform.position,playerPos+Vector3.forward,"up arrow moves anchor forward");
Near(TipMsgCore.i.mainplayer.transform.position,playerPos,"up arrow leaves player unchanged");
Input.Keys.Clear(); Input.Keys.Add(KeyCode.PageUp); FkCamera.Tick();
Near(anchor.transform.position,playerPos+Vector3.forward+Vector3.up,"PageUp moves anchor vertically");
Input.Keys.Clear(); Input.Keys.Add(KeyCode.PageDown); FkCamera.Tick();
Near(anchor.transform.position,playerPos+Vector3.forward,"PageDown reverses vertical movement");
Input.Keys.Clear(); Input.Keys.Add(KeyCode.W); Input.Keys.Add(KeyCode.A); Input.Keys.Add(KeyCode.S); Input.Keys.Add(KeyCode.D);
var before=anchor.transform.position; FkCamera.Tick();
Near(anchor.transform.position,before,"WASD do not move anchor");
Near(TipMsgCore.i.mainplayer.transform.position,playerPos,"camera tick never writes player");
Input.Keys.Clear(); Input.Keys.Add(KeyCode.RightArrow); system.MainCamera.transform.eulerAngles=new(0,90,0); FkCamera.Tick();
Near(anchor.transform.position,before-Vector3.forward,"arrow movement follows camera yaw");
Input.Keys.Clear(); Input.Keys.Add(KeyCode.UpArrow); Input.Keys.Add(KeyCode.RightArrow); Input.Keys.Add(KeyCode.PageUp);
before=anchor.transform.position; FkCamera.Tick(); var diff=anchor.transform.position-before;
Check(MathF.Abs(MathF.Sqrt(diff.x*diff.x+diff.y*diff.y+diff.z*diff.z)-1f)<0.0001f,"diagonal movement uses constant speed");

Reset(); system=CameraSystem.Instance; original=new GameObject("TransientOriginal"); system.FollowTarget=original.transform;
FkCamera.SetEnabled(true); UnityEngine.Object.Destroy(original); FkCamera.Stop();
Check(system.FollowTarget==TipMsgCore.i.mainplayer.transform,"destroyed original falls back to player");
Reset(); system=CameraSystem.Instance; system.FollowTarget=null; FkCamera.SetEnabled(true);
Near(system.FollowTarget!.position,TipMsgCore.i.mainplayer.transform.position,"null original starts at player");
FkCamera.Stop(); Check(system.FollowTarget==TipMsgCore.i.mainplayer.transform,"null original restores player fallback");
Reset(); system=CameraSystem.Instance; CustomCameraController.Instance.IsActive=true; FkCamera.SetEnabled(true);
Check(!FkCamera.Active && system.Calls.Count==0,"custom camera blocks activation");
Reset(); system=CameraSystem.Instance;
FkCamera.SetEnabled(true); anchor=system.FollowTarget!.gameObject;
CustomCameraController.Instance.Target=anchor.transform; CustomCameraController.Instance.IsActive=true;
FkCamera.Tick();
Check(!FkCamera.Active && CustomCameraController.Instance.Target==TipMsgCore.i.mainplayer.transform && system.FollowTarget==TipMsgCore.i.mainplayer.transform && anchor==null,"F5 takeover releases anchor and retargets custom camera before deletion");
Reset(); system=CameraSystem.Instance;
FkCamera.SetEnabled(true); other=new GameObject("CustomIndependentTarget");
CustomCameraController.Instance.Target=other.transform; CustomCameraController.Instance.IsActive=true;
FkCamera.Tick();
Check(CustomCameraController.Instance.Target==other.transform,"custom camera independent target preserved");
Reset(); system=CameraSystem.Instance;
FkCamera.SetEnabled(true); anchor=system.FollowTarget!.gameObject;
other=new GameObject("ExternalFollow"); system.SetCameraFollowUnit(other);
CustomCameraController.Instance.Target=anchor.transform; CustomCameraController.Instance.IsActive=true; calls=system.Calls.Count;
FkCamera.Tick();
Check(system.FollowTarget==other.transform && system.Calls.Count==calls && CustomCameraController.Instance.Target==TipMsgCore.i.mainplayer.transform,"external owner kept while dangling custom target repaired");
FkCamera.Stop(); Console.WriteLine($"All {passed} checks passed.");
