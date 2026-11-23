using System.Text.Json;
namespace LocalHomeModule;

internal sealed partial class HomeRuntime
{
    private MechanismDefinition[]? rotationPreviewNodes;
    private Placement[]? rotationPreviewItems;
    private string rotationPreviewKey="",rotationPreviewId="";
    private MechanismConfig? rotationPreviewConfig;
    private float rotationPreviewProgress;
    internal bool RotationPreviewActive => rotationPreviewNodes!=null;
    internal void SetRotationPreview(MechanismDefinition node,float progress)
    {
        RequireMechanisms();
        if(node.Kind!="rotate" || !float.IsFinite(progress) || progress<0 || progress>1)
            throw new ArgumentException("旋转预览参数无效");
        var items=Items;
        string key=JsonSerializer.Serialize(node);
        if(rotationPreviewKey!=key || rotationPreviewConfig!=mechanismConfig || rotationPreviewItems==null || !rotationPreviewItems.SequenceEqual(items))
        {
            var defs=mechanismConfig.Nodes.Where(d=>d.Id!=node.Id).Append(node).ToArray();
            var validated=MechanismEngine.Validate(defs,items);
            rotationPreviewConfig=mechanismConfig;
            rotationPreviewNodes=validated; rotationPreviewItems=items.ToArray();
            rotationPreviewKey=key;
        }
        rotationPreviewId=node.Id; rotationPreviewProgress=progress;
    }
    internal void ClearRotationPreview()
    {
        rotationPreviewNodes=null; rotationPreviewItems=null; rotationPreviewConfig=null;
        rotationPreviewKey=rotationPreviewId=""; rotationPreviewProgress=0;
    }
    private Placement[]? RotationPreviewFrame() => rotationPreviewNodes==null || rotationPreviewItems==null?null:
        MechanismEngine.Preview(rotationPreviewNodes,rotationPreviewItems,rotationPreviewId,rotationPreviewProgress);
}
