// ShaderOcclusionPatcher — fixes VR depth-occlusion bleed in Gloomhaven Digital.
//
// Certain game shaders are compiled with "ZTest Always" in their serialized pass
// render state, so their geometry (flames, glows, hex decals, x-ray tiles, moths)
// draws on top of walls. This tool patches ONLY the serialized pass state inside
// the compiled Shader assets — zTest 8 (Always) -> 4 (LEqual) — leaving the
// compiled shader programs byte-identical. No recompilation, no visual drift.
//
// Commands: scan | patch | verify | restore   (see PrintUsage below)

using ShaderOcclusionPatcher;

return Cli.Run(args);

namespace ShaderOcclusionPatcher
{
    internal static class Cli
    {
        public const string ToolVersion = "1.0.0";

        public static int Run(string[] args)
        {
            try
            {
                if (args.Length < 1) { PrintUsage(); return 1; }
                string command = args[0].ToLowerInvariant();
                string? gameData = null, backupDir = null, manifestOut = null;
                for (int i = 1; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--game-data": gameData = args[++i]; break;
                        case "--backup-dir": backupDir = args[++i]; break;
                        case "--manifest-out": manifestOut = args[++i]; break;
                        default:
                            Console.Error.WriteLine($"Unknown option: {args[i]}");
                            PrintUsage();
                            return 1;
                    }
                }

                if (gameData is null) { Console.Error.WriteLine("--game-data is required."); return 1; }
                gameData = Path.GetFullPath(gameData);
                if (!Directory.Exists(gameData)) { Console.Error.WriteLine($"Game data dir not found: {gameData}"); return 1; }

                switch (command)
                {
                    case "scan":
                        return Engine.RunScanOrVerify(gameData, manifestOut, verifyMode: false);
                    case "verify":
                        return Engine.RunScanOrVerify(gameData, manifestOut, verifyMode: true);
                    case "patch":
                        if (backupDir is null) { Console.Error.WriteLine("--backup-dir is required for patch."); return 1; }
                        return Engine.RunPatch(gameData, Path.GetFullPath(backupDir), manifestOut);
                    case "restore":
                        if (backupDir is null) { Console.Error.WriteLine("--backup-dir is required for restore."); return 1; }
                        return Engine.RunRestore(gameData, Path.GetFullPath(backupDir));
                    case "dump": // undocumented diagnostic: full pass-state JSON of the target shaders
                        return Engine.RunDump(gameData);
                    default:
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: {ex}");
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"""
                ShaderOcclusionPatcher {ToolVersion} — Gloomhaven VR occlusion fix (ZTest Always -> LEqual)

                Usage:
                  ShaderOcclusionPatcher scan    --game-data <Gloomhaven_Data> [--manifest-out <file>]
                  ShaderOcclusionPatcher patch   --game-data <Gloomhaven_Data> --backup-dir <dir> [--manifest-out <file>]
                  ShaderOcclusionPatcher verify  --game-data <Gloomhaven_Data> [--manifest-out <file>]
                  ShaderOcclusionPatcher restore --game-data <Gloomhaven_Data> --backup-dir <dir>

                scan    read-only: locate the target shaders, dump per-pass zTest/zWrite/queue.
                patch   back up originals (never overwriting existing backups), then set
                        zTest Always(8) -> LEqual(4) in place. Idempotent.
                verify  re-read current values; exit 0 when no Always passes remain.
                restore copy every backed-up file back into the game dir.
                """);
        }
    }
}
