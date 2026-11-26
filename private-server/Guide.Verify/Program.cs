using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using OverField.PrivateServer;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var metadataPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root,"libs/Assembly-CSharp.dll");
Console.WriteLine("METADATA " + metadataPath);
using var assembly = AssemblyDefinition.ReadAssembly(metadataPath);
var types = All(assembly.MainModule.Types).ToDictionary(t=>t.FullName);
var validated = new List<MethodDefinition>();
foreach(var target in DisableGuides.Targets) {
 if(!types.TryGetValue(target.Type,out var type)) throw new Exception("Missing type "+target.Type);
 var matches=type.Methods.Where(m=>m.Name==target.Method && m.Parameters.Count==target.Arity && m.ReturnType.FullName=="System.Void").ToArray();
 if(matches.Length!=1)throw new Exception($"Target is ambiguous/missing: {target}: {matches.Length}");
 validated.Add(matches[0]); Console.WriteLine("METADATA PASS "+matches[0].FullName);
}
var riddles=types["Il2CppInutan.RiddleSystem"].Methods.Where(m=>m.Name=="OpenRiddleTutorial"&&m.Parameters.Count==1&&m.ReturnType.FullName=="System.Void").ToArray();
if(riddles.Length!=2||!riddles.Select(m=>m.Parameters[0].ParameterType.FullName).Order().SequenceEqual(new[]{"Il2CppProtocol.RiddleConfigure","System.Int32"}))throw new Exception("Riddle overload contract changed");
validated.AddRange(riddles);
var quest=types["Il2CppInutan.QuestSystem"].Methods.Single(m=>m.Name=="QuestFinishGuide");
if(!quest.IsStatic || quest.ReturnType.FullName!="Il2Cpp.Callback" || !quest.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"Il2CppInutan.QuestData","Il2Cpp.Callback"}))throw new Exception("Quest continuation contract changed");
validated.Add(quest);
var builder=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("GuideVerificationTargets"),AssemblyBuilderAccess.Run);
var module=builder.DefineDynamicModule("Main");
foreach(var group in validated.GroupBy(m=>m.DeclaringType.FullName)) {
 var tb=module.DefineType(group.Key, System.Reflection.TypeAttributes.Public|System.Reflection.TypeAttributes.Class);
 foreach(var method in group) {
  var returnType=method.ReturnType.FullName=="Il2Cpp.Callback"?typeof(Il2Cpp.Callback):typeof(void);
  var mb=tb.DefineMethod(method.Name,System.Reflection.MethodAttributes.Public|(method.IsStatic?System.Reflection.MethodAttributes.Static:0),returnType,method.Parameters.Select(p=>p.ParameterType.FullName=="System.Int32"?typeof(int):p.ParameterType.FullName=="Il2Cpp.Callback"?typeof(Il2Cpp.Callback):typeof(object)).ToArray());
  if(returnType!=typeof(void))mb.GetILGenerator().Emit(OpCodes.Ldnull);
  mb.GetILGenerator().Emit(OpCodes.Ret);
 }
 tb.CreateType();
}
DisableGuides.Install(new HarmonyLib.Harmony());
if(HarmonyLib.Harmony.Hooks.Count!=validated.Count)throw new Exception("Hook count mismatch");
foreach(var hook in HarmonyLib.Harmony.Hooks) {
 if(hook.Prefix==null)throw new Exception("Missing prefix");
 var parameters=hook.Prefix.Method.GetParameters();
 if(parameters.Length==0) {
  if(hook.Prefix.Method.Invoke(null,null) is not false)throw new Exception("Prefix did not suppress native method "+hook.Target);
 } else {
  if(hook.Target.Name!="QuestFinishGuide")throw new Exception("Unexpected argument-aware prefix");
  var callback=new Il2Cpp.Callback(); object?[] hookArgs={new object[]{new object(),callback},null};
  if(hook.Prefix.Method.Invoke(null,hookArgs) is not false || !ReferenceEquals(hookArgs[1],callback) || callback.Calls!=0)throw new Exception("Continuation was lost, wrapped or invoked early");
  object?[] nullArgs={new object?[]{new object(),null},new object()};
  if(hook.Prefix.Method.Invoke(null,nullArgs) is not false || nullArgs[1]!=null)throw new Exception("Null continuation not preserved");
  callback.Invoke();if(callback.Calls!=1)throw new Exception("Caller could not invoke original continuation once");
  Console.WriteLine("CONTINUATION PASS same reference, deferred invocation, null preserved");
 }
 Console.WriteLine("PREFIX PASS "+hook.Target.DeclaringType!.FullName+"."+hook.Target.Name+"/"+hook.Target.GetParameters().Length);
}
Console.WriteLine($"PASS: {validated.Count} real DLL targets, {HarmonyLib.Harmony.Hooks.Count} linked production prefixes; no game/native assembly loaded.");
static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}

namespace Il2Cpp { public sealed class Callback { public int Calls; public void Invoke()=>Calls++; } }
namespace HarmonyLib {
 public sealed class HarmonyMethod(Type type,string name) { public MethodInfo Method => type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!; }
 public sealed class Harmony {
  public sealed record Hook(MethodInfo Target,HarmonyMethod? Prefix);
  public static readonly List<Hook> Hooks=new();
  public void Patch(MethodInfo original,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null)=>Hooks.Add(new(original,prefix));
 }
}
namespace MelonLoader { public static class MelonLogger {public static void Msg(string message)=>Console.WriteLine(message);public static void Warning(string message)=>Console.WriteLine(message);public static void Error(string message)=>Console.WriteLine(message);} }
