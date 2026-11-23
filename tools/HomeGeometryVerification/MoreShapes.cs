using LocalHomeModule;
using System.Numerics;

internal static class MoreShapes
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool ok,string name) { checks++; if(!ok) throw new Exception(name); }
        void Near(double a,double b,string name,double eps=.0001) => Check(Math.Abs(a-b)<=eps,$"{name}: {a} vs {b}");
        void Reject(Action action,string name)
        {
            try { action(); } catch(ArgumentException) { checks++; return; } catch(InvalidDataException) { checks++; return; }
            throw new Exception("Expected rejection: "+name);
        }
        Vector3 V((float X,float Y,float Z) p)=>new(p.X,p.Y,p.Z);
        Vector3 Rotate(Vector3 p,EditorOrigin o)
        {
            double x=o.Pitch*Math.PI/180,y=o.Yaw*Math.PI/180,z=o.Roll*Math.PI/180;
            double a=p.X*Math.Cos(z)-p.Y*Math.Sin(z),b=p.X*Math.Sin(z)+p.Y*Math.Cos(z);
            double c=b*Math.Cos(x)-p.Z*Math.Sin(x),d=b*Math.Sin(x)+p.Z*Math.Cos(x);
            return new((float)(a*Math.Cos(y)+d*Math.Sin(y)),(float)c,(float)(-a*Math.Sin(y)+d*Math.Cos(y)));
        }
        var zero=new EditorOrigin();
        string[] shapes={"line","circle","sphere","grid","cube","disk","ball","cylinder","cone","helix","spiral","triangle","square","pentagon","hexagon","star"};
        foreach(string shape in shapes)
        {
            foreach(int n in new[]{1,2,3,5,6,7,17,101,10000})
            {
                var points=HomeGeometry.Generate(shape,n,.2f,10,zero);
                Check(points.Length==n,"exact count "+shape);
                Check(points.Distinct().Count()==n,"distinct points/no closed tail "+shape+" n="+n);
                Check(points.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)),"finite "+shape);
            }
            var origin=new EditorOrigin(17,-23,51,23,47,69);
            var local=HomeGeometry.Generate(shape,101,.2f,10,zero);
            var world=HomeGeometry.Generate(shape,101,.2f,10,origin);
            for(int i=0;i<local.Length;i++)
                Near(Vector3.Distance(V(world[i]),Rotate(V(local[i]),origin)+new Vector3(origin.X,origin.Y,origin.Z)),0,"origin rotation "+shape);
            var collapse=HomeGeometry.Generate(shape,7,0,0,new(100000,-100000,100000));
            Check(collapse.All(p=>p==(100000f,-100000f,100000f)),"degenerate zero size "+shape);
            Reject(()=>HomeGeometry.Generate(shape,0,1,1,zero),"zero count "+shape);
            Reject(()=>HomeGeometry.Generate(shape,10001,1,1,zero),"count max "+shape);
            Reject(()=>HomeGeometry.Generate(shape,1,float.NaN,1,zero),"nan spacing "+shape);
            Reject(()=>HomeGeometry.Generate(shape,1,1,float.NaN,zero),"nan radius "+shape);
            Reject(()=>HomeGeometry.Generate(shape,1,1,-1,zero),"negative radius "+shape);
            Reject(()=>HomeGeometry.Generate(shape,1,1,1,new(Yaw:float.PositiveInfinity)),"origin validation "+shape);
        }
        const float radius=10;
        foreach(var p in HomeGeometry.Generate("cube",10000,1,radius,zero))
        {
            Near(Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z))),radius,"cube surface");
        }
        var cube=HomeGeometry.Generate("cube",10000,1,radius,zero);
        int[] faces=new int[6];
        foreach(var p in cube) { int f=p.X==radius?0:p.X==-radius?1:p.Y==radius?2:p.Y==-radius?3:p.Z==radius?4:5; faces[f]++; }
        Check(faces.Max()-faces.Min()<=1,"balanced cube face populations");
        foreach(var p in HomeGeometry.Generate("disk",10000,1,radius,zero))
        {
            Check(p.Y==0&&p.X*p.X+p.Z*p.Z<=radius*radius+.0001,"disk inside XZ circle");
        }
        var ball=HomeGeometry.Generate("ball",10000,1,radius,zero);
        Check(ball.All(p=>V(p).Length()<=radius+.0001),"ball inside sphere");
        Near(ball.Average(p=>Math.Pow(V(p).Length()/radius,3)),.5,"ball uniform volume radial CDF",.001);
        Near(ball.Count(p=>V(p).Length()<radius/2.0)/(double)ball.Length,.125,"ball inner half volume",.002);
        Near(V((ball.Average(p=>p.X),ball.Average(p=>p.Y),ball.Average(p=>p.Z))).Length(),0,"ball centroid",.02);
        foreach(var p in HomeGeometry.Generate("cylinder",10000,1,radius,zero))
        {
            Near(Math.Sqrt(p.X*p.X+p.Z*p.Z),radius,"cylinder side radius");
            Check(p.Y>=-radius&&p.Y<=radius,"cylinder height");
        }
        foreach(var p in HomeGeometry.Generate("cone",10000,1,radius,zero))
        {
            Near(Math.Sqrt(p.X*p.X+p.Z*p.Z),(radius-p.Y)/2,"cone side slope");
            Check(p.Y>=-radius&&p.Y<=radius,"cone height");
        }
        foreach(int n in new[]{13,10000})
        {
            var flatHelix=HomeGeometry.Generate("helix",n,0,radius,zero);
            Check(flatHelix.Distinct().Count()==n,"zero-height helix uses unique circle samples");
            Check(flatHelix.All(p=>p.Y==0),"zero-height helix is planar");
        }
        var helix=HomeGeometry.Generate("helix",101,.2f,radius,zero);
        Near(helix[^1].Y-helix[0].Y,.2*100,"helix total axial height");
        foreach(var p in helix) Near(Math.Sqrt(p.X*p.X+p.Z*p.Z),radius,"helix radius");
        var spiral=HomeGeometry.Generate("spiral",1001,1,radius,zero);
        Near(V(spiral[0]).Length(),0,"spiral starts at center");
        Near(V(spiral[^1]).Length(),radius,"spiral outer radius");
        Check(spiral.Zip(spiral.Skip(1),(a,b)=>V(b).Length()+.00001>=V(a).Length()).All(x=>x),"spiral radius monotone");
        var steps=spiral.Zip(spiral.Skip(1),(a,b)=>(double)Vector3.Distance(V(a),V(b))).ToArray();
        Check(steps.Max()/steps.Min()<1.02,"spiral nearly equal arc spacing");
        foreach(var entry in new[]{("triangle",3),("square",4),("pentagon",5),("hexagon",6),("star",10)})
        {
            // Three samples per edge: endpoints and their one-third interpolants.
            var polygon=HomeGeometry.Generate(entry.Item1,entry.Item2*3,1,radius,zero);
            var edges=new List<double>();
            for(int j=0;j<entry.Item2;j++)
            {
                var a=V(polygon[3*j]); var b=V(polygon[(3*j+3)%polygon.Length]);
                Near(Vector3.Distance(V(polygon[3*j+1]),Vector3.Lerp(a,b,1f/3)),0,"polygon first interpolant "+entry.Item1);
                Near(Vector3.Distance(V(polygon[3*j+2]),Vector3.Lerp(a,b,2f/3)),0,"polygon second interpolant "+entry.Item1);
                Near(a.Length(),entry.Item1=="star"&&j%2==1?radius*(3-Math.Sqrt(5))/2:radius,"polygon circumradius "+entry.Item1);
                edges.Add(Vector3.Distance(a,b));
            }
            Near(edges.Max()-edges.Min(),0,"polygon equal edges "+entry.Item1);
        }
        // World-space +Z must match target even when origin is rotated; no additional rotation.
        var target=new EditorOrigin(10,-20,30,37,143,-59);
        var random=new Random(703);
        for(int i=0;i<500;i++)
        {
            var item=new Placement(i+1,42,random.Next(-100,100),random.Next(-100,100),random.Next(-100,100),725,120,-540,2);
            var faced=HomeGeometry.FaceOrigin(item,target);
            var expected=Vector3.Normalize(new Vector3(target.X-item.X,target.Y-item.Y,target.Z-item.Z));
            var actual=Rotate(Vector3.UnitZ,new(Pitch:faced.Pitch,Yaw:faced.Yaw,Roll:faced.Roll));
            Near(Vector3.Distance(actual,expected),0,"face world forward",.0002);
            Check(faced.Id==item.Id&&faced.Item==item.Item&&faced.X==item.X&&faced.Y==item.Y&&faced.Z==item.Z&&faced.Scale==item.Scale,"face preserves furniture values");
            Check(faced.Roll==0&&faced.Valid&&faced==faced.Rounded(),"face output validated rounded");
        }
        foreach(var axis in new[]{(Vector3.UnitX,90f,0f),(-Vector3.UnitX,-90f,0f),(Vector3.UnitZ,0f,0f),(-Vector3.UnitZ,180f,0f),(Vector3.UnitY,143f,-90f),(-Vector3.UnitY,143f,90f)})
        {
            var p=new Placement(1,42,target.X-axis.Item1.X,target.Y-axis.Item1.Y,target.Z-axis.Item1.Z,0);
            var face=HomeGeometry.FaceOrigin(p,target);
            Near(face.Yaw,axis.Item2,"axis yaw"); Near(face.Pitch,axis.Item3,"axis pitch");
        }
        var same=new Placement(1,42,target.X,target.Y,target.Z,0);
        Check(HomeGeometry.FaceOrigin(same,target)==same with{Pitch=target.Pitch,Yaw=target.Yaw,Roll=target.Roll},"coincident takes full origin orientation");
        var tiny=new Placement(1,42,0.0000001f,0,0,0);
        var tinyOrigin=new EditorOrigin(Pitch:1.125f,Yaw:-1.125f,Roll:12.345f);
        var tinyFace=HomeGeometry.FaceOrigin(tiny,tinyOrigin);
        Check(tinyFace.Pitch==1.13f&&tinyFace.Yaw==-1.13f&&tinyFace.Roll==12.35f,"tiny coincidence and rounding");
        Reject(()=>HomeGeometry.FaceOrigin(null!,zero),"null item");
        Reject(()=>HomeGeometry.FaceOrigin(same,null!),"null origin");
        foreach(var invalid in new[]{same with{Id=0},same with{Item=0},same with{X=float.NaN},same with{Pitch=float.PositiveInfinity},same with{Scale=0},same with{X=100001}})
            Reject(()=>HomeGeometry.FaceOrigin(invalid,zero),"invalid item before face");
        Reject(()=>HomeGeometry.FaceOrigin(same,new(Roll:100001)),"invalid face origin");
        return checks;
    }
}
