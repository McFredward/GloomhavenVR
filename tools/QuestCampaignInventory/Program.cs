using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ScenarioRuleLibrary.CustomLevels;

namespace QuestCampaignInventory;

/// <summary>Reads the selected original level classes, rather than inferring geometry from file names.</summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3 || args[0] is "--help" or "help")
            {
                Console.WriteLine("QuestCampaignInventory GAME_DATA OUTPUT_JSON OWNED_DLC_MASK");
                return args.Length == 0 ? 64 : 0;
            }
            string game = Path.GetFullPath(args[0]);
            string managed = Path.Combine(game, "Managed");
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string path = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            int mask = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            if (mask < 0 || (mask & ~7) != 0) throw new ArgumentException("Unknown original DLC flags.");
            Run(game, Path.GetFullPath(args[1]), mask);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Campaign inventory: " + error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string game, string output, int mask)
    {
        string rules = Path.Combine(game, "StreamingAssets", "Rulebase");
        var archives = new List<string> { "Campaign.ruleset", "CustomScenarios.ruleset" };
        foreach ((int flag, string name) in new[] { (1, "JoTL"), (2, "Solo") })
            if ((mask & flag) != 0)
                foreach (string category in new[] { "Campaign", "CustomScenarios" })
                    archives.Add($"DLC/DLC_{name}/DLC_{name}_{category}.ruleset");
        var records = new List<object>();
        var provenance = new List<object>();
        int randomised = 0;
        foreach (string relative in archives)
        {
            string path = Path.Combine(rules, relative);
            if (!File.Exists(path)) throw new FileNotFoundException("Selected original ruleset is unavailable.", path);
            using var archive = ZipFile.OpenRead(path);
            int count = 0;
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.EndsWith(".lvldat", StringComparison.OrdinalIgnoreCase)))
            {
                using var input = entry.Open();
                using var bytes = new MemoryStream();
                input.CopyTo(bytes);
                byte[] data = bytes.ToArray();
                bytes.Position = 0;
                // Only original local game inputs selected for conversion enter this process.
                object decoded = new BinaryFormatter().Deserialize(bytes);
                if (decoded is not CCustomLevelData level || level.ScenarioState == null)
                    throw new InvalidDataException("Original level has no scenario state: " + relative + "/" + entry.FullName);
                var state = level.ScenarioState;
                if (level.RandomiseOnLoad) randomised++;
                records.Add(new
                {
                    archive = relative, entry = entry.FullName, sha256 = Hash(data),
                    name = level.Name, id = state.ID, scenarioFileName = state.ScenarioFileName,
                    scenarioType = state.ScenarioType.ToString(), dlcUsed = (int)level.DLCUsed,
                    randomiseOnLoad = level.RandomiseOnLoad, seed = state.Seed, seedFromMap = state.SeedFromMap,
                    partySizeLimit = level.PartySizeLimit, partySpawnType = level.PartySpawnType.ToString(),
                    levelEvents = level.LevelEvents?.Count ?? 0, levelMessages = level.LevelMessages?.Count ?? 0,
                    hasScriptedEvents = level.HasScriptedEvents,
                    style = Values(state.Style),
                    overrides = (level.ApparanceOverrideList ?? new()).Select(item => Values(item)).ToArray(),
                    mapCount = state.Maps.Count, maps = state.Maps.Select(m => Values(m, "Name", "MapName", "MapInstanceName", "MapGuid", "Style", "Seed", "MapTiles", "FixedStyle")).ToArray(),
                    propCount = state.Props.Count, destroyedPropCount = state.DestroyedProps.Count,
                    activatedPropCount = state.ActivatedProps.Count
                });
                count++;
            }
            provenance.Add(new { path = "StreamingAssets/Rulebase/" + relative, sha256 = Hash(File.ReadAllBytes(path)), levels = count });
        }
        var report = new
        {
            schema = 1, scope = "original-campaign-level-semantic-inventory", ownedDlcMask = mask,
            originalDeserializerExecuted = true, androidExecutionProven = false, fullGameReady = false,
            assemblies = new[] { "ScenarioRuleLibrary", "SharedLibrary" }.Select(name => new
            { name, sha256 = Hash(File.ReadAllBytes(Path.Combine(game, "Managed", name + ".dll"))) }).ToArray(),
            archives = provenance, levelCount = records.Count, randomisedOnLoadCount = randomised,
            finitePresentationProven = false, levels = records
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Campaign inventory: original levels={records.Count}; randomise-on-load={randomised}; report={output}");
    }

    private static Dictionary<string, object?> Values(object? instance, params string[] selected)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (instance == null) return values;
        foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (selected.Length == 0 || selected.Contains(field.Name)) values[field.Name] = Scalar(field.GetValue(instance));
        foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.GetIndexParameters().Length == 0 && (selected.Length == 0 || selected.Contains(property.Name)))
                values[property.Name] = Scalar(property.GetValue(instance));
        return values;
    }

    private static object? Scalar(object? value) => value switch
    {
        null => null,
        string or int or uint or long or float or double or bool => value,
        Enum e => e.ToString(),
        System.Collections.ICollection collection => new { count = collection.Count },
        _ => Values(value)
    };

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
