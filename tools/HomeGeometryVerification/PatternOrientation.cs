using LocalHomeModule;
using System.Numerics;

internal static class PatternOrientation
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
        void Reject(Action action) { try { action(); } catch(ArgumentException) { checks++; return; } throw new Exception("expected invalid OrientPattern input rejection"); }
        // Independent scalar rotations, never production helpers.
        Vector3 Rotate(Vector3 p,EditorOrigin o)
        {
            double x=o.Pitch*Math.PI/180,y=o.Yaw*Math.PI/180,z=o.Roll*Math.PI/180;
            double a=p.X*Math.Cos(z)-p.Y*Math.Sin(z),b=p.X*Math.Sin(z)+p.Y*Math.Cos(z);
            double c=b*Math.Cos(x)-p.Z*Math.Sin(x),d=b*Math.Sin(x)+p.Z*Math.Cos(x);
            return new((float)(a*Math.Cos(y)+d*Math.Sin(y)),(float)c,(float)(-a*Math.Sin(y)+d*Math.Cos(y)));
        }
        void YawMatches(Placement p,Vector3 direction,EditorOrigin o,string message)
        {
            Check(p.Pitch==0&&p.Roll==0,message+" must stay upright");
            double h=Math.Sqrt(direction.X*direction.X+direction.Z*direction.Z);
            if(h<=1e-6) Check(p.Yaw==o.Rounded().Yaw,message+" fallback");
            else { double a=p.Yaw*Math.PI/180; Check((Math.Sin(a)*direction.X+Math.Cos(a)*direction.Z)/h>.9999999,message+" yaw"); }
        }
        string[] shapes={"line","circle","sphere","grid","cube","disk","ball","cylinder","cone","helix","spiral","triangle","square","pentagon","hexagon","star","ellipse","rectangle","octagon","torus","capsule","doublehelix","heart","wave"};
        var origins=new[]{new EditorOrigin(),new EditorOrigin(5,-2,7,37,61,-23),new EditorOrigin(5,-2,7,90,27,0),new EditorOrigin(5,-2,7,0,27,90)};
        var item=new Placement(1,12,6,4,9,123,46,79,2);
        foreach(var o in origins)
        foreach(var shape in shapes)
        {
            var points=HomeGeometry.Generate(shape,121,.15f,3,o);
            for(int i=0;i<points.Length;i++)
            {
                var point=points[i]; var p=item with {Id=i+1,X=point.X,Y=point.Y,Z=point.Z};
                var result=HomeGeometry.OrientPattern(shape,i,points.Length,p,o);
                Check(result.Valid&&result.Id==p.Id&&result.Item==p.Item&&result.Scale==p.Scale,"orientation metadata");
                if(shape is "sphere" or "ball") Check(result==HomeGeometry.FaceOrigin(p,o),"sphere/ball full three dimensional FaceOrigin compatibility");
                else Check(result.Pitch==0&&result.Roll==0,"tilted origin cannot tilt "+shape);
                if(shape is not ("sphere" or "ball" or "cube" or "triangle" or "square" or "pentagon" or "hexagon" or "star" or "rectangle" or "octagon"))
                    YawMatches(result,new(o.X-p.X,0,o.Z-p.Z),o,"horizontal center "+shape);
            }
        }
        foreach(var o in origins)
        foreach(var shape in new[]{"triangle","square","pentagon","hexagon","star","octagon"})
        {
            int sides=shape switch {"triangle"=>3,"square"=>4,"pentagon"=>5,"hexagon"=>6,"octagon"=>8,_=>10};
            int count=sides*7;
            var points=HomeGeometry.Generate(shape,count,1,4,o);
            Vector3 Vertex(int index)
            {
                double a=2*Math.PI*index/sides,r=shape=="star"&&index%2==1?(3-Math.Sqrt(5))/2:1;
                return new((float)(r*Math.Cos(a)),0,(float)(r*Math.Sin(a)));
            }
            for(int edge=0;edge<sides;edge++)
            {
                var a=Vertex(edge); var b=Vertex((edge+1)%sides); var direction=b-a;
                var inward=new Vector3(-direction.Z,0,direction.X);
                Check(Math.Abs(Vector3.Dot(direction,inward))<1e-6,"local edge normal perpendicular");
                // For both regular and concave star edges the polygon interior lies on this side.
                Check(Vector3.Dot(-(a+b)/2,inward)>0,"edge normal points inward");
                float? previous=null;
                for(int offset=0;offset<7;offset++)
                {
                    int i=edge*7+offset; var point=points[i];
                    var result=HomeGeometry.OrientPattern(shape,i,count,item with {X=point.X,Y=point.Y,Z=point.Z},o);
                    YawMatches(result,Rotate(inward,o),o,"polygon edge "+shape);
                    if(previous.HasValue) Check(result.Yaw==previous.Value,"same edge same yaw including first vertex");
                    previous=result.Yaw;
                }
            }
        }
        foreach(var o in origins)
        {
            // Rectangle perimeter 6r: edges have 20,10,20,10 samples, including start vertices.
            var points=HomeGeometry.Generate("rectangle",60,1,4,o);
            Vector3[] normals={new(0,0,-1),new(1,0,0),new(0,0,1),new(-1,0,0)};
            for(int i=0;i<60;i++)
            {
                int edge=i<20?0:i<30?1:i<50?2:3;
                var result=HomeGeometry.OrientPattern("rectangle",i,60,item,o);
                YawMatches(result,Rotate(normals[edge],o),o,"rectangle actual perimeter edge");
                var current=points[i]; var next=points[(i+1)%60];
                Check(Math.Abs(Vector3.Distance(new(current.X,current.Y,current.Z),new(next.X,next.Y,next.Z))-.4)<.00001,"rectangle arc spacing");
            }
            Vector3[] cubeNormals={-Vector3.UnitX,Vector3.UnitX,-Vector3.UnitY,Vector3.UnitY,-Vector3.UnitZ,Vector3.UnitZ};
            for(int face=0;face<6;face++)
            {
                float? previous=null;
                for(int i=face;i<120;i+=6)
                {
                    var result=HomeGeometry.OrientPattern("cube",i,120,item with {X=i,Y=-i,Z=2*i},o);
                    YawMatches(result,Rotate(cubeNormals[face],o),o,"cube face");
                    if(previous.HasValue) Check(result.Yaw==previous.Value,"same cube face same yaw independent of position");
                    previous=result.Yaw;
                }
            }
        }
        foreach(var shape in shapes)
        {
            var o=new EditorOrigin(1,2,3,25,37,48);
            var same=item with {X=1,Y=2,Z=3};
            var result=HomeGeometry.OrientPattern(shape,0,1,same,o);
            if(shape is "sphere" or "ball") Check(result.Pitch==25&&result.Yaw==37&&result.Roll==48,"sphere center full fallback");
            else if(shape is not ("cube" or "triangle" or "square" or "pentagon" or "hexagon" or "star" or "rectangle" or "octagon")) Check(result.Pitch==0&&result.Yaw==37&&result.Roll==0,"upright center fallback");
            Reject(()=>HomeGeometry.OrientPattern(shape,-1,1,item,o)); Reject(()=>HomeGeometry.OrientPattern(shape,1,1,item,o));
            Reject(()=>HomeGeometry.OrientPattern(shape,0,0,item,o)); Reject(()=>HomeGeometry.OrientPattern(shape,0,10001,item,o));
            Reject(()=>HomeGeometry.OrientPattern(shape,0,1,item with {X=float.NaN},o));
            Reject(()=>HomeGeometry.OrientPattern(shape,0,1,item,new(float.PositiveInfinity)));
        }
        Reject(()=>HomeGeometry.OrientPattern("unknown",0,1,item,new()));
        Reject(()=>HomeGeometry.OrientPattern(null!,0,1,item,new())); Reject(()=>HomeGeometry.OrientPattern("line",0,1,null!,new())); Reject(()=>HomeGeometry.OrientPattern("line",0,1,item,null!));
        foreach(var shape in shapes.Skip(16))
        foreach(int count in new[]{1,2,7,101,10000})
        {
            var points=HomeGeometry.Generate(shape,count,.1f,3,new());
            Check(points.Length==count&&points.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)),"new shape count/finite "+shape);
            if(shape is not "doublehelix") Check(points.Distinct().Count()==count,"new shape unique points "+shape);
            var o=new EditorOrigin(1,2,3,22,33,44); var rotated=HomeGeometry.Generate(shape,count,.1f,3,o);
            for(int i=0;i<count;i++)
            {
                var p=points[i]; var expected=Rotate(new(p.X,p.Y,p.Z),o)+new Vector3(1,2,3); var actual=rotated[i];
                Check(Vector3.Distance(new(actual.X,actual.Y,actual.Z),expected)<.0002,"new shape independent rotation "+shape);
                double x=p.X,y=p.Y,z=p.Z;
                if(shape=="ellipse") Check(Math.Abs(x*x/9+z*z/2.25-1)<1e-6,"ellipse surface");
                if(shape=="torus") Check(Math.Abs(Math.Pow(Math.Sqrt(x*x+z*z)-3,2)+y*y-.81)<1e-5,"torus surface");
                if(shape=="capsule") Check(Math.Abs(x*x+z*z+Math.Pow(Math.Max(0,Math.Abs(y)-3),2)-9)<1e-5&&Math.Abs(y)<=6,"capsule surface");
                if(shape=="wave") Check(Math.Abs(z-3*Math.Sin(i*Math.PI/6))<1e-6,"wave sine");
                if(shape=="doublehelix")
                {
                    int branch=i%2,step=i/2,n=(count+1-branch)/2;
                    Check(Math.Abs(y-(step-(n-1)/2.0)*.1)<.0001,"doublehelix branch height");
                    Check(Math.Abs(x-3*Math.Cos(step*Math.PI/6+branch*Math.PI))<1e-6&&Math.Abs(z-3*Math.Sin(step*Math.PI/6+branch*Math.PI))<1e-6,"doublehelix phase");
                }
            }
        }
        foreach(var shape in shapes)
        {
            Check(HomeGeometry.OrientPattern(shape,9999,10000,item,new()).Valid,"inclusive index/count bound "+shape);
            Reject(()=>HomeGeometry.OrientPattern(shape,0,1,item with {Scale=0},new()));
        }
        foreach(var shape in shapes.Skip(16))
        {
            Reject(()=>HomeGeometry.Generate(shape,0,1,1,new()));
            Reject(()=>HomeGeometry.Generate(shape,10001,1,1,new()));
            Reject(()=>HomeGeometry.Generate(shape,1,-1,1,new()));
            Reject(()=>HomeGeometry.Generate(shape,1,1,float.NaN,new()));
            var atLimit=HomeGeometry.Generate(shape,3,0,0,new(100000,-100000,100000));
            Check(atLimit.All(p=>p==(100000f,-100000f,100000f)),"new shape zero-size boundary "+shape);
        }
        // Heart contour equation checked against a separate dense parameter scan, without using its production arc table.
        var heartPoints=HomeGeometry.Generate("heart",101,0,5,new());
        foreach(var p in heartPoints)
        {
            double closest=double.PositiveInfinity;
            for(int j=0;j<16384;j++)
            {
                double t=j*2*Math.PI/16384;
                double x=80*Math.Pow(Math.Sin(t),3)/17,z=5*(13*Math.Cos(t)-5*Math.Cos(2*t)-2*Math.Cos(3*t)-Math.Cos(4*t))/17;
                closest=Math.Min(closest,Math.Sqrt(Math.Pow(p.X-x,2)+Math.Pow(p.Z-z,2)));
            }
            Check(closest<.002,"heart follows classic curve");
        }
        return checks;
    }
}
