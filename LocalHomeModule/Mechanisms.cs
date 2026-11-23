using System.Numerics;

namespace LocalHomeModule;

public record MechanismPoint(float X=0,float Y=0,float Z=0);
public record MechanismDefinition(string Id,string Name,string Kind,MechanismPoint Position,string? ParentId,int[] FurnitureIds,MechanismPoint[] Track,float Speed=1,float Duration=5,float Delay=0,string Mode="once",float RotationX=0,float RotationY=90,float RotationZ=0,float Scale=2,MechanismPoint? Gravity=null,MechanismPoint? Velocity=null,string RotationSpace="world",MechanismPoint? RotationReference=null,string RotationStyle="group",string RotationInput="speed",string[]? OnCompleteTargets=null,string[]? OnCycleTargets=null);

/// <summary>Deterministic baseline-only animation. Euler angles follow Unity Z, X, Y order.</summary>
public static class MechanismEngine
{
    private const double Limit=100000;
    private const double MaxSeconds=86400;
    private static bool Bounded(float n,double max=Limit) => float.IsFinite(n)&&Math.Abs(n)<=max;
    private static void Point(MechanismPoint? p,string name)
    {
        if(p==null||!Bounded(p.X)||!Bounded(p.Y)||!Bounded(p.Z)) throw new ArgumentException("机关向量无效: "+name);
    }
    private static void Definition(MechanismDefinition? d)
    {
        if(d==null) throw new ArgumentException("机关不能为空");
        if(string.IsNullOrWhiteSpace(d.Id)||d.Id.Length>128||string.IsNullOrWhiteSpace(d.Name)||d.Name.Length>256) throw new ArgumentException("机关 ID 或名称无效");
        if(d.ParentId!=null&&(string.IsNullOrWhiteSpace(d.ParentId)||d.ParentId.Length>128)) throw new ArgumentException("父机关 ID 无效");
        if(d.Kind is not ("track" or "rotate" or "scale" or "gravity")) throw new ArgumentException("未知机关类型");
        if(d.Mode is not ("once" or "loop" or "pingpong")) throw new ArgumentException("未知播放模式");
        if(d.RotationSpace is not ("world" or "local") || d.RotationStyle is not ("group" or "orbit" or "spin") || d.RotationInput is not ("speed" or "angle")) throw new ArgumentException("未知旋转空间、方式或输入类型");
        Point(d.Position,"Position");
        if(d.RotationReference!=null) Point(d.RotationReference,"RotationReference");
        if(d.Gravity!=null) Point(d.Gravity,"Gravity");
        if(d.Velocity!=null) Point(d.Velocity,"Velocity");
        if(!Bounded(d.Speed)||d.Speed<=0||!Bounded(d.Duration,MaxSeconds)||d.Duration<=0||!Bounded(d.Delay,MaxSeconds)||d.Delay<0) throw new ArgumentException("机关速度、时长或延迟无效");
        if(!Bounded(d.RotationX)||!Bounded(d.RotationY)||!Bounded(d.RotationZ)||!float.IsFinite(d.Scale)||d.Scale<.01f||d.Scale>100) throw new ArgumentException("机关角速度或缩放无效");
        if(d.FurnitureIds==null||d.FurnitureIds.Any(id=>id<=0)||d.FurnitureIds.Distinct().Count()!=d.FurnitureIds.Length) throw new ArgumentException("机关家具绑定无效");
        if(d.Track==null||d.Track.Length>256||(d.Kind=="track"&&d.Track.Length<2)) throw new ArgumentException("轨道须有 2～256 个点");
        foreach(var p in d.Track) Point(p,"Track");
    }
    /// <summary>Validate the complete graph and bindings, returning detached copies of every array.</summary>
    public static MechanismDefinition[] Validate(MechanismDefinition[] defs,Placement[] items)
    {
        ArgumentNullException.ThrowIfNull(defs); ArgumentNullException.ThrowIfNull(items);
        if(defs.Length>128) throw new ArgumentException("机关数不能超过 128");
        if(items.Any(p=>p==null||!p.Valid)||items.Select(p=>p.Id).Distinct().Count()!=items.Length) throw new ArgumentException("基线家具无效或编号重复");
        var furniture=items.Select(p=>p.Id).ToHashSet();
        var nodes=new Dictionary<string,MechanismDefinition>(StringComparer.Ordinal);
        var bound=new HashSet<int>();
        foreach(var d in defs)
        {
            Definition(d);
            if(!nodes.TryAdd(d.Id,d)) throw new ArgumentException("机关 ID 重复");
            foreach(int id in d.FurnitureIds)
                if(!furniture.Contains(id)||!bound.Add(id)) throw new ArgumentException("家具不存在或被多个机关直接绑定");
        }
        var marks=new Dictionary<string,int>(StringComparer.Ordinal);
        void Visit(MechanismDefinition d)
        {
            if(marks.TryGetValue(d.Id,out int mark)) { if(mark==1) throw new ArgumentException("机关父子关系存在环"); return; }
            marks[d.Id]=1;
            if(d.ParentId!=null)
            {
                if(!nodes.TryGetValue(d.ParentId,out var parent)) throw new ArgumentException("父机关不存在");
                Visit(parent);
            }
            marks[d.Id]=2;
        }
        foreach(var d in defs)
        {
            Visit(d);
            foreach(var targets in new[]{d.OnCompleteTargets,d.OnCycleTargets})
                if(targets!=null && (targets.Length>128 || targets.Distinct(StringComparer.Ordinal).Count()!=targets.Length || targets.Any(id=>id==null || id==d.Id || !nodes.ContainsKey(id))))
                    throw new ArgumentException("机关事件目标不存在、重复或指向自身");
        }
        return defs.Select(d=>d with { OnCompleteTargets=d.OnCompleteTargets is null?null:(string[])d.OnCompleteTargets.Clone(), OnCycleTargets=d.OnCycleTargets is null?null:(string[])d.OnCycleTargets.Clone(), Position=d.Position with {}, FurnitureIds=(int[])d.FurnitureIds.Clone(), Track=d.Track.Select(p=>p with {}).ToArray(), Gravity=d.Gravity is null?null:d.Gravity with {}, Velocity=d.Velocity is null?null:d.Velocity with {}, RotationReference=d.RotationReference is null?null:d.RotationReference with {} }).ToArray();
    }
    private static double PathLength(MechanismDefinition d)
    {
        double length=0;
        for(int i=1;i<d.Track.Length;i++) length+=(V.From(d.Track[i])-V.From(d.Track[i-1])).Length;
        return length;
    }
    internal static double CycleDuration(MechanismDefinition d) => EndTime(d)*(d.Mode=="pingpong"?2:1);
    private static double EndTime(MechanismDefinition d) => d.Kind=="track"?PathLength(d)/d.Speed:d.Duration;
    private static double Elapsed(long start,long now) => ((double)now-start)/1000;
    public static string State(MechanismDefinition def,long start,long now)
    {
        Definition(def);
        if(start==0||now<start) return "启动前";
        double elapsed=Elapsed(start,now);
        if(elapsed<def.Delay) return "启动";
        if(def.Mode=="once"&&elapsed-def.Delay>=EndTime(def)) return "结束";
        return "运行";
    }
    private static double Time(MechanismDefinition d,long start,long now)
    {
        double t=Elapsed(start,now)-d.Delay;
        if(t<=0) return 0;
        double end=EndTime(d);
        if(end<=0) return 0; // Stationary tracks remain stationary, including loop/pingpong.
        if(d.Mode=="once") return Math.Min(t,end);
        if(d.Mode=="loop") return t%end;
        double phase=t%(2*end); return phase<=end?phase:2*end-phase;
    }
    // Double precision vectors and scale prevent overflow even for 128 nested 100x nodes.
    // Numerics row-vector rotation matrices and quaternion composition mirror T(-pivot)*S*R*T(pivot+delta).
    private readonly record struct V(double X,double Y,double Z)
    {
        public static V From(MechanismPoint p) => new(p.X,p.Y,p.Z);
        public double Length => Math.Sqrt(X*X+Y*Y+Z*Z);
        public static V operator +(V a,V b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V operator -(V a,V b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V operator *(V a,double b)=>new(a.X*b,a.Y*b,a.Z*b);
    }
    private static V Rotate(Quaternion q,V v)
    {
        var m=Matrix4x4.CreateFromQuaternion(q);
        return new(v.X*m.M11+v.Y*m.M21+v.Z*m.M31,v.X*m.M12+v.Y*m.M22+v.Z*m.M32,v.X*m.M13+v.Y*m.M23+v.Z*m.M33);
    }
    private static Quaternion Euler(double x,double y,double z)
    {
        const double radians=Math.PI/180;
        var qx=Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)(Math.IEEERemainder(x,360)*radians));
        var qy=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(Math.IEEERemainder(y,360)*radians));
        var qz=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(Math.IEEERemainder(z,360)*radians));
        return Quaternion.Normalize(qy*qx*qz);
    }
    private readonly record struct Pose(V Translation,Quaternion PositionRotation,Quaternion OrientationRotation,double Scale,bool Rotated)
    {
        public static Pose Identity => new(new(0,0,0),Quaternion.Identity,Quaternion.Identity,1,false);
        public V Apply(V point) => Rotate(PositionRotation,point)*Scale+Translation;
        public static Pose Compose(Pose parent,Pose child) => new(parent.Apply(child.Translation),Quaternion.Normalize(parent.PositionRotation*child.PositionRotation),Quaternion.Normalize(parent.OrientationRotation*child.OrientationRotation),parent.Scale*child.Scale,parent.Rotated||child.Rotated);
    }
    private static Pose Local(MechanismDefinition d,double t)
    {
        if(t==0) return Pose.Identity;
        V delta=new(0,0,0); Quaternion rotation=Quaternion.Identity; double scale=1; bool rotated=false;
        switch(d.Kind)
        {
            case "track":
                double distance=t*d.Speed;
                V sample=V.From(d.Track[^1]);
                for(int i=1;i<d.Track.Length;i++)
                {
                    var a=V.From(d.Track[i-1]); var b=V.From(d.Track[i]); double length=(b-a).Length;
                    if(length==0) continue;
                    if(distance<=length) { sample=a+(b-a)*(distance/length); break; }
                    distance-=length;
                }
                delta=sample-V.From(d.Track[0]); break;
            case "rotate":
                double factor=d.RotationInput=="angle"?t/d.Duration:t;
                rotation=Euler(d.RotationX*factor,d.RotationY*factor,d.RotationZ*factor);
                if(d.RotationSpace=="local")
                {
                    var reference=d.RotationReference??new();
                    var basis=Euler(reference.X,reference.Y,reference.Z);
                    rotation=Quaternion.Normalize(basis*rotation*Quaternion.Inverse(basis));
                }
                rotated=d.RotationStyle!="orbit"; break;
            case "scale": scale=1+(d.Scale-1)*t/d.Duration; break;
            case "gravity":
                var velocity=V.From(d.Velocity??new()); var gravity=V.From(d.Gravity??new(0,-9.8f,0));
                delta=velocity*t+gravity*(.5*t*t); break;
        }
        var pivot=V.From(d.Position);
        var positionRotation=d.Kind=="rotate" && d.RotationStyle=="spin"?Quaternion.Identity:rotation;
        var orientationRotation=d.Kind=="rotate" && d.RotationStyle=="orbit"?Quaternion.Identity:rotation;
        return new(pivot+delta-Rotate(positionRotation,pivot)*scale,positionRotation,orientationRotation,scale,rotated);
    }
    private static (float Pitch,float Yaw,float Roll) Angles(Quaternion q)
    {
        var right=Rotate(q,new(1,0,0)); var up=Rotate(q,new(0,1,0)); var forward=Rotate(q,new(0,0,1));
        double pitch=Math.Asin(Math.Clamp(-forward.Y,-1,1)),yaw,roll;
        if(Math.Abs(Math.Cos(pitch))>1e-6) { yaw=Math.Atan2(forward.X,forward.Z); roll=Math.Atan2(right.Y,up.Y); }
        else { yaw=Math.Atan2(-right.Z,right.X); roll=0; }
        return ((float)(pitch*180/Math.PI),(float)(yaw*180/Math.PI),(float)(roll*180/Math.PI));
    }
    /// <summary>Evaluate from original world-space placements. Never pass last frame's output as the baseline.</summary>
    public static Placement[] Evaluate(MechanismDefinition[] defs,Placement[] items,long startUtcMs,long nowUtcMs)
    {
        ArgumentNullException.ThrowIfNull(defs);
        return Evaluate(defs,items,defs.Where(d=>d!=null).ToDictionary(d=>d.Id,d=>startUtcMs,StringComparer.Ordinal),nowUtcMs);
    }
    /// <summary>Per-node starts; inactive children still inherit every active ancestor's transform.</summary>
    public static Placement[] Evaluate(MechanismDefinition[] defs,Placement[] items,IReadOnlyDictionary<string,long> starts,long nowUtcMs)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var valid=Validate(defs,items);
        return EvaluateTimes(valid,items,d=>starts.TryGetValue(d.Id,out long start)&&start!=0&&nowUtcMs>=start?Time(d,start,nowUtcMs):0);
    }
    /// <summary>Preview a rotation subtree from its baseline without mutating runtime starts.
    /// Progress maps to the selected node's Duration, ignoring delays and repeat modes;
    /// descendants use the same seconds, capped at their individual endpoints.</summary>
    public static Placement[] Preview(MechanismDefinition[] defs,Placement[] items,string id,float progress)
    {
        if(!float.IsFinite(progress)||progress<0||progress>1) throw new ArgumentException("预览进度须在0～1之间");
        var valid=Validate(defs,items);
        var selected=valid.FirstOrDefault(d=>d.Id==id)??throw new ArgumentException("预览机关不存在");
        if(selected.Kind!="rotate") throw new ArgumentException("预览仅支持旋转机关");
        var subtree=new HashSet<string>(StringComparer.Ordinal){id};
        bool changed;
        do { changed=false; foreach(var node in valid) if(node.ParentId!=null&&subtree.Contains(node.ParentId)) changed|=subtree.Add(node.Id); } while(changed);
        double seconds=(double)progress*selected.Duration;
        return EvaluateTimes(valid,items,d=>subtree.Contains(d.Id)?Math.Min(seconds,EndTime(d)):0);
    }
    private static Placement[] EvaluateTimes(MechanismDefinition[] valid,Placement[] items,Func<MechanismDefinition,double> time)
    {
        var nodes=valid.ToDictionary(d=>d.Id,StringComparer.Ordinal); var poses=new Dictionary<string,Pose>(StringComparer.Ordinal);
        Pose World(MechanismDefinition d)
        {
            if(poses.TryGetValue(d.Id,out var cached)) return cached;
            var local=Local(d,time(d));
            var world=d.ParentId==null?local:Pose.Compose(World(nodes[d.ParentId]),local);
            poses.Add(d.Id,world); return world;
        }
        var bindings=new Dictionary<int,Pose>();
        foreach(var d in valid) { var pose=World(d); foreach(int id in d.FurnitureIds) bindings.Add(id,pose); }
        return items.Select(p=>
        {
            if(!bindings.TryGetValue(p.Id,out var pose)||pose==Pose.Identity) return p;
            var point=pose.Apply(new(p.X,p.Y,p.Z));
            var angles=pose.Rotated?Angles(Quaternion.Normalize(pose.OrientationRotation*Euler(p.Pitch,p.Yaw,p.Roll))):(p.Pitch,p.Yaw,p.Roll);
            return (p with {X=(float)Math.Clamp(point.X,-Limit,Limit),Y=(float)Math.Clamp(point.Y,-Limit,Limit),Z=(float)Math.Clamp(point.Z,-Limit,Limit),Pitch=angles.Item1,Yaw=angles.Item2,Roll=angles.Item3,Scale=(float)Math.Clamp(p.Scale*pose.Scale,.01,100)}).Rounded();
        }).ToArray();
    }
}
