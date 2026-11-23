using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("Usage: VersionManifestGenerator <GameMods/Mods directory>");
            return args.Length == 1 ? 0 : 1;
        }
        try
        {
            string root = Path.GetFullPath(args[0]);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            string manifestPath = Path.Combine(root, "version.json");
            // Serialize complete scans, not just writes, to prevent parallel projects
            // from publishing a stale snapshot over a more recent manifest.
            using var generationLock = Retry(() => new FileStream(
                Path.Combine(root, ".version-generator.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
            Manifest previous = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonOptions)
                    ?? throw new InvalidDataException("The existing manifest is empty.")
                : new Manifest();
            var modules = new List<Module>();
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(p => Path.GetExtension(p).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);
                if (name.Equals("mod.dll", StringComparison.OrdinalIgnoreCase)) continue;
                Module? old = previous.Modules.FirstOrDefault(m =>
                    FileNameFromUrl(m.Url).Equals(name, StringComparison.OrdinalIgnoreCase));
                Module? module = ReadModule(path, old);
                if (module != null) modules.Add(module);
            }
            var duplicate = modules.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidDataException($"Duplicate module ID: {duplicate.Key}");
            var manifest = new Manifest
            {
                Modules = modules.OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal).ToList()
            };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            // Same-directory rename publishes complete UTF-8 JSON without a BOM.
            // Retry if a TCP reader briefly has the old manifest open.
            string temp = Path.Combine(root, $".version.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(json);
                    output.Flush(flushToDisk: true);
                }
                Retry(() => { File.Move(temp, manifestPath, overwrite: true); return true; });
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            Console.WriteLine($"[VersionJson] {manifestPath} ({modules.Count} modules; mod.dll excluded)");
            foreach (Module m in manifest.Modules)
                Console.WriteLine($"[VersionJson] {m.Id} v{m.Version} -> {m.Url} (priority {m.Priority})");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VersionJson] ERROR: {ex.Message}");
            return 1;
        }
    }

    private static Module? ReadModule(string path, Module? previous)
    {
        // Metadata and hash come from the same snapshot. Never execute module code
        // or load Unity/MelonLoader dependencies in the generator process.
        byte[] bytes = Retry(() => File.ReadAllBytes(path));
        try
        {
            using var pe = new PEReader(new MemoryStream(bytes));
            if (!pe.HasMetadata) return null;
            MetadataReader reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) return null;
            AssemblyDefinition assembly = reader.GetAssemblyDefinition();
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            string? informationalVersion = null;
            foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string type = AttributeType(reader, attribute.Constructor);
                if (type is not ("System.Reflection.AssemblyMetadataAttribute" or
                    "System.Reflection.AssemblyInformationalVersionAttribute")) continue;
                BlobReader blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) throw new InvalidDataException($"Invalid attribute in {path}");
                string? first = blob.ReadSerializedString();
                if (type.EndsWith("AssemblyMetadataAttribute", StringComparison.Ordinal))
                {
                    string? value = blob.ReadSerializedString();
                    if (first != null && value != null) metadata[first] = value;
                }
                else informationalVersion = first;
            }
            if (!metadata.ContainsKey("HotUpdate.ModuleId") && !ImplementsLogic(reader))
            {
                Console.WriteLine($"[VersionJson] Skipping non-module: {Path.GetFileName(path)}");
                return null;
            }
            string id = metadata.GetValueOrDefault("HotUpdate.ModuleId")
                ?? previous?.Id ?? reader.GetString(assembly.Name);
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]+$"))
                throw new InvalidDataException($"Unsafe module ID '{id}' in {path}");
            string version = informationalVersion?.Split('+')[0]
                ?? (assembly.Version.Revision == 0 ? assembly.Version.ToString(3) : assembly.Version.ToString(4));
            if (!Version.TryParse(version, out _))
                throw new InvalidDataException($"Invalid numeric version '{version}' in {path}");
            int priority = metadata.TryGetValue("HotUpdate.Priority", out string? rawPriority)
                ? int.Parse(rawPriority, System.Globalization.CultureInfo.InvariantCulture) : previous?.Priority ?? 100;
            bool enabled = metadata.TryGetValue("HotUpdate.Enabled", out string? rawEnabled)
                ? bool.Parse(rawEnabled) : previous?.Enabled ?? true;
            return new Module
            {
                Id = id,
                Version = version,
                Url = Path.GetFileName(path),
                Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Priority = priority,
                Enabled = enabled,
                Notes = metadata.GetValueOrDefault("HotUpdate.Notes") ?? previous?.Notes
            };
        }
        catch (BadImageFormatException) when (previous == null)
        {
            Console.WriteLine($"[VersionJson] Skipping non-managed DLL: {Path.GetFileName(path)}");
            return null;
        }
    }

    private static string AttributeType(MetadataReader reader, EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference) return "";
        EntityHandle type = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (type.Kind != HandleKind.TypeReference) return "";
        TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)type);
        return reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
    }

    private static bool ImplementsLogic(MetadataReader reader)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        foreach (InterfaceImplementationHandle implementation in reader.GetTypeDefinition(handle).GetInterfaceImplementations())
        {
            EntityHandle type = reader.GetInterfaceImplementation(implementation).Interface;
            if (type.Kind != HandleKind.TypeReference) continue;
            TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)type);
            if (reader.GetString(reference.Namespace) == "ModLoader" && reader.GetString(reference.Name) == "ILogic")
                return true;
        }
        return false;
    }

    private static string FileNameFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) url = uri.AbsolutePath;
        return Path.GetFileName(url.Replace('/', Path.DirectorySeparatorChar));
    }

    private static T Retry<T>(Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return action(); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(30)) { Thread.Sleep(50); }
        }
    }

    private sealed class Manifest
    {
        public List<Module> Modules { get; set; } = new();
    }

    private sealed class Module
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public string Url { get; set; } = "";
        public string Hash { get; set; } = "";
        public int Priority { get; set; } = 100;
        public bool Enabled { get; set; } = true;
        public string? Notes { get; set; }
    }
}
