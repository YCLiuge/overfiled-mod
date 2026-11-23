using UnityEngine;
using UnityEngine.UI;
namespace LocalHomeModule;

internal sealed partial class HomePanel
{
    private void CopyVector(InputField[] values) => EditorAction(()=>
    {
        var point=ReadMechanismPoint(values);
        string text=VectorText.Format(point);
        VectorText.Parse(text);
        GUIUtility.systemCopyBuffer=text;
    },"已复制 XYZ 向量");
    private void PasteVector(InputField[] values) => EditorAction(()=>
    {
        var point=VectorText.Parse(GUIUtility.systemCopyBuffer);
        // Parse the complete triple before changing any field. Preserve numeric precision.
        values[0].text=point.X.ToString("G9",System.Globalization.CultureInfo.InvariantCulture);
        values[1].text=point.Y.ToString("G9",System.Globalization.CultureInfo.InvariantCulture);
        values[2].text=point.Z.ToString("G9",System.Globalization.CultureInfo.InvariantCulture);
    },"已粘贴 XYZ 草稿；应用或保存后生效");
}
