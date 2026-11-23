using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace mod.tool
{
    internal class BundleTool
    {

        public static void PrintLoadedBundles()
        {
            // 获取 Il2Cpp 的 IEnumerable (非泛型也可，但这里保留泛型)
            var enumerable = AssetBundle.GetAllLoadedAssetBundles();
            if (enumerable == null)
            {
                MelonLogger.Warning("没有已加载的 AssetBundle");
                return;
            }

            // 获取 Il2Cpp 的枚举器（类型为 Il2CppSystem.Collections.Generic.IEnumerator<AssetBundle>）
            var il2cppEnumerator = enumerable.GetEnumerator();
            // 转换为标准 .NET IEnumerator，以便使用 MoveNext 和 Current
            var enumerator = (System.Collections.IEnumerator)il2cppEnumerator;

            int count = 0;
            List<string> msg = new();
            while (enumerator.MoveNext())
            {
                // Current 是 object，需要转为 AssetBundle
                var bundle = enumerator.Current as AssetBundle;
                if (bundle != null)
                {
                    MelonLogger.Msg($"Bundle {++count}: {bundle.name}");
                    msg.Add(count +":"+ bundle.name);
                }
            }
            FileManager.Save(ConstMsg.filepath+ "AssetBundles表.json", msg);
            MelonLogger.Msg($"共找到 {count} 个已加载的 AssetBundle");
        }

        /// <summary>
        /// 根据资源路径查找所属的 Bundle 名称
        /// </summary>
        public static string FindBundleForAsset(string assetPath)
        {
            var enumerable = AssetBundle.GetAllLoadedAssetBundles();
            if (enumerable == null) return null;

            var il2cppEnumerator = enumerable.GetEnumerator();
            var enumerator = (System.Collections.IEnumerator)il2cppEnumerator;

            while (enumerator.MoveNext())
            {
                var bundle = enumerator.Current as AssetBundle;
                if (bundle == null) continue;
                // 获取该 Bundle 内所有资源路径
                string[] assetNames = bundle.GetAllAssetNames();
                if (assetNames != null && System.Array.Exists(assetNames, n => n == assetPath))
                    return bundle.name;
            }
            return null;
        }
    }
}
