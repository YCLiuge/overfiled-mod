using System.Text.Json;
using LocalHomeModule;

var suite=new EditorSuite();
return suite.Run();

internal sealed class EditorSuite
{
    private int passed, failed;
    private readonly string root=Path.Combine(Path.GetTempPath(),"HomeEditorVerification-"+Guid.NewGuid().ToString("N"));
    private HomeRuntime New() => new(Path.Combine(root,Guid.NewGuid().ToString("N"),"layout.json"));
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private void Test(string name,Action body)
    {
        try { mod.TipMsgCore.i.mainplayer=null; body(); passed++; Console.WriteLine("PASS "+name); }
        catch(Exception ex) { failed++; Console.WriteLine("FAIL "+name+": "+ex); }
    }
    private static void Throws(Action action)
    {
        try { action(); } catch(Exception) { return; }
        throw new Exception("expected rejection");
    }
    private static void Unchanged(HomeRuntime h,Action action)
    {
        string state=h.StateForTest(),history=h.HistoryForTest();
        int dirty=h.DirtyCalls;
        byte[]? file=File.Exists(h.SavePath)?File.ReadAllBytes(h.SavePath):null;
        Throws(action);
        Check(h.StateForTest()==state,"memory changed after rejection");
        Check(h.HistoryForTest()==history,"history changed after rejection");
        Check(h.DirtyCalls==dirty,"sync was dirtied after rejection");
        Check(file==null?!File.Exists(h.SavePath):File.ReadAllBytes(h.SavePath).SequenceEqual(file),"save changed after rejection");
    }
    internal int Run()
    {
        Directory.CreateDirectory(root);
        Test("generation is one undo/redo including generated group",()=> {
            var h=New(); var empty=h.StateForTest(); var ids=h.GeneratePattern("line",12,5,2,4);
            Check(ids.SequenceEqual(new[]{1,2,3,4,5}),"generated IDs");
            Check(h.UndoCount==1&&h.RedoCount==0&&h.Items.Length==5,"generation must be one transaction");
            Check(h.LastGroup.SequenceEqual(ids),"generated group"); var generated=h.StateForTest();
            h.Undo(); Check(h.StateForTest()==empty&&h.RedoCount==1,"undo entire generation");
            h.Redo(); Check(h.StateForTest()==generated&&h.UndoCount==1,"redo generation and group");
        });
        Test("origin edits are one undo/redo without moving furniture or syncing",()=> {
            var h=New(); h.GeneratePattern("line",12,3,2,3); var items=h.Items; int dirty=h.DirtyCalls;
            var before=h.StateForTest(); h.SetOrigin(new(10,20,30,15,45,5)); var after=h.StateForTest();
            Check(h.Items.SequenceEqual(items)&&h.DirtyCalls==dirty,"origin edit moved/synced items");
            Check(h.UndoCount==2,"origin not one transaction"); h.Undo(); Check(h.StateForTest()==before,"origin undo");
            h.Redo(); Check(h.StateForTest()==after&&h.DirtyCalls==dirty,"origin redo");
        });
        Test("group transform is one undo/redo and moves origin when requested",()=> {
            var h=New(); h.SetOrigin(new(10,2,5)); var ids=h.GeneratePattern("circle",12,6,1,4);
            var before=h.StateForTest(); int depth=h.UndoCount;
            h.TransformGroup(ids,3,4,5,20,30,40,2,true); var after=h.StateForTest();
            Check(h.UndoCount==depth+1&&h.Origin==new EditorOrigin(13,6,10),"group transaction/origin");
            Check(h.Items.All(p=>p.Scale==2),"group scale"); h.Undo(); Check(h.StateForTest()==before,"group undo");
            h.Redo(); Check(h.StateForTest()==after,"group redo");
        });
        Test("second generation restores previous generated group on undo",()=> {
            var h=New(); var first=h.GeneratePattern("grid",12,4,1,1); var before=h.StateForTest();
            var second=h.GeneratePattern("sphere",13,3,1,2); var after=h.StateForTest();
            Check(!first.Intersect(second).Any()&&h.LastGroup.SequenceEqual(second),"second group");
            h.Undo(); Check(h.StateForTest()==before&&h.LastGroup.SequenceEqual(first),"restore former group");
            h.Redo(); Check(h.StateForTest()==after&&h.LastGroup.SequenceEqual(second),"restore latter group");
        });
        Test("save/reload includes version, origin, group and scaled items",()=> {
            var h=New(); h.SetOrigin(new(2.345f,4,6,5,30,2)); var ids=h.GeneratePattern("grid",12,4,2,1);
            h.TransformGroup(ids,1,2,3,4,5,6,1.5f,false); h.SaveForTest();
            using var json=JsonDocument.Parse(File.ReadAllText(h.SavePath)); var obj=json.RootElement;
            Check(obj.GetProperty("Version").GetInt32()==2&&obj.GetProperty("Group").GetArrayLength()==4,"save envelope");
            var loaded=new HomeRuntime(h.SavePath); loaded.LoadForTest();
            Check(loaded.StateForTest()==h.StateForTest()&&loaded.UndoCount==0&&!loaded.CanRedo,"load fidelity/history");
        });
        Test("legacy array load is read-only and next save migrates",()=> {
            var h=New(); Directory.CreateDirectory(Path.GetDirectoryName(h.SavePath)!);
            const string old="[{\"Id\":7,\"Item\":12,\"X\":1,\"Y\":2,\"Z\":3,\"Yaw\":45}]";
            File.WriteAllText(h.SavePath,old); mod.TipMsgCore.i.mainplayer=new();
            mod.TipMsgCore.i.mainplayer.transform.position=new(5,6,7); mod.TipMsgCore.i.mainplayer.transform.eulerAngles=new(0,90,0);
            h.LoadForTest(); Check(File.ReadAllText(h.SavePath)==old,"load rewrote legacy save");
            Check(h.Items.Single().Scale==1&&h.Origin==new EditorOrigin(5,6,7,0,90,0)&&h.LastGroup.Length==0,"legacy defaults");
            h.SaveForTest(); var loaded=new HomeRuntime(h.SavePath); loaded.LoadForTest();
            Check(loaded.StateForTest()==h.StateForTest(),"migrated save lost state");
        });
        Test("invalid input preserves memory, history, redo, file and sync",()=> {
            var h=New(); var ids=h.GeneratePattern("line",12,3,1,1); h.SetOrigin(new(2,3,4)); h.Undo();
            Action[] bad={ ()=>h.SetOrigin(new(float.NaN)),()=>h.SetOrigin(new(100001)),
                ()=>h.GeneratePattern("unknown",12,2,1,1),()=>h.GeneratePattern("line",0,2,1,1),
                ()=>h.GeneratePattern("line",12,0,1,1),()=>h.GeneratePattern("grid",12,10001,1,1),
                ()=>h.GeneratePattern("line",12,2,-1,1),()=>h.GeneratePattern("circle",12,2,1,float.PositiveInfinity),
                ()=>h.TransformGroup(Array.Empty<int>(),0,0,0,0,0,0,1,false),
                ()=>h.TransformGroup(new[]{999},0,0,0,0,0,0,1,false),
                ()=>h.TransformGroup(ids,float.NaN,0,0,0,0,0,1,false),
                ()=>h.TransformGroup(ids,0,0,0,0,0,0,0,false),
                ()=>h.ChangeForTest(d=>d[1]=d[1] with {Scale=101}) };
            foreach(var action in bad) Unchanged(h,action);
        });
        Test("save failure preserves generation, origin, group-transform and undo/redo history",()=> {
            var h=New(); var ids=h.GeneratePattern("line",12,3,1,1); h.SetOrigin(new(2)); h.Undo();
            // A directory at the temp-file path fails before replacing the real save.
            Directory.CreateDirectory(h.SavePath+".tmp");
            Unchanged(h,()=>h.GeneratePattern("circle",12,3,1,2));
            Unchanged(h,()=>h.SetOrigin(new(8)));
            Unchanged(h,()=>h.TransformGroup(ids,1,0,0,0,20,0,1,false));
            Unchanged(h,()=>h.Undo()); Unchanged(h,()=>h.Redo());
        });
        Test("no-op change preserves redo and does not touch disk",()=> {
            var h=New(); h.GeneratePattern("line",12,3,1,1); h.SetOrigin(new(1)); h.Undo();
            string state=h.StateForTest(),history=h.HistoryForTest(); byte[] saved=File.ReadAllBytes(h.SavePath); int dirty=h.DirtyCalls;
            // Deliberately block saving: a no-op must never reach SaveEditor.
            Directory.CreateDirectory(h.SavePath+".tmp");
            h.ChangeForTest(d=>{}); h.ChangeForTest(d=>d[1]=d[1] with { X=d[1].X+.001f }); h.SetOrigin(h.Origin);
            Check(h.StateForTest()==state&&h.HistoryForTest()==history&&h.CanRedo,"no-op destroyed redo");
            Check(File.ReadAllBytes(h.SavePath).SequenceEqual(saved)&&h.DirtyCalls==dirty,"no-op saved/synced");
        });
        Test("history keeps last 50 operations and redo is bounded",()=> {
            var h=New(); for(int i=1;i<=65;i++) h.SetOrigin(new(i));
            Check(h.UndoCount==50,"undo cap"); for(int i=0;i<50;i++) h.Undo();
            Check(h.Origin.X==15&&h.UndoCount==0&&h.RedoCount==50,"oldest retained history");
            Unchanged(h,()=>h.Undo()); for(int i=0;i<50;i++) h.Redo();
            Check(h.Origin.X==65&&h.UndoCount==50&&h.RedoCount==0,"redo fidelity/cap");
            Unchanged(h,()=>h.Redo());
        });
        Test("origin at player/furniture copies full pose and is undoable",()=> {
            var h=New(); h.SetOrigin(new(0,0,0,10,20,30)); h.GeneratePattern("line",12,1,1,1);
            mod.TipMsgCore.i.mainplayer=new(); mod.TipMsgCore.i.mainplayer.transform.position=new(4,5,6);
            mod.TipMsgCore.i.mainplayer.transform.eulerAngles=new(40,50,60);
            var before=h.StateForTest(); h.OriginAtPlayer(); Check(h.Origin==new EditorOrigin(4,5,6,40,50,60),"player origin");
            h.Undo(); Check(h.StateForTest()==before,"player-origin undo"); h.OriginAtFurniture(1);
            var p=h.Items.Single(); Check(h.Origin==new EditorOrigin(p.X,p.Y,p.Z,p.Pitch,p.Yaw,p.Roll),"furniture origin");
            Unchanged(h,()=>h.OriginAtFurniture(999));
        });
        Test("disabled or unavailable editor rejects modifications",()=> {
            var h=New(); h.GeneratePattern("line",12,1,1,1); h.EnableForTest(false);
            Check(!h.CanUndo&&!h.CanRedo,"disabled history controls"); Unchanged(h,()=>h.SetOrigin(new(2))); Unchanged(h,()=>h.Undo());
            h.EnableForTest(true); h.Available=false; Unchanged(h,()=>h.GeneratePattern("line",12,2,1,1));
        });
        Test("origin nudge is one undo and keeps furniture unchanged",()=> {
            var h=New(); h.GeneratePattern("circle",12,6,1,3); h.SetOrigin(new(1,2,3,10,20,30));
            var items=h.Items; var before=h.StateForTest(); int dirty=h.DirtyCalls, depth=h.UndoCount;
            h.MoveOrigin(.5f,-2,3); var after=h.StateForTest();
            Check(h.Origin==new EditorOrigin(1.5f,0,6,10,20,30)&&h.Items.SequenceEqual(items),"nudge changed furniture/angles");
            Check(h.DirtyCalls==dirty&&h.UndoCount==depth+1,"nudge history/network");
            h.Undo(); Check(h.StateForTest()==before,"nudge undo"); h.Redo(); Check(h.StateForTest()==after,"nudge redo");
            Unchanged(h,()=>h.MoveOrigin(float.NaN,0,0)); Unchanged(h,()=>h.MoveOrigin(100001,0,0));
        });
        Test("multi-delete atomically restores group and invalidates stale selection",()=> {
            var h=New(); var ids=h.GeneratePattern("grid",12,6,1,1); var before=h.StateForTest();
            long epoch=h.SelectionEpochForTest; int depth=h.UndoCount;
            h.DeleteGroup(new[]{ids[0],ids[2],ids[0]});
            Check(h.Items.Length==4&&h.LastGroup.Length==4&&h.UndoCount==depth+1,"multi delete atomic");
            Check(h.SelectionEpochForTest>epoch,"removed IDs did not invalidate selection");
            h.Undo(); Check(h.StateForTest()==before,"delete undo did not restore group");
            Unchanged(h,()=>h.DeleteGroup(new[]{ids[0],999})); Unchanged(h,()=>h.DeleteGroup(Array.Empty<int>()));
            Directory.CreateDirectory(h.SavePath+".tmp"); Unchanged(h,()=>h.DeleteGroup(ids)); Unchanged(h,()=>h.MoveOrigin(1,0,0));
        });
        Test("24 shapes persist sphere 3D, edge-face normals and upright other furniture",()=> {
            foreach(var shape in new[]{"line","circle","sphere","grid","cube","disk","ball","cylinder","cone","helix","spiral","triangle","square","pentagon","hexagon","star","ellipse","rectangle","octagon","torus","capsule","doublehelix","heart","wave"})
            {
                var h=New(); h.SetOrigin(new(12.345f,-2.225f,7.885f,35,65,12)); h.GeneratePattern(shape,12,48,.87f,3.25f);
                var generated=h.StateForTest(); h.Undo(); h.Redo(); Check(h.StateForTest()==generated,"generated orientation undo/redo "+shape);
                foreach(var p in h.Items)
                {
                    bool sphere=shape is "sphere" or "ball";
                    if(!sphere) Check(p.Pitch==0&&p.Roll==0,"non-sphere tilted "+shape);
                    if(shape is "cube" or "triangle" or "square" or "pentagon" or "hexagon" or "star" or "rectangle" or "octagon") continue;
                    double dx=h.Origin.X-p.X,dy=sphere?h.Origin.Y-p.Y:0,dz=h.Origin.Z-p.Z,length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                    if(length<1e-6) { Check(p.Yaw==h.Origin.Yaw,"center fallback"); continue; }
                    double pitch=p.Pitch*Math.PI/180,yaw=p.Yaw*Math.PI/180;
                    double dot=(Math.Sin(yaw)*Math.Cos(pitch)*dx-Math.Sin(pitch)*dy+Math.Cos(yaw)*Math.Cos(pitch)*dz)/length;
                    Check(dot>.9999999&&p.Roll==0,"wrong inward orientation "+shape);
                }
                var loaded=new HomeRuntime(h.SavePath); loaded.LoadForTest(); Check(loaded.StateForTest()==generated,"orientation save/load "+shape);
                if(shape=="cube")
                    for(int face=0;face<6;face++) Check(h.Items.Where((p,i)=>i%6==face).Select(p=>p.Yaw).Distinct().Count()==1,"same face headings differ");
                if(shape=="triangle")
                    for(int edge=0;edge<3;edge++) Check(h.Items.Skip(edge*16).Take(16).Select(p=>p.Yaw).Distinct().Count()==1,"same edge headings differ");
            }
        });
        Console.WriteLine($"HomeEditorVerification: {passed}/{passed+failed} passed; {failed} failed. Fixtures: {root}");
        return failed==0?0:1;
    }
}
