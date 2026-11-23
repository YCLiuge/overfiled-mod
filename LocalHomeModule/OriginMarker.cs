using UnityEngine;
using Object=UnityEngine.Object;
namespace LocalHomeModule;

// Render-only axes: no colliders, game furniture IDs or network objects.
internal sealed class OriginMarker : IDisposable
{
    private GameObject? root;
    private Material? material;
    private bool unavailable;
    internal void Update(bool visible,EditorOrigin origin)
    {
        if(!visible) { if(root!=null) root.SetActive(false); return; }
        if(root==null && !unavailable)
        {
            var shader=Shader.Find("Sprites/Default");
            if(shader==null) { unavailable=true; return; }
            material=new Material(shader);
            root=new GameObject("LocalHome_EditorOrigin");
            Object.DontDestroyOnLoad(root);
            var colors=new[]{Color.red,Color.green,Color.blue};
            var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};
            for(int i=0;i<3;i++)
            {
                var child=new GameObject("Axis"+i); child.transform.SetParent(root.transform,false);
                var line=child.AddComponent<LineRenderer>();
                line.sharedMaterial=material; line.useWorldSpace=false;
                line.positionCount=2; line.SetPosition(0,-axes[i]*.2f); line.SetPosition(1,axes[i]*.8f);
                line.startWidth=.025f; line.endWidth=.025f; line.startColor=colors[i]; line.endColor=colors[i];
            }
        }
        if(root==null) return;
        root.SetActive(true);
        root.transform.position=new Vector3(origin.X,origin.Y,origin.Z);
        root.transform.rotation=Quaternion.Euler(origin.Pitch,origin.Yaw,origin.Roll);
    }
    public void Dispose()
    {
        if(root!=null) { root.SetActive(false); Object.Destroy(root); root=null; }
        if(material!=null) { Object.Destroy(material); material=null; }
    }
}
