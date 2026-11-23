using LocalHomeModule;
using System.Numerics;

static class Program
{
    static int checks;
    static void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
    static void Near(float actual,double expected,string message,double tolerance=.025) => Check(Math.Abs(actual-expected)<tolerance,$"{message}: {actual} vs {expected}");
    static void Bad(Action action,string message) { bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; } Check(rejected,message); }
    static readonly Placement[] Items={new(1,12,1,0,0,0),new(2,12,2,0,0,0),new(3,12,9,8,7,13,24,35)};
    static MechanismDefinition Node(string kind="track",int[]? ids=null) => new("a","机关",kind,new(),null,ids??new[]{1},new[]{new MechanismPoint(),new MechanismPoint(10)},Speed:2,Duration:2,RotationY:90);
    static Placement Run(MechanismDefinition d,double seconds) => MechanismEngine.Evaluate(new[]{d},Items,1000,1000+(long)(seconds*1000))[0];
    static void Main()
    {
        var track=Node(); Near(Run(track,2).X,5,"track speed"); Near(Run(track,8).X,11,"track endpoint independent of Duration");
        Check(MechanismEngine.State(track,1000,4000)=="运行","track longer than duration");
        Check(MechanismEngine.State(track,1000,6000)=="结束","track exact end");
        var corner=track with { Track=new[]{new MechanismPoint(20,30,40),new MechanismPoint(23,30,40),new MechanismPoint(23,34,40)},Speed=1 };
        var cornerP=Run(corner,5); Near(cornerP.X,4,"absolute track relative first x"); Near(cornerP.Y,2,"corner interpolation y");
        var zero=track with {Track=new[]{new MechanismPoint(8),new MechanismPoint(8),new MechanismPoint(8)}};
        Near(Run(zero,50).X,1,"zero length track"); Check(MechanismEngine.State(zero,1000,1000)=="结束","stationary once end");
        Near(Run(track with {Mode="loop"},6).X,3,"loop wraps");
        Near(Run(track with {Mode="loop"},5).X,1,"loop exact period");
        Near(Run(track with {Mode="pingpong"},7).X,7,"pingpong return");
        Near(Run(track with {Mode="pingpong"},10).X,1,"pingpong full period");
        var delayed=track with {Delay=2}; Near(Run(delayed,1).X,1,"delay static"); Near(Run(delayed,3).X,3,"delay shifts elapsed");
        Check(MechanismEngine.State(delayed,1000,999)=="启动前","before start"); Check(MechanismEngine.State(delayed,0,99999)=="启动前","inactive state");
        Check(MechanismEngine.State(delayed,1000,1000)=="启动","delay state"); Check(MechanismEngine.State(delayed,1000,2999)=="启动","delay boundary");
        Check(MechanismEngine.State(delayed,1000,3000)=="运行","delay exact running"); Check(MechanismEngine.State(delayed,1000,8000)=="结束","delayed end");
        Check(MechanismEngine.State(track with {Mode="loop"},1000,long.MaxValue)=="运行","loop never ends");
        Check(MechanismEngine.Evaluate(new[]{track},Items,0,10000).SequenceEqual(Items),"inactive baseline");
        Check(MechanismEngine.Evaluate(new[]{track},Items,10000,9999).SequenceEqual(Items),"before baseline");
        var rotation=Node("rotate"); var rotated=Run(rotation,1); Near(rotated.X,0,"yaw90 pivot x"); Near(rotated.Z,-1,"yaw90 pivot z"); Near(rotated.Yaw,90,"yaw90 heading");
        var rx=Run(rotation with {RotationX=90,RotationY=0},1); Near(rx.Yaw,0,"X rotation yaw"); Near(rx.Pitch,90,"X rotation pitch");
        var rz=Run(rotation with {RotationZ=90,RotationY=0},1); Near(rz.Y,1,"Z rotation y"); Near(rz.Roll,90,"Z rotation roll");
        var pivoted=Run(rotation with {Position=new(1,0,0)},1); Near(pivoted.X,1,"pivot stationary"); Near(pivoted.Z,0,"pivot stationary z");
        var scale=Node("scale") with {Position=new(2),Scale=3}; Near(Run(scale,1).X,0,"scale around pivot"); Near(Run(scale,1).Scale,2,"linear scale"); Near(Run(scale,9).Scale,3,"scale once holds");
        Near(Run(scale with {Mode="pingpong"},3).Scale,2,"scale pingpong"); Near(Run(scale with {Mode="loop"},2).Scale,1,"scale loop exact boundary");
        var gravity=Node("gravity") with {Velocity=new(2,3,4)}; var g=Run(gravity,1); Near(g.X,3,"velocity x"); Near(g.Y,-1.9,"default gravity"); Near(g.Z,4,"velocity z"); Near(Run(gravity,5).Y,-13.6,"gravity stop at duration");
        var custom=Run(gravity with {Gravity=new(0,2,0)},1); Near(custom.Y,4,"custom gravity");
        var parent=Node("rotate",Array.Empty<int>()) with {Id="parent"};
        var child=Node("track") with {ParentId="parent",Track=new[]{new MechanismPoint(),new MechanismPoint(10)},Speed=1};
        var defs=new[]{child,parent}; var combined=MechanismEngine.Evaluate(defs,Items,1000,2000)[0]; Near(combined.X,0,"child translation parent rotated x"); Near(combined.Z,-2,"child translation parent rotated z"); Near(combined.Yaw,90,"parent heading inherited");
        var rotateChild=Node("rotate") with {ParentId="parent",Position=new(1),RotationY=90};
        var orbit=MechanismEngine.Evaluate(new[]{rotateChild,parent},new[]{Items[0] with {X=2}},1000,2000)[0]; Near(orbit.X,-1,"child pivot follows parent x"); Near(orbit.Z,-1,"child pivot follows parent z");
        var delayedChild=child with {Delay=10}; var inherited=MechanismEngine.Evaluate(new[]{parent,delayedChild},Items,1000,2000)[0]; Near(inherited.Z,-1,"delayed child still inherits parent");
        var rootScale=Node("scale",new[]{2}) with {Id="parent",Scale=3}; var scaledChild=MechanismEngine.Evaluate(new[]{rootScale,child},Items,1000,2000); Near(scaledChild[0].X,4,"child scaled by parent"); Near(scaledChild[1].X,4,"direct parent binding"); Check(scaledChild[2]==Items[2],"unbound untouched");
        var parentOnly=MechanismEngine.Evaluate(defs,Items,new Dictionary<string,long>{{"parent",1000}},2000)[0]; Near(parentOnly.Z,-1,"inactive child inherits active parent");
        var childOnly=MechanismEngine.Evaluate(defs,Items,new Dictionary<string,long>{{"a",1000}},2000)[0]; Near(childOnly.X,2,"active child inactive parent"); Near(childOnly.Z,0,"inactive parent has no rotation");
        var split=MechanismEngine.Evaluate(defs,Items,new Dictionary<string,long>{{"parent",1000},{"a",1500}},2000)[0]; Near(split.Z,-1.5,"independent start times");
        Check(MechanismEngine.Evaluate(defs,Items,new Dictionary<string,long>(),2000).SequenceEqual(Items),"missing starts inactive");
        Check(MechanismEngine.Evaluate(defs,Items,new Dictionary<string,long>{{"parent",3000},{"a",0}},2000).SequenceEqual(Items),"future and zero start inactive");
        var clone=MechanismEngine.Validate(new[]{track},Items); clone[0].FurnitureIds[0]=2; clone[0].Track[0]=new(999); Check(track.FurnitureIds[0]==1&&track.Track[0].X==0,"validated arrays detached");
        var snapshot=(Placement[])Items.Clone(); var baselineResult=MechanismEngine.Evaluate(defs,Items,1000,2000);
        for(int i=0;i<1000;i++) Check(MechanismEngine.Evaluate(defs,Items,1000,2000).SequenceEqual(baselineResult),"no cumulative drift");
        Check(Items.SequenceEqual(snapshot),"baseline immutable");
        // Independent quaternion reference: rotate Z, then X, then Y; nested rotations multiply parent*child.
        var random=new Random(4919);
        for(int i=0;i<300;i++)
        {
            float x=random.Next(-170,170),y=random.Next(-170,170),z=random.Next(-170,170);
            var n=rotation with {RotationX=x,RotationY=y,RotationZ=z,Duration=1}; var result=Run(n,1);
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,y*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,x*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,z*MathF.PI/180);
            var expected=Vector3.Transform(Vector3.UnitX,q); Near(result.X,expected.X,"Unity order x"); Near(result.Y,expected.Y,"Unity order y"); Near(result.Z,expected.Z,"Unity order z");
            var actualQ=Quaternion.CreateFromAxisAngle(Vector3.UnitY,result.Yaw*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,result.Pitch*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,result.Roll*MathF.PI/180);
            Near(MathF.Abs(Quaternion.Dot(q,actualQ)),1,"Euler orientation",.00001);
        }
        foreach(var angles in new[]{(90f,35f,24f),(-90f,75f,-12f),(89.99f,120f,54f),(-89.99f,-170f,100f)})
        {
            var d=rotation with {RotationX=angles.Item1,RotationY=angles.Item2,RotationZ=angles.Item3,Duration=1}; var p=Run(d,1);
            var expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,angles.Item2*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,angles.Item1*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,angles.Item3*MathF.PI/180);
            var actual=Quaternion.CreateFromAxisAngle(Vector3.UnitY,p.Yaw*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,p.Pitch*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,p.Roll*MathF.PI/180);
            Near(MathF.Abs(Quaternion.Dot(expected,actual)),1,"gimbal reconstruction",.00001);
        }
        // Extreme finite inputs and a full 128-node scale chain must clamp, never return NaN/Infinity.
        var extreme=MechanismEngine.Evaluate(new[]{gravity with {Duration=86400,Velocity=new(100000,100000,100000),Gravity=new(100000,-100000,100000)}},Items,1,long.MaxValue);
        Check(extreme.All(p=>p.Valid),"extreme gravity clamped");
        var chain=Enumerable.Range(0,128).Select(i=>Node("scale",i==127?new[]{1}:Array.Empty<int>()) with {Id="n"+i,ParentId=i==0?null:"n"+(i-1),Scale=100,Position=new(100000,-100000,100000)}).ToArray();
        Check(MechanismEngine.Evaluate(chain,Items,1,long.MaxValue).All(p=>p.Valid),"deep chain clamped");
        foreach(var invalid in new[]{track with {Id=""},track with {Name=""},track with {Kind="bad"},track with {Mode="bad"},track with {Speed=0},track with {Duration=0},track with {Duration=float.NaN},track with {Delay=-1},track with {Scale=101},track with {Position=new(float.NaN)},track with {RotationX=float.PositiveInfinity},track with {Velocity=new(100001)},track with {Track=Array.Empty<MechanismPoint>()},track with {Track=new MechanismPoint[]{new(),null!}},track with {FurnitureIds=new[]{1,1}},track with {FurnitureIds=new[]{99}},track with {Track=Enumerable.Repeat(new MechanismPoint(),257).ToArray()}}) Bad(()=>MechanismEngine.Validate(new[]{invalid},Items),"invalid definition accepted");
        Bad(()=>MechanismEngine.Validate(new[]{track,track},Items),"duplicate id");
        Bad(()=>MechanismEngine.Validate(new[]{track,track with {Id="b"}},Items),"duplicate binding");
        Bad(()=>MechanismEngine.Validate(new[]{track with {ParentId="missing"}},Items),"missing parent");
        Bad(()=>MechanismEngine.Validate(new[]{track with {ParentId="a"}},Items),"self cycle");
        Bad(()=>MechanismEngine.Validate(new[]{track with {ParentId="b"},track with {Id="b",ParentId="a",FurnitureIds=Array.Empty<int>()}},Items),"multi cycle");
        Bad(()=>MechanismEngine.Validate(Enumerable.Range(0,129).Select(i=>track with {Id=i.ToString(),FurnitureIds=Array.Empty<int>()}).ToArray(),Items),"node max");
        Bad(()=>MechanismEngine.Validate(new[]{track},new[]{Items[0],Items[0]}),"duplicate baseline");
        Bad(()=>MechanismEngine.Validate(new[]{track},new[]{Items[0] with {X=float.NaN}}),"invalid baseline");
        var eventTarget=track with {Id="b",FurnitureIds=Array.Empty<int>()};
        foreach(var badTargets in new[]{new[]{"a"},new[]{"missing"},new[]{"b","b"},new string[]{null!}})
            Bad(()=>MechanismEngine.Validate(new[]{track with {OnCompleteTargets=badTargets},eventTarget},Items),"invalid completion targets");
        foreach(var badTargets in new[]{new[]{"a"},new[]{"missing"},new[]{"b","b"},new string[]{null!}})
            Bad(()=>MechanismEngine.Validate(new[]{track with {OnCycleTargets=badTargets},eventTarget},Items),"invalid cycle targets");
        var eventNode=track with {OnCompleteTargets=new[]{"b"},OnCycleTargets=new[]{"b"}};
        var eventClone=MechanismEngine.Validate(new[]{eventNode,eventTarget with {OnCompleteTargets=new[]{"a"}}},Items);
        eventClone[0].OnCompleteTargets![0]="changed";eventClone[0].OnCycleTargets![0]="changed";
        Check(eventNode.OnCompleteTargets[0]=="b"&&eventNode.OnCycleTargets[0]=="b","event arrays detached; mutual graph legal");
        var legacy=System.Text.Json.JsonSerializer.Deserialize<MechanismDefinition>("{\"Id\":\"old\",\"Name\":\"old\",\"Kind\":\"rotate\",\"Position\":{},\"FurnitureIds\":[],\"Track\":[]}")!;
        Check(legacy.OnCompleteTargets==null&&legacy.OnCycleTargets==null,"legacy json event defaults");
        Check(MechanismEngine.CycleDuration(track)==5&&MechanismEngine.CycleDuration(track with{Mode="pingpong"})==10,"track cycle uses length/speed");
        checks+=RotationChecks.Run();
        Console.WriteLine($"PASS: {checks} mechanism checks (track, lifecycle, transformations, hierarchy, cycles, limits and baseline immutability).");
    }
}
