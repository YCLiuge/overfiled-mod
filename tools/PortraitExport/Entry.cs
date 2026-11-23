using System;
using System.IO;

namespace PortraitExport
{
    public static class Entry
    {
        // Uses the same bundle command parser and exporter with the IL2CPP type fix.
        public static void Run(string bundle, string output)
        {
            if (!bundle.StartsWith("assets_development_arts_ui_atlas_cn_resbundle_texture_character_character_", StringComparison.Ordinal)
                || !bundle.EndsWith(".bundle", StringComparison.Ordinal))
                throw new ArgumentException("Expected a character illustration bundle.");
            Logic.BundleHelper.update("bundle export \"" + bundle + "\" quanshen \"" + Path.GetFullPath(output) + "\"");
        }
    }
}
