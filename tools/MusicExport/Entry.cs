using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace MusicExportV2
{
    public static class Entry
    {
        private static AssetBundle[] Bundles() => Logic.BundleHelper.GetAllLoadedBundles()
            .Where(b => b.name.StartsWith("assets_development_arts_sound_bgm_", StringComparison.Ordinal))
            .OrderBy(b => b.name, StringComparer.Ordinal).ToArray();

        public static void Catalog(string output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "catalog.json"), JsonConvert.SerializeObject(
                Bundles().Select((b,i) => new { index=i, bundle=b.name, assets=Logic.BundleHelper.ListAssets(b,false) }), Formatting.Indented));
        }

        public static void Run(string index, string output)
        {
            var b = Bundles()[int.Parse(index)];
            string dir = Path.Combine(output, int.Parse(index).ToString("D3"));
            Directory.CreateDirectory(dir);
            // Invoke the fixed bundle command implementation without reloading Logic.
            Logic.BundleHelper.update("bundle export \"" + b.name + "\" . \"" + dir + "\"");
            File.WriteAllText(Path.Combine(dir, "result.json"), JsonConvert.SerializeObject(new {
                index=int.Parse(index), bundle=b.name, files=Directory.GetFiles(dir,"*.wav",SearchOption.AllDirectories)
            }, Formatting.Indented));
        }
    }
}
