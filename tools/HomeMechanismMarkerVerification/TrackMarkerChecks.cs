using LocalHomeModule;
using UnityEngine;
using UnityEngine.UI;
static class TrackMarkerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Time.unscaledTime=100;Camera.main=new Camera();Resources.Cameras.Clear();Resources.Cameras.Add(Camera.main);
        var marker=new TrackPathMarker();var font=new Font();var points=new[]{new MechanismPoint(0,0,10),new MechanismPoint(1,0,10),new MechanismPoint(2,0,10)};
        GameObject Root()=>GameObject.All.Last(g=>g.name=="LocalHome_TrackPathOverlay");
        Image[] Images(string name)=>Root().transform.children.Where(t=>t.gameObject.name==name).Select(t=>t.gameObject.GetComponent<Image>()).ToArray();
        marker.Update(points,1,font);
        check(Images("TrackPoint").Count(i=>i.gameObject.activeSelf)==3,"all track points rendered");
        check(Images("TrackSegment").Count(i=>i.gameObject.activeSelf)==2,"consecutive track segments rendered");
        check(Images("TrackPoint")[1].GetComponent<RectTransform>().sizeDelta.x==15,"selected track point highlighted");
        check(Images("TrackPoint").All(i=>!i.raycastTarget)&&Images("TrackSegment").All(i=>!i.raycastTarget),"track overlay input transparent");
        check(Root().transform.children.Where(t=>t.gameObject.name=="TrackNumberBackground").Select(t=>t.children.Single().gameObject.GetComponent<Text>().text).SequenceEqual(new[]{"1","2 *","3"}),"point numbers match list order");
        int objects=GameObject.All.Count;for(int i=0;i<50;i++)marker.Update(points,0,font);check(objects==GameObject.All.Count,"repeated track draws reuse objects");
        marker.Update(points.Take(1).ToArray(),0,font);check(Images("TrackPoint").Count(i=>i.gameObject.activeSelf)==1&&Images("TrackSegment").All(i=>!i.gameObject.activeSelf),"shrinking hides unused pooled markers");
        marker.Update(new[]{new MechanismPoint(0,0,-1),new MechanismPoint(0,0,10)},0,font);check(!Images("TrackPoint")[0].gameObject.activeSelf&&Images("TrackSegment").All(i=>!i.gameObject.activeSelf),"behind camera point and crossing line hidden");
        marker.Update(Array.Empty<MechanismPoint>(),0,font);check(!Root().activeSelf,"empty path hides overlay");
        marker.Update(points,0,font);marker.Hide();check(!Root().activeSelf,"explicit hide");marker.Dispose();check(Root().destroyed,"track overlay disposed");
        Time.unscaledTime=200;Camera.main=null;Resources.Cameras.Clear();marker=new();marker.Update(points,0,font);check(marker.Status.Contains("等待"),"missing camera handled");marker.Dispose();
    }
}
