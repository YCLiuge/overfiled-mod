using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2CppInutan;
using MelonLoader;
using mod;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HomeEditorModule.Core;

// Each loaded assembly owns its resources; no host runtime or network client is copied.
internal static class EditorRuntime
{
    internal const string Owner = "mod.home_editor.runtime";
    internal static bool Active { get; private set; }
    internal static int Generation { get; private set; }
    internal static ObjectController? Controller;
    private static int scene;
    private static float originalY;
    private static bool captured;
    internal static float? LastY;
    private static readonly List<object> routines = new();
    private static readonly List<UnityEngine.Object> owned = new();
    private static readonly List<State> states = new();
    private static readonly List<(GameObject target, bool active)> visibility = new();
    internal static void RememberVisibility(GameObject target) { if(target != null && !visibility.Any(s => s.target == target)) visibility.Add((target,target.activeSelf)); }
    private static HomeSystem? home;
    internal static string LastError = "";
    internal static string Snapshot() => System.Text.Json.JsonSerializer.Serialize(new { active=Active, generation=Generation, scene, coroutineCount=routines.Count, objectCount=owned.Count, snapshotCount=states.Count, mode=AutoHomeItem.Stute.ToString(), clipboardCount=mod.AutoHomeItem.i.items.Count, cameraSeparated=FkCamera.Active, lastError=LastError });
    internal static void Remember(GameObject obj)
    {
        if (obj == null || states.Any(s => s.Target == obj)) return;
        states.Add(new State(obj));
    }
    internal static void Own(UnityEngine.Object obj) { if (obj != null) owned.Add(obj); }
    internal static bool Ready => Active && SceneManager.GetActiveScene().handle == scene && IHomeSystem.sys == home && TipMsgCore.i.mainplayer != null;
    internal static void Enter()
    {
        if (Active) return;
        if (TipMsgCore.Stute != Stute_tokensys.None) throw new InvalidOperationException("请先 ~stop 退出当前工具模式");
        if (SceneManager.GetActiveScene().name != "Home_Day_001_Modify" || IHomeSystem.sys == null) throw new InvalidOperationException("请先进入家园");
        scene = SceneManager.GetActiveScene().handle; home = IHomeSystem.sys; originalY = EditorBridge.Y;
        captured=true; LastY=null; Generation++; Active = true; LastError = "";
        try { FreeSetHomeItem.i.Start(); } catch { Recover(); throw; }
    }
    internal static void Tick()
    {
        if (!Active) return;
        if (!Ready || TipMsgCore.Stute != Stute_tokensys.None) { Recover(); return; }
        try { FreeSetHomeItem.i.Updata(TipMsgCore.i.mainplayername, ""); }
        catch (Exception ex) { LastError = ex.Message; Recover(); MelonLogger.Error("[HomeEditor] " + ex); }
    }
    internal static void Recover()
    {
        bool restoreY=captured; captured=false;
        Active = false; Generation++;
        foreach (var routine in routines.ToArray()) { try { MelonCoroutines.Stop(routine); } catch (Exception ex) { MelonLogger.Warning(ex.Message); } }
        routines.Clear();
        try { Controller?.Deactivate(); } catch (Exception ex) { MelonLogger.Warning(ex.Message); } Controller = null;
        try { FkCamera.Stop(); } catch (Exception ex) { MelonLogger.Warning(ex.Message); }
        // Detach real furniture before deleting preview pivots or UI helpers.
        for (int i=states.Count-1; i>=0; --i) try { states[i].Restore(); } catch (Exception ex) { MelonLogger.Warning(ex.Message); }
        states.Clear();
        foreach(var entry in visibility) if(entry.target != null) entry.target.SetActive(entry.active); visibility.Clear();
        foreach (var obj in owned) if (obj != null) UnityEngine.Object.Destroy(obj);
        owned.Clear();
        FreeSetHomeItem.i.Recover(); AutoHomeItem.i.Reset(); Fksys.use=false; Followsys.i.Set(null); Followsys.i.pt=null;
        Fk_fanction.Clear(); CenterTMPManager.Reset(); if (restoreY && LastY.HasValue && EditorBridge.Y == LastY.Value) EditorBridge.RestoreY(originalY); LastY=null; home=null;
    }
    internal static void ReleasePivot(GameObject pivot)
    {
        if (pivot == null) return;
        foreach (var state in states) if (state.Target != null && state.Target.transform.parent == pivot.transform) state.Restore();
        if (pivot != null) UnityEngine.Object.Destroy(pivot);
        owned.Remove(pivot);
    }
    internal static object Start(IEnumerator iterator)
    {
        if (!Ready) { (iterator as IDisposable)?.Dispose(); return null; }
        object handle=null; bool done=false; int generation=Generation;
        IEnumerator Run() { try { while(Ready && Generation == generation && iterator.MoveNext()) yield return iterator.Current; } finally { (iterator as IDisposable)?.Dispose(); done=true; if(handle != null) routines.Remove(handle); } }
        handle = MelonCoroutines.Start(Run()); if (!done) routines.Add(handle); return handle;
    }
    internal static void Stop(object handle) { if (handle == null) return; MelonCoroutines.Stop(handle); routines.Remove(handle); }
    private static IEnumerator Guard(IEnumerator iterator, int generation)
    {
        try { while (Ready && Generation == generation && iterator.MoveNext()) yield return iterator.Current; }
        finally { (iterator as IDisposable)?.Dispose(); }
    }
    private sealed class State
    {
        internal readonly GameObject Target; readonly Transform parent; readonly Vector3 position,scale; readonly Quaternion rotation; readonly bool active;
        internal State(GameObject obj) { Target=obj; var t=obj.transform; parent=t.parent; position=t.localPosition; rotation=t.localRotation; scale=t.localScale; active=obj.activeSelf; }
        internal void Restore() { if (Target == null) return; var t=Target.transform; t.SetParent(parent,false); t.localPosition=position; t.localRotation=rotation; t.localScale=scale; Target.SetActive(active); }
    }
}

// Reflect only the two existing public fields on an internal host tool. This works
// with both the pre-migration host and the new host without widening host internals.
internal static class EditorBridge
{
    private static readonly Type Tool = typeof(TipMsgCore).Assembly.GetType("mod.tool.A_maintool",true)!;
    internal static float Y { get => (float)Tool.GetField("Y")!.GetValue(null)!; set { Tool.GetField("Y")!.SetValue(null,value); EditorRuntime.LastY=value; } }
    internal static void RestoreY(float value) => Tool.GetField("Y")!.SetValue(null,value);
    internal static HomeItem itemtemp { set => Tool.GetField("itemtemp")!.SetValue(null,value); }
}
