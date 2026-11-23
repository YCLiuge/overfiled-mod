using System.Globalization;
namespace LocalHomeModule;

public static class VectorText
{
    public static MechanismPoint Parse(string text)
    {
        if(string.IsNullOrWhiteSpace(text) || text.Length>512) throw new ArgumentException("请粘贴三个数值，例如 1.5, 2, 3");
        string normalized=text.Trim();
        if((normalized.StartsWith("(") && normalized.EndsWith(")")) || (normalized.StartsWith("[") && normalized.EndsWith("]"))) normalized=normalized[1..^1];
        var parts=normalized.Split(new[]{',','，',';','；',' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(parts.Length!=3) throw new ArgumentException("向量必须恰好包含 X、Y、Z 三个数值");
        var values=new float[3];
        for(int i=0;i<3;i++)
            if(!float.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]) || !float.IsFinite(values[i]) || Math.Abs(values[i])>100000)
                throw new ArgumentException("向量数值须有限且在±100000范围内，小数点使用 .");
        return new(values[0],values[1],values[2]);
    }
    public static string Format(MechanismPoint p) => string.Join(", ",new[]{p.X,p.Y,p.Z}.Select(v=>v.ToString("G9",CultureInfo.InvariantCulture)));
}
