using System.Reflection;
using mod.st;

internal static class HostContext
{
    private static readonly PropertyInfo Instance = typeof(NetChater).GetProperty("Instance")!;
    public static void Select(NetChater host) => Instance.SetValue(null, host);
}

namespace UnityEngine
{
    public sealed class Transform { public void SetParent(Transform parent) { } }
    public sealed class GameObject
    {
        public GameObject(string name) { }
        public Transform transform = new();
    }
}
namespace mod.st
{
    public sealed class NetPlayer
    {
        public static UnityEngine.GameObject root = new("TestPlayers");
        public static NetPlayer Instance = new();
        public Dictionary<string, UnityEngine.GameObject> players = new();
        public Dictionary<string, List<string>> FollowplayersList = new();
        public void reset() { players.Clear(); FollowplayersList.Clear(); }
    }
}
