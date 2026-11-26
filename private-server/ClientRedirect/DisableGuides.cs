using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace OverField.PrivateServer;

/// <summary>Suppress tutorial producers and their UI, leaving normal panels and gameplay intact.</summary>
internal static class DisableGuides
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // Validate the complete contract before installing any hook.
    internal static readonly (string Type, string Method, int Arity)[] Targets = {
        ("Il2CppInutan.BeginnerGuideSystem", "OnUpdateItem", 1),
        ("Il2CppInutan.BeginnerGuideSystem", "UpdateQuestState", 1),
        ("Il2CppInutan.UIBeginnerGuideManager", "Open", 0),
        ("Il2CppInutan.UIBeginnerGuideSystem", "Open", 0),
        ("Il2Cpp.UIGuideSystem", "CreateGuideModule", 5),
        ("Il2CppInutan.UIGuideCreater", "CreateGuide", 0),
        ("Il2CppInutan.UIGuideCreater", "CreateGuide", 3),
        ("Il2Cpp.ChallengeController", "OpenChallengeTutorial", 0),
        ("Il2Cpp.RiddleController", "OpenRiddleTutorial", 0),
        ("Il2CppInutan.ChallengeSystem", "TryOpenChallengeTutorialWhenFirstPlay", 1),
        ("Il2CppInutan.ChallengeSystem", "OpenChallengeTutorial", 1),
        ("Il2CppInutan.RiddleSystem", "TryOpenRiddleTutorialWhenFirstPlay", 1),
        ("Il2CppInutan.QuestGameplayController", "ShowTutorial", 2),
        ("Il2CppInutan.UICharacterSystem", "CheckArmorGuide", 0),
        ("Il2Cpp.Buff_40", "CheckGuide", 0),
        ("Il2Cpp.Buff_41", "CheckGuide", 0),
        ("Il2CppInutan.QuestEventManager", "DoQuestGuideVoice", 1)
    };

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var methods = Targets.Select(t => {
            var type = Resolve(t.Type);
            return type.GetMethods(Members).Single(m => m.Name == t.Method && m.GetParameters().Length == t.Arity && m.ReturnType == typeof(void));
        }).ToArray();
        var riddles = Resolve("Il2CppInutan.RiddleSystem").GetMethods(Members)
            .Where(m => m.Name == "OpenRiddleTutorial" && m.GetParameters().Length == 1 && m.ReturnType == typeof(void)).ToArray();
        if (riddles.Length != 2) throw new MissingMethodException("Riddle tutorial contract changed");
        var questGuide = Resolve("Il2CppInutan.QuestSystem").GetMethods(Members)
            .Single(m => m.Name == "QuestFinishGuide" && m.IsStatic && m.GetParameters().Length == 2);
        if (questGuide.ReturnType.FullName != "Il2Cpp.Callback" || questGuide.GetParameters()[1].ParameterType != questGuide.ReturnType)
            throw new InvalidOperationException("Quest continuation contract changed");
        var skip = new HarmonyMethod(typeof(DisableGuides), nameof(Skip));
        foreach (var method in methods.Concat(riddles)) harmony.Patch(method, prefix: skip);
        harmony.Patch(questGuide, prefix: new HarmonyMethod(typeof(DisableGuides), nameof(PreserveQuestContinuation)));
        MelonLogger.Msg("[PrivateServer] Tutorials disabled: item/quest triggers, beginner panels, guide overlays, challenge/riddle and equipment tips.");
    }

    private static bool Skip() => false;

    // The caller owns invocation; preserve its original continuation without wrapping it in a guide.
    private static bool PreserveQuestContinuation(object[] __args, ref object? __result)
    {
        __result = __args[1];
        return false;
    }

    private static Type Resolve(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
}
