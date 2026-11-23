using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

string root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");
if (args.Contains("--live")) { await LiveHomeCheck.Run(root); return; }
string dist = Path.Combine(root, "GameMods", "Mods");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(root, "libs"));
resolver.AddSearchDirectory(dist);
var parameters = new ReaderParameters { AssemblyResolver = resolver };
using var host = AssemblyDefinition.ReadAssembly(Path.Combine(dist, "mod.dll"), parameters);
using var module = AssemblyDefinition.ReadAssembly(Path.Combine(dist, "HomeEditorModule.dll"), parameters);
int count = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException("HOME FAIL: " + message);
    count++; Console.WriteLine("HOME PASS: " + message);
}
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
var types = Flatten(module.MainModule.Types).ToArray();
Check(module.Name.Name == "HomeEditorModule", "independent home editor assembly");
Check(types.Any(t => t.Interfaces.Any(i => i.InterfaceType.FullName == "ModLoader.ILogic")), "entry implements host hot-load contract");
foreach (string name in new[] { "FreeSetHomeItem", "Fksys" })
{
    Check(types.Any(t => t.Name == name && t.Methods.Any(m => m.HasBody && m.Body.Instructions.Count > 20)), "module contains editor implementation: " + name);
    Check(!host.MainModule.Types.Any(t => t.Name == name), "editor implementation removed from host: " + name);
}
Check(!host.MainModule.AssemblyReferences.Any(r => r.Name == "HomeEditorModule"), "host has no hard dependency on optional editor DLL");
Check(!types.Any(t => t.FullName == "Chat.Client.ChatClient"), "module does not embed a separate network client");
var clipboard = host.MainModule.Types.Single(t => t.FullName == "mod.AutoHomeItem");
Check(clipboard.Fields.Any(f => f.Name == "items" && f.IsPublic && f.FieldType.FullName == "System.Collections.Generic.List`1<mod.Itempot>") &&
    clipboard.Fields.Any(f => f.Name == "i" && f.IsPublic && f.IsStatic), "shared clipboard preserves public field signatures for existing tools");
Check(host.MainModule.Types.Any(t => t.FullName == "mod.Itempot") && !types.Any(t => t.FullName == "mod.Itempot"), "item DTO remains owned by host");
Check(clipboard.Methods.Count(m => m.Name == "GetCenterAverage" && m.IsPublic) == 2 && clipboard.Methods.Any(m => m.Name == "Copyitems" && m.IsPublic), "boat clipboard helper APIs remain available");
var refs = types.SelectMany(t => t.Methods).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MemberReference>()
    .Where(r => r.DeclaringType?.Scope is AssemblyNameReference a && a.Name == "mod")
    .GroupBy(r => r.FullName).Select(g => g.First()).ToArray();
foreach (var reference in refs)
{
    try
    {
        IMemberDefinition? resolved = reference switch
        {
            MethodReference m => m.Resolve(),
            FieldReference f => f.Resolve(),
            TypeReference t => t.Resolve(),
            _ => null
        };
        if (resolved == null) throw new MissingMemberException(reference.FullName);
    }
    catch (Exception e) { throw new InvalidOperationException("Unresolved host API: " + reference.FullName, e); }
}
Check(refs.Length > 0, "all " + refs.Length + " referenced host members resolve against published host");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dist, "version.json")));
var entry = manifest.RootElement.GetProperty("modules").EnumerateArray().Single(x => x.GetProperty("url").GetString() == "HomeEditorModule.dll");
Check(entry.GetProperty("enabled").GetBoolean(), "home editor is enabled in update manifest");
string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dist, "HomeEditorModule.dll"))));
Check(string.Equals(expected, entry.GetProperty("hash").GetString(), StringComparison.OrdinalIgnoreCase), "manifest SHA256 matches published DLL");
Console.WriteLine($"HOME EDITOR METADATA: {count} checks passed");
