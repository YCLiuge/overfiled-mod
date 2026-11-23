using LocalHomeModule;
using UnityEngine;
using UnityEngine.UI;

int checks=0;
void Check(bool condition,string description) { checks++; if(!condition) throw new Exception(description); }
GameObject Root()=>GameObject.All.Last(o=>o.name=="LocalHome_MechanismPositionOverlay");
Image[] Orbs()=>Root().transform.children.Where(t=>t.gameObject.name=="LightBall").Select(t=>t.gameObject.GetComponent<Image>()).ToArray();
Image[] Lines()=>Root().transform.children.Where(t=>t.gameObject.name=="RangeSegment").Select(t=>t.gameObject.GetComponent<Image>()).ToArray();
Camera camera=new(); Camera.main=camera; Resources.Cameras.Add(camera);
var marker=new MechanismPositionMarker();
marker.Update(new(0,0,10),false,0);
Check(Root().GetComponent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"overlay canvas");
Check(Root().GetComponent<Canvas>().sortingOrder==22190,"canvas sort order");
Check(Orbs().Length==25 && Lines().Length==192,"bounded generated objects");
Check(Orbs().All(i=>!i.raycastTarget) && Lines().All(i=>!i.raycastTarget),"input transparent");
Check(Orbs().All(i=>i.gameObject.activeSelf),"front-facing orb visible");
Check(Orbs()[12].GetComponent<RectTransform>().anchoredPosition.x==0 && Orbs()[12].GetComponent<RectTransform>().anchoredPosition.y==0,"camera center projects to overlay center");
Check(Lines().All(i=>i.gameObject.activeSelf),"front range visible");
var smallLine=Lines()[0].GetComponent<RectTransform>().anchoredPosition.x;
marker.Update(new(0,0,10),true,2);
Check(MathF.Abs(Lines()[0].GetComponent<RectTransform>().anchoredPosition.x)>MathF.Abs(smallLine)*7,"trigger radius changes world-projected extent");
Check(Orbs()[0].color.r==1 && Orbs()[0].color.g==.65f,"trigger orange color");
marker.Update(new(0,0,-10),false,0);
Check(Orbs().All(i=>!i.gameObject.activeSelf) && Lines().All(i=>!i.gameObject.activeSelf),"behind camera hides whole small marker");
marker.Update(new(10000,0,10),false,0);
Check(Orbs().All(i=>!i.gameObject.activeSelf),"offscreen center hidden");
marker.Update(new(0,0,10),true,float.NaN);
Check(Lines().All(i=>i.gameObject.activeSelf && float.IsFinite(i.GetComponent<RectTransform>().sizeDelta.x)),"invalid radius has finite fallback");
Image[] Axes()=>Root().transform.children.Where(t=>t.gameObject.name=="RotationAxis").Select(t=>t.gameObject.GetComponent<Image>()).ToArray();
marker.Update(new(0,0,10),false,0,true);
Check(Axes().Length==3 && Axes().All(i=>i.gameObject.activeSelf && !i.raycastTarget),"axis markers visible and input transparent");
float worldXLength=Axes()[0].GetComponent<RectTransform>().sizeDelta.x;
marker.Update(new(0,0,10),false,0,true,new(0,90,0));
Check(Axes()[0].GetComponent<RectTransform>().sizeDelta.x<worldXLength/2,"local reference rotates projected axis direction");
marker.Update(new(0,0,10),false,0);
Check(Axes().All(i=>!i.gameObject.activeSelf),"axis markers hidden outside rotation editor");
int objects=GameObject.All.Count;
for(int i=0;i<1000;i++) marker.Update(new(0,0,10),false,0);
Check(GameObject.All.Count==objects,"updates do not allocate more game objects");
marker.Hide(); Check(!Root().activeSelf && marker.Status=="","hide clears visibility and status");
marker.Update(new(0,0,10),false,0); Check(Root().activeSelf,"update reactivates hidden marker");
camera.isActiveAndEnabled=false; marker.Update(new(0,0,10),false,0);
Check(!Root().activeSelf && marker.Status.Contains("等待"),"disabled camera hides marker");
camera.isActiveAndEnabled=true; marker.Update(new(0,0,10),false,0);
var owned=GameObject.All.ToArray(); marker.Dispose(); marker.Dispose();
Check(owned.All(o=>o.destroyed),"dispose destroys overlay and descendants and is idempotent");
Check(marker.Status=="","dispose resets status");

// Regression: changing a cached camera must invalidate the overlay immediately,
// including inside the one second camera search throttle.
Time.unscaledTime=10; camera.targetTexture=null;
var probe=new MechanismPositionMarker(); probe.Update(new(0,0,10),false,0);
camera.targetTexture=new object(); Time.unscaledTime=10.1f;
probe.Update(new(0,0,10),false,0);
Check(!Root().activeSelf,"RenderTexture camera rejected before retry interval");
camera.targetTexture=null; camera.targetDisplay=1;
probe.Update(new(0,0,10),false,0);
Check(!Root().activeSelf,"camera on other display rejected");
camera.targetDisplay=0; camera.pixelRect=new(0,0,10,10);
probe.Update(new(0,0,10),false,0);
Check(!Root().activeSelf,"tiny UI camera rejected");
camera.pixelRect=new(0,0,400,800); camera.Projection=p=>new(450,400,p.z);
probe.Update(new(0,0,10),false,0);
Check(Orbs().All(i=>!i.gameObject.activeSelf),"orb outside camera viewport hidden even inside screen");
probe.Dispose();
// Fallback selection must prefer scene perspective over a deeper UI camera.
Time.unscaledTime=20; Camera.main=null; Resources.Cameras.Clear();
var ui=new Camera{orthographic=true,depth=100,Projection=p=>new(700,400,p.z)};
var scene=new Camera{Projection=p=>new(500,400,p.z)};
Resources.Cameras.Add(ui); Resources.Cameras.Add(scene);
probe=new MechanismPositionMarker(); probe.Update(new(0,0,10),false,0);
Check(Orbs()[12].GetComponent<RectTransform>().anchoredPosition.x==0,"fallback prefers perspective over UI camera");
probe.Dispose();
// A UI camera may be the only camera during loading. Re-evaluate even while it
// stays enabled when the actual main scene camera becomes available later.
Time.unscaledTime=30; Camera.main=null; Resources.Cameras.Clear(); Resources.Cameras.Add(ui);
probe=new MechanismPositionMarker(); probe.Update(new(0,0,10),false,0);
Check(Orbs()[12].GetComponent<RectTransform>().anchoredPosition.x==200,"temporary UI camera selected while loading");
Camera.main=scene; Resources.Cameras.Add(scene); Time.unscaledTime=31.1f;
probe.Update(new(0,0,10),false,0);
Check(Orbs()[12].GetComponent<RectTransform>().anchoredPosition.x==0,"late main scene camera replaces cached UI camera");
probe.Dispose();
TrackMarkerChecks.Run(Check);
Console.WriteLine($"MarkerVerification: {checks} checks passed. Stub tests cover control flow/projection inputs/object ownership; actual Unity canvas layout and renderer require in-game testing.");
