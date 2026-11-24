using Mono.Cecil;
using Iced.Intel;
var root = @"D:\TapTap\PC Games\176228\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out";
var symbols = new Dictionary<ulong,string>();
var methods = new List<(MethodDefinition m,ulong rva,int offset,int length)>();
void Visit(TypeDefinition t) {
 foreach(var m in t.Methods) {
 var at=m.CustomAttributes.FirstOrDefault(a=>a.AttributeType.Name=="AddressAttribute");
 if(at==null) continue;
 var f=at.Fields.ToDictionary(a=>a.Name,a=>Convert.ToUInt64((string)a.Argument.Value,16));
 symbols[f["RVA"]]=m.FullName;
 methods.Add((m,f["RVA"],(int)f["Offset"],(int)f["Length"]));
 }
 foreach(var n in t.NestedTypes) Visit(n);
}
foreach(var file in new[]{"Assembly-CSharp.dll","YooAsset.dll"}) foreach(var t in AssemblyDefinition.ReadAssembly(Path.Combine(root,file)).MainModule.Types) Visit(t);
var bytes=File.ReadAllBytes(@"D:\TapTap\PC Games\176228\GameAssembly.dll");
foreach(var entry in methods.Where(e=>args.Any(a=>e.m.FullName.Contains(a)))) {
 Console.WriteLine($"\n{entry.m.FullName} RVA={entry.rva:X}");
 var decoder=Decoder.Create(64,new ByteArrayCodeReader(bytes.Skip(entry.offset).Take(entry.length).ToArray())); decoder.IP=entry.rva;
 while(decoder.IP<entry.rva+(ulong)entry.length) { decoder.Decode(out var i); var label=symbols.GetValueOrDefault(i.NearBranchTarget,""); Console.WriteLine($"{i.IP:X}: {i} {label}"); }
}
