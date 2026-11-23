using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalHomeModule;

internal static class RotationChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool ok,string label) { checks++; if(!ok) throw new Exception("Rotation: "+label); }
        void Near(float a,float b,string label) => Check(Math.Abs(a-b)<.035f,$"{label}: {a} vs {b}");
        Quaternion Euler(float x,float y,float z) => Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,y*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,x*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,z*MathF.PI/180));
        void Orient(Placement result,Quaternion expected,string label)
        {
            var actual=Euler(result.Pitch,result.Yaw,result.Roll);
            foreach(var axis in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ})
            {
                var a=Vector3.Transform(axis,actual); var b=Vector3.Transform(axis,expected);
                Near(a.X,b.X,label+" x"); Near(a.Y,b.Y,label+" y"); Near(a.Z,b.Z,label+" z");
            }
        }
        void Position(Placement result,Vector3 expected,string label)
        { Near(result.X,expected.X,label+" x"); Near(result.Y,expected.Y,label+" y"); Near(result.Z,expected.Z,label+" z"); }
        void Bad(Action action,string label) { bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label); }
        var furniture=new Placement(1,12,2,3,4,25,35,15);
        var items=new[]{furniture};
        var node=new MechanismDefinition("R","R","rotate",new(1,2,3),null,new[]{1},Array.Empty<MechanismPoint>(),Duration:2,RotationY:90,RotationInput:"angle");
        Placement Eval(MechanismDefinition n,float seconds=2) => MechanismEngine.Evaluate(new[]{n},items,1000,1000+(long)(seconds*1000))[0];
        var random=new Random(19122);
        for(int i=0;i<100;i++)
        {
            var reference=new MechanismPoint(random.Next(-175,175),random.Next(-175,175),random.Next(-175,175));
            var angles=new MechanismPoint(random.Next(-175,175),random.Next(-175,175),random.Next(-175,175));
            var basis=Euler(reference.X,reference.Y,reference.Z); var delta=Euler(angles.X,angles.Y,angles.Z);
            var local=Quaternion.Normalize(basis*delta*Quaternion.Inverse(basis));
            foreach(string space in new[]{"world","local"})
                foreach(string style in new[]{"group","orbit","spin"})
                {
                    var n=node with {RotationSpace=space,RotationReference=reference,RotationStyle=style,RotationX=angles.X,RotationY=angles.Y,RotationZ=angles.Z};
                    var q=space=="local"?local:delta; var output=Eval(n);
                    var p=new Vector3(furniture.X,furniture.Y,furniture.Z); var pivot=new Vector3(1,2,3);
                    Position(output,style=="spin"?p:pivot+Vector3.Transform(p-pivot,q),space+" "+style);
                    Orient(output,style=="orbit"?Euler(furniture.Pitch,furniture.Yaw,furniture.Roll):q*Euler(furniture.Pitch,furniture.Yaw,furniture.Roll),space+" "+style);
                }
        }
        var halfway=Eval(node,1); var speed=node with {RotationInput="speed"};
        Position(halfway,new Vector3(1,2,3)+Vector3.Transform(new Vector3(1,1,1),Euler(0,45,0)),"angle half");
        Position(Eval(speed,1),new Vector3(1,2,3)+Vector3.Transform(new Vector3(1,1,1),Euler(0,90,0)),"speed one second");
        // Parent and descendant independently compose position and orientation channels.
        foreach(string parentStyle in new[]{"group","orbit","spin"})
            foreach(string childStyle in new[]{"group","orbit","spin"})
            {
                var parent=node with {Id="P",FurnitureIds=Array.Empty<int>(),RotationStyle=parentStyle,RotationY=90};
                var child=node with {ParentId="P",RotationStyle=childStyle,Position=new(-1,0,1),RotationY=0,RotationZ=60};
                var result=MechanismEngine.Evaluate(new[]{child,parent},items,1000,3000)[0];
                var qp=Euler(0,90,0); var qc=Euler(0,0,60);
                var p=new Vector3(2,3,4); var cp=new Vector3(-1,0,1); var pp=new Vector3(1,2,3);
                if(childStyle!="spin") p=cp+Vector3.Transform(p-cp,qc);
                if(parentStyle!="spin") p=pp+Vector3.Transform(p-pp,qp);
                Position(result,p,"nested "+parentStyle+"/"+childStyle);
                var orient=Euler(furniture.Pitch,furniture.Yaw,furniture.Roll);
                if(childStyle!="orbit") orient=qc*orient;
                if(parentStyle!="orbit") orient=qp*orient;
                Orient(result,orient,"nested orientation");
            }
        var json=JsonNode.Parse(JsonSerializer.Serialize(node))!.AsObject();
        foreach(var field in new[]{"RotationSpace","RotationReference","RotationStyle","RotationInput"}) json.Remove(field);
        var legacy=JsonSerializer.Deserialize<MechanismDefinition>(json.ToJsonString())!;
        Check(legacy.RotationSpace=="world"&&legacy.RotationStyle=="group"&&legacy.RotationInput=="speed"&&legacy.RotationReference==null,"legacy JSON defaults");
        Check(Eval(legacy)==Eval(speed),"legacy default output unchanged");
        var localNode=node with {RotationSpace="local",RotationReference=new(0,0,90)};
        var clone=MechanismEngine.Validate(new[]{localNode},items)[0];
        Check(clone.RotationReference==localNode.RotationReference&&!ReferenceEquals(clone.RotationReference,localNode.RotationReference),"reference cloned");
        Bad(()=>MechanismEngine.Validate(new[]{node with {RotationSpace="bad"}},items),"invalid space");
        Bad(()=>MechanismEngine.Validate(new[]{node with {RotationStyle="bad"}},items),"invalid style");
        Bad(()=>MechanismEngine.Validate(new[]{node with {RotationInput="bad"}},items),"invalid input");
        Bad(()=>MechanismEngine.Validate(new[]{node with {RotationReference=new(float.NaN)}},items),"invalid reference");
        var loop=node with {Mode="loop",Delay=8}; var preview0=MechanismEngine.Preview(new[]{loop},items,"R",0); var preview1=MechanismEngine.Preview(new[]{loop},items,"R",1);
        Check(preview0.SequenceEqual(items),"preview exact baseline"); Check(preview1[0]==Eval(node),"preview loop endpoint does not wrap or delay");
        Check(MechanismEngine.Preview(new[]{loop},items,"R",.5f)[0]==halfway,"preview half maps duration seconds");
        var ancestor=node with {Id="P",FurnitureIds=Array.Empty<int>()};
        var selected=loop with {ParentId="P"};
        var childTrack=new MechanismDefinition("C","C","track",new(),"R",new[]{2},new[]{new MechanismPoint(),new MechanismPoint(100)},Speed:2,Duration:1,Delay:12,Mode:"loop");
        var allItems=new[]{furniture,new Placement(2,12,1,0,0,0),new Placement(3,12,9,0,0,0)};
        var unrelated=node with {Id="U",FurnitureIds=new[]{3}};
        var graph=new[]{ancestor,selected,childTrack,unrelated};
        var actual=MechanismEngine.Preview(graph,allItems,"R",1);
        var expectedGraph=graph.Select(n=>n with {Delay=0,Mode="once"}).ToArray();
        var expected=MechanismEngine.Evaluate(expectedGraph,allItems,new Dictionary<string,long>{{"R",1000},{"C",1000}},3000);
        Check(actual.SequenceEqual(expected),"preview descendants use actual seconds without active ancestors or siblings");
        Check(graph[1].Delay==8&&graph[2].Mode=="loop"&&allItems[0]==furniture,"preview does not mutate definitions or baseline");
        Bad(()=>MechanismEngine.Preview(new[]{node},items,"R",float.NaN),"preview rejects NaN");
        Bad(()=>MechanismEngine.Preview(new[]{node},items,"R",1.1f),"preview rejects out of range");
        Bad(()=>MechanismEngine.Preview(new[]{node},items,"missing",0),"preview rejects missing selection");
        Bad(()=>MechanismEngine.Preview(new[]{node with {Kind="scale"}},items,"R",0),"preview limited to rotation");
        return checks;
    }
}
