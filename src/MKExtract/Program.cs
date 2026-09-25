using System.Reflection;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace mkextract;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "dump")
        {
            DumpApi();
            return 0;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("usage:");
            Console.WriteLine("  mkextract dump");
            Console.WriteLine("  mkextract list    <paksDir> <aesKey> <outTxt> [filter|--all]");
            Console.WriteLine("  mkextract extract <paksDir> <aesKey> <outDir> <ext1,ext2|all> <filter> [.. more filters]");
            Console.WriteLine("  mkextract probe   <paksDir> <aesKey> <pathFilter> <count>");
            Console.WriteLine("  mkextract voices  <paksDir> <aesKey> <outWemDir> <outWavDir> <language|all> <filter> [.. more filters]");
            Console.WriteLine("  mkextract json    <paksDir> <aesKey> <outDir> <pathFilter> [maxFiles]");
            Console.WriteLine("  mkextract locres  <paksDir> <aesKey> <outTsv> <pathFilter>");
            return 1;
        }

        var mode = args[0];
        var paksDir = args[1];

        try
        {
            using var provider = CreateProvider(paksDir);
            Console.WriteLine($"mounted {provider.MountedVfs.Count} archive(s), files={provider.Files.Count}");
            if (provider.UnloadedVfs.Count > 0)
                Console.WriteLine($"unmounted archives: {provider.UnloadedVfs.Count} (missing keys: {provider.RequiredKeys.Count})");
            foreach (var vfs in provider.UnloadedVfs)
                Console.WriteLine($"  unmounted: {vfs.Name} encrypted={vfs.IsEncrypted} keyGuid={vfs.EncryptionKeyGuid}");

            return mode switch
            {
                "list" => ListFiles(provider, args),
                "extract" => ExtractFiles(provider, args),
                "probe" => ProbeEvents(provider, args),
                "voices" => ExtractVoices(provider, args),
                "json" => DumpJson(provider, args),
                "locres" => DumpLocres(provider, args),
                _ => 1
            };
        }
        catch (Exception e)
        {
            Console.WriteLine("FATAL: " + e);
            return 2;
        }
    }

    private static int DumpLocres(DefaultFileProvider provider, string[] args)
    {
        var outTsv = args.Length > 3 ? args[3] : Path.Combine(Environment.CurrentDirectory, "locres.tsv");
        var filter = args.Length > 4 ? args[4] : string.Empty;

        var files = 0;
        var entries = 0;
        using var writer = new StreamWriter(outTsv, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("locres\tnamespace\tkey\tvalue");

        foreach (var file in provider.Files.Values)
        {
            if (!string.Equals(file.Extension, "locres", StringComparison.OrdinalIgnoreCase)) continue;
            if (!file.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var archive = file.CreateReader();
                var resource = new FTextLocalizationResource(archive);
                files++;
                foreach (var (namespce, keys) in resource.Entries)
                {
                    foreach (var (key, entry) in keys)
                    {
                        var value = (entry?.LocalizedString ?? string.Empty)
                            .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                        writer.WriteLine($"{file.Path}\t{namespce.Str}\t{key.Str}\t{value}");
                        entries++;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"  FAIL {file.Path}: {e.GetType().Name} {e.Message}");
            }
        }

        Console.WriteLine($"parsed {files} locres file(s), {entries} entries -> {outTsv}");
        return 0;
    }

    private static int DumpJson(DefaultFileProvider provider, string[] args)
    {
        var outDir = args.Length > 3 ? args[3] : Path.Combine(Environment.CurrentDirectory, "json");
        var filter = args.Length > 4 ? args[4] : string.Empty;
        var limit = args.Length > 5 && int.TryParse(args[5], out var parsed) ? parsed : 5;
        Directory.CreateDirectory(outDir);

        var written = 0;
        foreach (var file in provider.Files.Values)
        {
            if (!file.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(file.Extension, "uasset", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Path.Contains("/Languages/", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var obj = provider.LoadPackageObject(file.PathWithoutExtension);
                var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
                var target = Path.Combine(outDir, $"{written:D2}_{file.NameWithoutExtension}.json");
                File.WriteAllText(target, json);
                Console.WriteLine($"wrote {target} ({json.Length} bytes)");
                written++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  FAIL {file.Path}: {e.GetType().Name} {e.Message}");
            }

            if (written >= limit) break;
        }

        Console.WriteLine($"dumped {written} asset(s) to {outDir}");
        return 0;
    }

    private static int ExtractVoices(DefaultFileProvider provider, string[] args)
    {
        // 角色名可用环境变量 MKCHAR 覆盖（默认 OmniMan）
        var character = Environment.GetEnvironmentVariable("MKCHAR");
        if (string.IsNullOrWhiteSpace(character)) character = "OmniMan";

        string Categorize(string eventName)
        {
            if (eventName.StartsWith("vs_Descriptive", StringComparison.OrdinalIgnoreCase)) return "04_Descriptive_Narration";
            if (eventName.StartsWith($"vs_{character}", StringComparison.OrdinalIgnoreCase)) return $"01_{character}_Dialogue";
            if (eventName.StartsWith($"vo_{character}", StringComparison.OrdinalIgnoreCase)) return $"02_{character}_Effort";
            if (eventName.Contains("Exert", StringComparison.OrdinalIgnoreCase)) return $"02_{character}_Effort";
            if (eventName.StartsWith("vs_", StringComparison.OrdinalIgnoreCase)) return $"05_Others_vs_{character}";
            if (eventName.Contains(character, StringComparison.OrdinalIgnoreCase)) return $"03_{character}_MoveSFX";
            return "06_Misc";
        }

        // 事件里的媒体可能挂在 Media / AudioNodes / SwitchContainerLeaves 三处，全部收集
        static IEnumerable<JToken> EnumerateMedia(JToken value)
        {
            if (value["Media"] is JArray direct)
                foreach (var m in direct) yield return m;

            if (value["AudioNodes"] is JArray nodes)
                foreach (var node in nodes)
                    if (node["Value"]?["Media"] is JArray nodeMedia)
                        foreach (var m in nodeMedia) yield return m;

            if (value["SwitchContainerLeaves"] is JArray leaves)
                foreach (var leaf in leaves)
                    if (leaf["Media"] is JArray leafMedia)
                        foreach (var m in leafMedia) yield return m;
        }

        if (args.Length < 6)
        {
            Console.WriteLine("voices needs: <paksDir> <aesKey> <outWemDir> <outWavDir> <language|all> <filter...>");
            return 1;
        }

        var outWem = args[3];
        var outWav = args[4];
        var langArg = args[5];
        var filters = args.Skip(6).ToArray();
        var vgmstream = FindVgmstream();
        var wantAllLanguages = langArg.Equals("all", StringComparison.OrdinalIgnoreCase);

        Directory.CreateDirectory(outWem);
        Directory.CreateDirectory(outWav);

        var eventAssets = provider.Files.Values
            .Where(f => string.Equals(f.Extension, "uasset", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Path.Contains("WwiseAudio/Events", StringComparison.OrdinalIgnoreCase))
            .Where(f => filters.Length == 0 || filters.Any(x => f.Path.Contains(x, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Console.WriteLine($"event assets matched: {eventAssets.Count}");

        var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<string>();
        var wemFiles = new List<(string Wem, string Wav)>();
        var seenMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loaded = 0;
        var failed = 0;

        foreach (var ev in eventAssets)
        {
            UAkAudioEvent? audioEvent;
            try
            {
                audioEvent = provider.LoadPackageObject(ev.PathWithoutExtension) as UAkAudioEvent;
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"  LOAD FAIL {ev.Name}: {e.GetType().Name} {e.Message}");
                continue;
            }

            if (audioEvent is null) continue;

            JArray? languageMap;
            try
            {
                var json = JsonConvert.SerializeObject(audioEvent, Formatting.None);
                languageMap = JObject.Parse(json)["EventCookedData"]?["EventLanguageMap"] as JArray;
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"  JSON FAIL {ev.Name}: {e.GetType().Name} {e.Message}");
                continue;
            }

            if (languageMap is null) continue;
            loaded++;

            var prefix = ev.Directory.Replace("MK12/Content/WwiseAudio/Events/", string.Empty).Replace('/', '_');
            foreach (var entry in languageMap)
            {
                var value = entry["Value"];
                if (value is null || value.Type == JTokenType.Null) continue;

                var langName = (string?) entry["Key"]?["LanguageName"] ?? "Unknown";
                languages.Add(langName);
                var isSfxLanguage = langName.Equals("SFX", StringComparison.OrdinalIgnoreCase);
                if (!wantAllLanguages && !isSfxLanguage && !langName.Equals(langArg, StringComparison.OrdinalIgnoreCase)) continue;

                var mediaList = EnumerateMedia(value)
                    .GroupBy(m => (uint?) m["MediaId"] ?? 0u)
                    .Select(g => g.First())
                    .ToList();
                if (mediaList.Count == 0) continue;
                foreach (var media in mediaList)
                {
                    var mediaPathName = (string?) media["MediaPathName"];
                    var mediaId = (uint?) media["MediaId"] ?? 0u;
                    var streaming = (bool?) media["bStreaming"] ?? false;
                    var prefetch = (int?) media["PrefetchSize"] ?? 0;
                    if (string.IsNullOrWhiteSpace(mediaPathName)) continue;

                    var candidates = new[]
                    {
                        "MK12/Content/WwiseAudio/Generated/" + mediaPathName,
                        $"MK12/Content/WwiseAudio/Generated/Media/{mediaId}.wem"
                    };

                    GameFile? mediaFile = null;
                    foreach (var candidate in candidates)
                    {
                        if (provider.TryGetGameFile(candidate, out var found) && found != null)
                        {
                            mediaFile = found;
                            break;
                        }
                    }

                    if (mediaFile is null)
                    {
                        Console.WriteLine($"  media not found: {mediaPathName} (event {ev.Name})");
                        continue;
                    }

                    var langPrefix = wantAllLanguages ? langName.Replace("(", "_").Replace(")", string.Empty) + "__" : string.Empty;
                    var baseName = $"{prefix}__{ev.NameWithoutExtension}" + (mediaList.Count > 1 ? $"__{mediaId}" : string.Empty);
                    var category = Categorize(ev.NameWithoutExtension);
                    var wemTarget = Path.Combine(outWem, category, langPrefix + baseName + ".wem");
                    var wavTarget = Path.Combine(outWav, category, langPrefix + baseName + ".wav");
                    Directory.CreateDirectory(Path.GetDirectoryName(wemTarget)!);
                    Directory.CreateDirectory(Path.GetDirectoryName(wavTarget)!);
                    rows.Add($"{ev.Name},{langName},{mediaId},{streaming},{prefetch},{new FileInfo(wemTarget).Name}");

                    if (!seenMedia.Add(wemTarget)) continue;
                    try
                    {
                        var bytes = provider.SaveAsset(mediaFile);
                        File.WriteAllBytes(wemTarget, bytes);
                        wemFiles.Add((wemTarget, wavTarget));
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Console.WriteLine($"  SAVE FAIL {mediaFile.Path}: {e.GetType().Name} {e.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"languages available: {string.Join(", ", languages)}");
        Console.WriteLine($"loaded {loaded} event(s), {failed} failure(s), {wemFiles.Count} media file(s)");

        var mappingPath = Path.Combine(outWem, "mapping.csv");
        File.WriteAllLines(mappingPath, new[] { "event,language,mediaId,streaming,prefetchSize,wem" }.Concat(rows),
            new System.Text.UTF8Encoding(true));
        Console.WriteLine($"mapping written to {mappingPath}");

        if (!File.Exists(vgmstream))
        {
            Console.WriteLine($"vgmstream-cli not found at {vgmstream}; skipping wav conversion");
            return 0;
        }

        var converted = 0;
        foreach (var (wem, wav) in wemFiles)
        {
            if (File.Exists(wav)) { converted++; continue; }
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vgmstream,
                    Arguments = $"-o \"{wav}\" \"{wem}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                proc?.WaitForExit(60000);
                if (File.Exists(wav)) converted++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  CONVERT FAIL {Path.GetFileName(wem)}: {e.Message}");
            }

            if (converted > 0 && converted % 100 == 0) Console.WriteLine($"  converted {converted} wav ...");
        }

        Console.WriteLine($"converted {converted}/{wemFiles.Count} to wav in {outWav}");
        return 0;
    }

    private static int ProbeEvents(DefaultFileProvider provider, string[] args)
    {
        var filter = args.Length > 3 ? args[3] : "WwiseAudio/Events";
        var limit = args.Length > 4 && int.TryParse(args[4], out var parsed) ? parsed : 1;
        var shown = 0;

        foreach (var file in provider.Files.Values)
        {
            if (!file.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(file.Extension, "uasset", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Path.Contains("/Languages/", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var obj = provider.LoadPackageObject(file.PathWithoutExtension);
                Console.WriteLine($"### {file.Path} -> {obj?.GetType().Name ?? "null"}");
                if (obj is UAkAudioEvent audioEvent)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(audioEvent, Formatting.Indented));
                }
                else if (obj != null)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented).Substring(0, Math.Min(1200, JsonConvert.SerializeObject(obj, Formatting.Indented).Length)));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"### {file.Path} -> FAILED {e.GetType().Name}: {e.Message}");
            }

            if (++shown >= limit) break;
        }

        Console.WriteLine($"probed {shown} asset(s)");
        return 0;
    }

    private const string DefaultAesKey = "0x6FAABA4F4EF8A6AC188A517ACEF38F1422484E3B1F3F4CF3DACB27A6CBCCD076";

    private static DefaultFileProvider CreateProvider(string paksDir)
    {
        InitializeOodle();

        var versions = new VersionContainer(EGame.GAME_MortalKombat1);
        var provider = new DefaultFileProvider(paksDir, SearchOption.TopDirectoryOnly, true, versions);
        provider.Initialize();
        Console.WriteLine($"discovered {provider.UnloadedVfs.Count} archive(s) before mounting");

        var key = new FAesKey(DefaultAesKey);
        provider.SubmitKey(new FGuid(), key);
        Console.WriteLine($"after default key: mounted={provider.MountedVfs.Count} unloaded={provider.UnloadedVfs.Count} requiredKeys={provider.RequiredKeys.Count}");

        provider.Mount();
        Console.WriteLine($"after mount: mounted={provider.MountedVfs.Count} unloaded={provider.UnloadedVfs.Count}");

        foreach (var guid in provider.RequiredKeys.ToArray())
        {
            var submitted = provider.SubmitKey(guid, key);
            Console.WriteLine($"submitted key for {guid}: {submitted}");
        }

        provider.PostMount();
        Console.WriteLine($"after submit+postmount: mounted={provider.MountedVfs.Count} unloaded={provider.UnloadedVfs.Count} files={provider.Files.Count}");
        return provider;
    }

    // 找 vgmstream-cli.exe：环境变量 > 程序目录 > 程序目录\vgmstream > 上一级\vgmstream > PATH
    private static string FindVgmstream()
    {
        var env = Environment.GetEnvironmentVariable("VGMSTREAM_CLI");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "vgmstream-cli.exe"),
            Path.Combine(baseDir, "vgmstream", "vgmstream-cli.exe"),
            Path.Combine(baseDir, "..", "vgmstream", "vgmstream-cli.exe"),
        };
        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore */ }
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var p = Path.Combine(dir, "vgmstream-cli.exe");
                if (File.Exists(p)) return p;
            }
            catch { /* ignore */ }
        }

        return "vgmstream-cli.exe"; // 交给系统按 PATH 解析，找不到时调用处会提示
    }

    private static void InitializeOodle()
    {
        if (OodleHelper.Instance is not null)
        {
            Console.WriteLine("oodle: already initialized");
            return;
        }

        try
        {
            var explicitPath = Environment.GetEnvironmentVariable("OODLE_DLL");
            if (string.IsNullOrWhiteSpace(explicitPath) || !File.Exists(explicitPath))
            {
                foreach (var candidate in new[] { "oo2core_9_win64.dll", "oo2core_6_win64.dll", "oodle-data-shared.dll" })
                {
                    if (!File.Exists(candidate)) continue;
                    explicitPath = Path.GetFullPath(candidate);
                    break;
                }
            }

            OodleHelper.Initialize(string.IsNullOrWhiteSpace(explicitPath) ? null : explicitPath);
            Console.WriteLine($"oodle: initialized ({(string.IsNullOrWhiteSpace(explicitPath) ? "downloaded/internal" : explicitPath)})");
        }
        catch (Exception e)
        {
            Console.WriteLine("oodle: initialize failed -> " + e.Message);
        }
    }

    private static int ListFiles(DefaultFileProvider provider, string[] args)
    {
        var outTxt = args.Length > 3 ? args[3] : Path.Combine(Environment.CurrentDirectory, "filelist.txt");
        var filter = args.Length > 4 ? args[4] : null;
        if (filter is "--all") filter = null;

        // UTF-8 with BOM：中文 Excel / WPS 才不会按 GBK 解码成乱码
        using var writer = new StreamWriter(outTxt, false, new System.Text.UTF8Encoding(true));
        var count = 0;
        foreach (var file in provider.Files.Values)
        {
            if (filter != null && !file.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            writer.WriteLine(file.Path);
            count++;
        }

        Console.WriteLine($"wrote {count} path(s) to {outTxt}");
        return 0;
    }

    private static int ExtractFiles(DefaultFileProvider provider, string[] args)
    {
        var outDir = args.Length > 3 ? args[3] : Path.Combine(Environment.CurrentDirectory, "extracted");
        var extArg = args.Length > 4 ? args[4] : "all";
        var filters = args.Skip(5).ToArray();
        var extensions = extArg.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : new HashSet<string>(extArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(e => e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
        if (filters.Length == 0)
        {
            Console.WriteLine("extract needs at least one filter substring");
            return 1;
        }

        Directory.CreateDirectory(outDir);
        var ok = 0;
        var failed = 0;
        long bytes = 0;
        long matched = 0;

        foreach (var file in provider.Files.Values)
        {
            if (!filters.Any(f => file.Path.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            if (extensions != null && (string.IsNullOrEmpty(file.Extension) || !extensions.Contains(file.Extension.TrimStart('.')))) continue;
            matched++;

            var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(outDir, relative);
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            try
            {
                var data = provider.SaveAsset(file);
                File.WriteAllBytes(target, data);
                ok++;
                bytes += data.Length;
                if (ok % 200 == 0) Console.WriteLine($"  {ok} files ({bytes / 1048576.0:F1} MB) ...");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"  FAIL {file.Path}: {e.GetType().Name} {e.Message}");
            }
        }

        Console.WriteLine($"matched {matched} file(s), extracted {ok} ({bytes / 1048576.0:F1} MB), {failed} failure(s) -> {outDir}");
        return failed > 0 ? 3 : 0;
    }

    private static void DumpApi()
    {
        Assembly asm;
        try
        {
            asm = Assembly.Load("CUE4Parse");
        }
        catch (Exception e)
        {
            Console.WriteLine("failed to load CUE4Parse: " + e.Message);
            return;
        }

        Console.WriteLine($"assembly: {asm.GetName().Name} {asm.GetName().Version}");
        Console.WriteLine($"location: {asm.Location}");

        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray();
        }

        string[] wanted =
        [
            "DefaultFileProvider", "AbstractVfsFileProvider", "IFileProvider", "IVfsFileProvider",
            "VersionContainer", "FAesKey", "FGuid", "WwiseProvider", "WwiseReader", "IoStoreReader",
            "UAkAudioBank", "UAkAudioEvent", "UAkMediaAsset", "UAkMediaAssetData", "GameFile"
        ];

        foreach (var t in types.Where(t => wanted.Contains(t.Name)).OrderBy(t => t.Name))
        {
            Console.WriteLine();
            Console.WriteLine("=== " + t.FullName + (t.IsEnum ? " (enum)" : ""));
            if (t.IsEnum)
            {
                var names = Enum.GetNames(t);
                Console.WriteLine("   values: " + string.Join(", ", names.Take(80)));
                continue;
            }

            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine("   ctor  " + c);

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                                   .Where(m => !m.IsSpecialName))
                Console.WriteLine("   meth  " + m);

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine("   prop  " + p);
        }
    }
}
