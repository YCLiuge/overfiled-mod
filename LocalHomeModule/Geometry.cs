namespace LocalHomeModule;

public record EditorOrigin(float X = 0, float Y = 0, float Z = 0, float Pitch = 0, float Yaw = 0, float Roll = 0)
{
    public bool Valid => new[] { X, Y, Z, Pitch, Yaw, Roll }.All(v => float.IsFinite(v) && Math.Abs(v) <= 100000);

    public EditorOrigin Rounded()
    {
        static float Round(float value)
        {
            var rounded = (float)Math.Round((double)value, 2, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0 : rounded;
        }
        return this with { X = Round(X), Y = Round(Y), Z = Round(Z), Pitch = Round(Pitch), Yaw = Round(Yaw), Roll = Round(Roll) };
    }
}

/// <summary>Pure .NET geometry. Angles are degrees, using Unity's extrinsic Z, X, Y Euler order.</summary>
public static class HomeGeometry
{
    private const double Degrees = Math.PI / 180;
    private static readonly double GoldenFraction = (Math.Sqrt(5) - 1) / 2;
    private static readonly double GoldenAngle = Math.PI * (3 - Math.Sqrt(5));
    private static double Fraction(double value) => value - Math.Floor(value);
    private static (double X, double Z) PolygonVertex(int index, int count, double radius, bool star)
    {
        if (star && index % 2 == 1) radius *= (3 - Math.Sqrt(5)) / 2;
        double angle = 2 * Math.PI * index / count;
        return (radius * Math.Cos(angle), radius * Math.Sin(angle));
    }
    private static double SpiralAngle(double fraction)
    {
        static double Arc(double t) => (t * Math.Sqrt(1 + t * t) + Math.Asinh(t)) / 2;
        double low = 0, high = 6 * Math.PI, target = fraction * Arc(high);
        if (fraction == 0) return 0;
        if (fraction == 1) return high;
        for (int j = 0; j < 48; j++)
        {
            double mid = (low + high) / 2;
            if (Arc(mid) < target) low = mid; else high = mid;
        }
        return (low + high) / 2;
    }

    private static bool KnownShape(string shape) => shape is "line" or "circle" or "sphere" or "grid" or "cube" or "disk" or "ball" or "cylinder" or "cone" or "helix" or "spiral" or "triangle" or "square" or "pentagon" or "hexagon" or "star" or "ellipse" or "rectangle" or "octagon" or "torus" or "capsule" or "doublehelix" or "heart" or "wave";
    // Both sampling and orientation use this edge mapping; vertices belong to the next edge.
    private static (double X, double Z, double EndX, double EndZ, double Blend) PatternEdge(string shape,int index,int count)
    {
        if(shape=="rectangle")
        {
            double progress=(double)index*6/count;
            (double X,double Z)[] vertices={ (1,.5),(-1,.5),(-1,-.5),(1,-.5) };
            double[] lengths={2,1,2,1}; int edge=0;
            while(edge<3 && progress>=lengths[edge]) { progress-=lengths[edge]; edge++; }
            var a=vertices[edge]; var b=vertices[(edge+1)%4];
            return(a.X,a.Z,b.X,b.Z,progress/lengths[edge]);
        }
        int sides=shape switch {"triangle"=>3,"square"=>4,"pentagon"=>5,"hexagon"=>6,"octagon"=>8,_=>10};
        double position=(double)index*sides/count; int vertex=(int)position;
        var start=PolygonVertex(vertex,sides,1,shape=="star");
        var end=PolygonVertex((vertex+1)%sides,sides,1,shape=="star");
        return(start.X,start.Z,end.X,end.Z,position-vertex);
    }
    private static (double X,double Z) HeartPoint(double angle) => (16*Math.Pow(Math.Sin(angle),3)/17,(13*Math.Cos(angle)-5*Math.Cos(2*angle)-2*Math.Cos(3*angle)-Math.Cos(4*angle))/17);
    private static readonly double[] HeartArc=BuildHeartArc();
    private static double[] BuildHeartArc()
    {
        var lengths=new double[4097]; var previous=HeartPoint(0);
        for(int i=1;i<lengths.Length;i++)
        {
            var next=HeartPoint(2*Math.PI*i/(lengths.Length-1));
            lengths[i]=lengths[i-1]+Math.Sqrt(Math.Pow(next.X-previous.X,2)+Math.Pow(next.Z-previous.Z,2)); previous=next;
        }
        return lengths;
    }
    private static double HeartAngle(double fraction)
    {
        double target=HeartArc[^1]*fraction; int found=Array.BinarySearch(HeartArc,target);
        if(found>=0) return 2*Math.PI*found/(HeartArc.Length-1);
        int upper=~found; int lower=upper-1;
        double blend=(target-HeartArc[lower])/(HeartArc[upper]-HeartArc[lower]);
        return 2*Math.PI*(lower+blend)/(HeartArc.Length-1);
    }

    public static (float X, float Y, float Z)[] Generate(string shape, int count, float spacing, float radius, EditorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(shape);
        CheckOrigin(origin);
        if (count < 1 || count > 10000) throw new ArgumentOutOfRangeException(nameof(count), "单次生成数量须为 1 到 10000");
        CheckNonnegative(spacing, nameof(spacing));
        CheckNonnegative(radius, nameof(radius));
        if (!KnownShape(shape)) throw new ArgumentException("未知形状", nameof(shape));
        var result = new (float X, float Y, float Z)[count];
        var rotation = Euler(origin.Pitch, origin.Yaw, origin.Roll);
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns;
        for (int i = 0; i < count; i++)
        {
            double x = 0, y = 0, z = 0;
            switch (shape)
            {
                case "line": x = (i - (count - 1) / 2.0) * spacing; break;
                case "circle":
                    double angle = 2 * Math.PI * i / count;
                    x = radius * Math.Cos(angle); z = radius * Math.Sin(angle); break;
                case "sphere":
                    // Half-step latitudes avoid duplicate poles, including the one-point case.
                    double height = 1 - 2 * (i + .5) / count;
                    double ring = Math.Sqrt(Math.Max(0, 1 - height * height));
                    double azimuth = Math.PI * (3 - Math.Sqrt(5)) * i;
                    x = radius * ring * Math.Cos(azimuth); y = radius * height; z = radius * ring * Math.Sin(azimuth); break;
                case "grid":
                    x = (i % columns - (columns - 1) / 2.0) * spacing;
                    z = (i / columns - (rows - 1) / 2.0) * spacing; break;
                case "cube":
                    // Equal populations on six faces. Interior samples avoid shared edges;
                    // stratification and an irrational sweep give exactly count distinct points.
                    int face = i % 6, index = i / 6;
                    int faceCount = (count + 5 - face) / 6;
                    double u = 2 * (index + .5) / faceCount - 1;
                    double v = 2 * Fraction(.5 + index * GoldenFraction) - 1;
                    double side = face % 2 == 0 ? radius : -radius;
                    if (face < 2) { x = side; y = radius * u; z = radius * v; }
                    else if (face < 4) { x = radius * v; y = side; z = radius * u; }
                    else { x = radius * u; y = radius * v; z = side; }
                    break;
                case "disk":
                    double diskRadius = radius * Math.Sqrt((i + .5) / count);
                    double diskAngle = GoldenAngle * i;
                    x = diskRadius * Math.Cos(diskAngle); z = diskRadius * Math.Sin(diskAngle); break;
                case "ball":
                    // Independent low-discrepancy radial quantile gives volume-uniform density.
                    double ballRadius = radius * Math.Cbrt(Fraction(.5 + i * Math.Sqrt(2)));
                    double ballHeight = 1 - 2 * (i + .5) / count;
                    double ballRing = Math.Sqrt(Math.Max(0, 1 - ballHeight * ballHeight));
                    x = ballRadius * ballRing * Math.Cos(GoldenAngle * i);
                    y = ballRadius * ballHeight;
                    z = ballRadius * ballRing * Math.Sin(GoldenAngle * i); break;
                case "cylinder":
                    y = radius * (2 * (i + .5) / count - 1);
                    x = radius * Math.Cos(GoldenAngle * i); z = radius * Math.Sin(GoldenAngle * i); break;
                case "cone":
                    // Radius measured from the apex; sqrt quantiles sample equal side area.
                    double coneT = Math.Sqrt((i + .5) / count);
                    y = radius * (1 - 2 * coneT);
                    x = radius * coneT * Math.Cos(GoldenAngle * i); z = radius * coneT * Math.Sin(GoldenAngle * i); break;
                case "helix":
                    // Twelve samples per revolution; requested spacing is the axial step.
                    double helixAngle = spacing == 0 ? 2 * Math.PI * i / count : i * Math.PI / 6;
                    y = (i - (count - 1) / 2.0) * spacing;
                    x = radius * Math.Cos(helixAngle); z = radius * Math.Sin(helixAngle); break;
                case "spiral":
                    // Three-turn Archimedean spiral sampled at equal arc-length intervals.
                    double spiralAngle = SpiralAngle(count == 1 ? 0 : (double)i / (count - 1));
                    double spiralRadius = radius * spiralAngle / (6 * Math.PI);
                    x = spiralRadius * Math.Cos(spiralAngle); z = spiralRadius * Math.Sin(spiralAngle); break;
                case "triangle": case "square": case "pentagon": case "hexagon": case "star": case "rectangle": case "octagon":
                    var edge=PatternEdge(shape,i,count);
                    x=radius*(edge.X+(edge.EndX-edge.X)*edge.Blend);
                    z=radius*(edge.Z+(edge.EndZ-edge.Z)*edge.Blend); break;
                case "ellipse":
                    double ellipseAngle=2*Math.PI*i/count;
                    x=radius*Math.Cos(ellipseAngle); z=radius*.5*Math.Sin(ellipseAngle); break;
                case "torus":
                    double majorAngle=2*Math.PI*(i+.5)/count, minorAngle=GoldenAngle*i;
                    double torusRing=radius*(1+.3*Math.Cos(minorAngle));
                    x=torusRing*Math.Cos(majorAngle); z=torusRing*Math.Sin(majorAngle); y=radius*.3*Math.Sin(minorAngle); break;
                case "capsule":
                    // Cylinder height 2r plus two hemispheres: area weights 1/2, 1/4, 1/4.
                    double capsuleT=(i+.5)/count;
                    double capsuleRing;
                    if(capsuleT<.25) { double h=4*capsuleT-1; y=radius*(-1+h); capsuleRing=radius*Math.Sqrt(1-h*h); }
                    else if(capsuleT<.75) { y=radius*(4*capsuleT-2); capsuleRing=radius; }
                    else { double h=4*capsuleT-3; y=radius*(1+h); capsuleRing=radius*Math.Sqrt(1-h*h); }
                    x=capsuleRing*Math.Cos(GoldenAngle*i); z=capsuleRing*Math.Sin(GoldenAngle*i); break;
                case "doublehelix":
                    int branch=i%2, branchIndex=i/2, branchCount=(count+1-branch)/2;
                    double doubleAngle=(spacing==0?2*Math.PI*branchIndex/branchCount:branchIndex*Math.PI/6)+branch*Math.PI;
                    y=(branchIndex-(branchCount-1)/2.0)*spacing;
                    x=radius*Math.Cos(doubleAngle); z=radius*Math.Sin(doubleAngle); break;
                case "heart":
                    var heart=HeartPoint(HeartAngle((double)i/count)); x=radius*heart.X; z=radius*heart.Z; break;
                case "wave":
                    x=(i-(count-1)/2.0)*spacing; z=radius*Math.Sin(i*Math.PI/6); break;
            }
            var p = Rotate(rotation, x, y, z);
            result[i] = CheckedPoint(p.X + origin.X, p.Y + origin.Y, p.Z + origin.Z);
        }
        return result;
    }

    /// <summary>Rotate and uniformly scale about origin's position, then translate in world axes.
    /// The incremental world rotation left-multiplies each furniture orientation.
    /// Input precision is retained until LayoutCodec.Validate rounds the output.</summary>
    public static Placement[] Transform(Placement[] items, EditorOrigin origin, float dx, float dy, float dz, float rx, float ry, float rz, float scale)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckOrigin(origin);
        CheckBounded(dx, nameof(dx)); CheckBounded(dy, nameof(dy)); CheckBounded(dz, nameof(dz));
        CheckBounded(rx, nameof(rx)); CheckBounded(ry, nameof(ry)); CheckBounded(rz, nameof(rz));
        if (!float.IsFinite(scale) || scale < .01f || scale > 100) throw new ArgumentOutOfRangeException(nameof(scale));
        // Validate without replacing the source with its rounded copy before doing geometry.
        LayoutCodec.Validate(items);
        var delta = Euler(rx, ry, rz);
        bool preserveAngles = rx % 360 == 0 && ry % 360 == 0 && rz % 360 == 0;
        var result = new Placement[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var p = Rotate(delta, ((double)item.X - origin.X) * scale, ((double)item.Y - origin.Y) * scale, ((double)item.Z - origin.Z) * scale);
            var point = CheckedPoint(p.X + origin.X + dx, p.Y + origin.Y + dy, p.Z + origin.Z + dz);
            // Equivalent zero rotations must not canonicalize saved Euler values.
            var angles = preserveAngles
                ? (X: item.Pitch, Y: item.Yaw, Z: item.Roll)
                : ToEuler(Multiply(delta, Euler(item.Pitch, item.Yaw, item.Roll)));
            result[i] = item with { X = point.X, Y = point.Y, Z = point.Z, Pitch = angles.X, Yaw = angles.Y, Roll = angles.Z, Scale = item.Scale * scale };
        }
        return LayoutCodec.Validate(result);
    }

    /// <summary>Point local +Z at the origin using world-space positions, without composing
    /// the origin rotation. Within 1e-6 world units, use the complete origin orientation.</summary>
    public static Placement FaceOrigin(Placement item, EditorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(item);
        CheckOrigin(origin);
        // Reject raw invalid data before rounding; preserve identity, position and scale.
        if (!item.Valid) throw new ArgumentException("家具数据无效", nameof(item));
        double x = (double)origin.X - item.X, y = (double)origin.Y - item.Y, z = (double)origin.Z - item.Z;
        double horizontal = Math.Sqrt(x * x + z * z);
        Placement result;
        if (horizontal * horizontal + y * y <= 1e-12)
            result = item with { Pitch = origin.Pitch, Yaw = origin.Yaw, Roll = origin.Roll };
        else
            result = item with
            {
                Yaw = horizontal <= 1e-6 ? origin.Yaw : (float)(Math.Atan2(x, z) / Degrees),
                Pitch = (float)(-Math.Atan2(y, horizontal) / Degrees),
                Roll = 0
            };
        return result.Rounded();
    }

    /// <summary>Only spheres use full 3D inward orientation. Other shapes remain upright;
    /// polygon vertices use the following edge, cube points use their generated face.</summary>
    public static Placement OrientPattern(string shape,int index,int count,Placement item,EditorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(shape); ArgumentNullException.ThrowIfNull(item); CheckOrigin(origin);
        if(!KnownShape(shape)) throw new ArgumentException("未知形状",nameof(shape));
        if(count<1 || count>10000) throw new ArgumentOutOfRangeException(nameof(count));
        if(index<0 || index>=count) throw new ArgumentOutOfRangeException(nameof(index));
        if(!item.Valid) throw new ArgumentException("家具数据无效",nameof(item));
        if(shape is "sphere" or "ball") return FaceOrigin(item,origin);
        double x=(double)origin.X-item.X,z=(double)origin.Z-item.Z;
        if(shape=="cube" || shape is "triangle" or "square" or "pentagon" or "hexagon" or "star" or "rectangle" or "octagon")
        {
            (double X,double Y,double Z) normal;
            if(shape=="cube") normal=(index%6) switch {0=>(-1,0,0),1=>(1,0,0),2=>(0,-1,0),3=>(0,1,0),4=>(0,0,-1),_=>(0,0,1)};
            else { var edge=PatternEdge(shape,index,count); normal=(-(edge.EndZ-edge.Z),0,edge.EndX-edge.X); }
            double magnitude=Math.Sqrt(normal.X*normal.X+normal.Y*normal.Y+normal.Z*normal.Z);
            var world=Rotate(Euler(origin.Pitch,origin.Yaw,origin.Roll),normal.X/magnitude,normal.Y/magnitude,normal.Z/magnitude);
            x=world.X; z=world.Z;
        }
        float yaw=x*x+z*z<=1e-12?origin.Yaw:(float)(Math.Atan2(x,z)/Degrees);
        return (item with {Pitch=0,Yaw=yaw,Roll=0}).Rounded();
    }

    private static void CheckOrigin(EditorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.Valid) throw new ArgumentOutOfRangeException(nameof(origin));
    }
    private static void CheckBounded(float value, string name)
    {
        if (!float.IsFinite(value) || Math.Abs(value) > 100000) throw new ArgumentOutOfRangeException(name);
    }
    private static void CheckNonnegative(float value, string name)
    {
        CheckBounded(value, name);
        if (value < 0) throw new ArgumentOutOfRangeException(name);
    }
    private static (float X, float Y, float Z) CheckedPoint(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000 || Math.Abs(z) > 100000)
            throw new ArgumentOutOfRangeException("position", "生成或变换后的坐标超出有效范围");
        return ((float)x, (float)y, (float)z);
    }

    // Double precision protects Euler extraction around gimbal lock and large pivots.
    private readonly record struct Q(double X, double Y, double Z, double W);
    private static Q Multiply(Q a, Q b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    private static Q Euler(double pitch, double yaw, double roll)
    {
        double x = Math.IEEERemainder(pitch, 360) * Degrees / 2;
        double y = Math.IEEERemainder(yaw, 360) * Degrees / 2;
        double z = Math.IEEERemainder(roll, 360) * Degrees / 2;
        return Multiply(Multiply(new Q(0, Math.Sin(y), 0, Math.Cos(y)), new Q(Math.Sin(x), 0, 0, Math.Cos(x))), new Q(0, 0, Math.Sin(z), Math.Cos(z)));
    }
    private static (double X, double Y, double Z) Rotate(Q q, double x, double y, double z) => (
        (1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * x + 2 * (q.X * q.Y - q.Z * q.W) * y + 2 * (q.X * q.Z + q.Y * q.W) * z,
        2 * (q.X * q.Y + q.Z * q.W) * x + (1 - 2 * (q.X * q.X + q.Z * q.Z)) * y + 2 * (q.Y * q.Z - q.X * q.W) * z,
        2 * (q.X * q.Z - q.Y * q.W) * x + 2 * (q.Y * q.Z + q.X * q.W) * y + (1 - 2 * (q.X * q.X + q.Y * q.Y)) * z);
    private static (float X, float Y, float Z) ToEuler(Q q)
    {
        var right = Rotate(q, 1, 0, 0);
        var up = Rotate(q, 0, 1, 0);
        var forward = Rotate(q, 0, 0, 1);
        double pitch = Math.Asin(Math.Clamp(-forward.Y, -1, 1));
        double yaw, roll;
        if (Math.Abs(Math.Cos(pitch)) > 1e-7)
        {
            yaw = Math.Atan2(forward.X, forward.Z);
            roll = Math.Atan2(right.Y, up.Y);
        }
        else
        {
            // At +/-90 degrees pitch, yaw and roll share a degree of freedom.
            yaw = Math.Atan2(-right.Z, right.X);
            roll = 0;
        }
        return ((float)(pitch / Degrees), (float)(yaw / Degrees), (float)(roll / Degrees));
    }
}
