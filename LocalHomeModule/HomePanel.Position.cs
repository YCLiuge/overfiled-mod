using System.Globalization;
using mod;
using UnityEngine;
using UnityEngine.UI;
namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private RectTransform? WindowAt(Vector2 mouse)
    {
        RectTransform? result=null;
        foreach(var candidate in new[]{window,patternWindow,mechanismWindow,rotationWindow,trackWindow,eventWindow})
            if(candidate!=null && candidate.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(candidate,mouse,null) &&
               (result==null || candidate.GetSiblingIndex()>result.GetSiblingIndex())) result=candidate;
        return result;
    }
    private InputField[] ActiveMechanismPosition => triggerTab?triggerPosition:mechanismPosition;
    private void NudgeMechanismPosition(int axis,float sign)
    {
        {
            float amount=EditorNumber(mechanismPositionStep);
            if(amount<=0 || amount>100000) throw new ArgumentException("坐标步长须大于0且不超过100000");
            var p=ReadMechanismPoint(ActiveMechanismPosition);
            var v=new[]{p.X,p.Y,p.Z}; v[axis]+=amount*sign;
            if(v.Any(f=>!float.IsFinite(f)||Math.Abs(f)>100000)) throw new ArgumentException("坐标须在±100000范围内");
            WriteMechanismPoint(ActiveMechanismPosition,new(v[0],v[1],v[2]));
        }
    }
    private void UsePlayerMechanismPosition()
    {
        var transform=TipMsgCore.i.mainplayer?.transform ?? throw new InvalidOperationException("角色尚未就绪");
        var pos=transform.position; WriteMechanismPoint(ActiveMechanismPosition,new(pos.x,pos.y,pos.z));
    }
    private readonly TrackPathMarker trackMarker=new();
    private void UpdateMechanismMarker()
    {
        if(mechanismOpen && !triggerTab && mechanismKind=="track" && mechanismId.Length>0 && runtime.Enabled)
            trackMarker.Update(TrackMarkerPoints(),trackSelected,font!);
        else trackMarker.Hide();
        bool selected=triggerTab?triggerId.Length>0:mechanismId.Length>0;
        if(!mechanismOpen || !selected || !runtime.Enabled) { mechanismMarker.Hide(); return; }
        var fields=ActiveMechanismPosition;
        if(fields.Length!=3) { mechanismMarker.Hide(); return; }
        var v=new float[3];
        for(int i=0;i<3;i++)
            if(!float.TryParse(fields[i].text,NumberStyles.Float,CultureInfo.InvariantCulture,out v[i]) || !float.IsFinite(v[i]) || Math.Abs(v[i])>100000)
            { mechanismMarker.Hide(); return; }
        float radius=0;
        if(triggerTab) float.TryParse(triggerRadius.text,NumberStyles.Float,CultureInfo.InvariantCulture,out radius);
        mechanismMarker.Update(new(v[0],v[1],v[2]),triggerTab,radius,!triggerTab && mechanismKind=="rotate",RotationMarkerReference);
    }
}
