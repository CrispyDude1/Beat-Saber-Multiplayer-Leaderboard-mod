using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MultiplayerLeaderboard.Verify
{
    /// <summary>
    /// Static checks for the compiled Multiplayer Leaderboard DLL:
    /// target framework/architecture, every assembly reference resolving against the game or BSIPA,
    /// the embedded BSIPA manifest, the plugin entry-point contract, all game members the mod uses,
    /// no reference to another plugin, and no developer-machine paths in the emitted strings.
    /// </summary>
    internal static class Program
    {
        private static int _failures;
        private static int _checks;

        private static int Main(string[] args)
        {
            string dll = Argument(args, "--dll");
            string game = Argument(args, "--game");
            string bsipa = Argument(args, "--bsipa");

            if (string.IsNullOrEmpty(dll) || string.IsNullOrEmpty(game))
            {
                Console.Error.WriteLine("usage: Verify --dll <MultiplayerLeaderboard.dll> --game <Beat Saber> [--bsipa <IPA>]");
                return 2;
            }

            dll = Path.GetFullPath(dll);
            game = Path.GetFullPath(game);
            string managed = Path.Combine(game, "Beat Saber_Data", "Managed");
            string plugins = Path.Combine(game, "Plugins");
            string ipaRoot = string.IsNullOrEmpty(bsipa) ? Path.Combine(game, "IPA") : Path.GetFullPath(bsipa);
            string ipaManaged = Path.Combine(ipaRoot, "Data", "Managed");

            Console.WriteLine("DLL:      " + dll);
            Console.WriteLine("Game:     " + game);
            Console.WriteLine();

            if (!File.Exists(dll))
            {
                Console.Error.WriteLine("error: DLL not found");
                return 2;
            }

            if (!Directory.Exists(managed))
            {
                Console.Error.WriteLine("error: game managed folder not found: " + managed);
                return 2;
            }

            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            if (Directory.Exists(ipaManaged))
            {
                resolver.AddSearchDirectory(ipaManaged);
            }

            if (Directory.Exists(plugins))
            {
                resolver.AddSearchDirectory(plugins);
            }

            ReaderParameters parameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false,
                InMemory = true
            };

            using ModuleDefinition module = ModuleDefinition.ReadModule(dll, parameters);

            CheckIdentity(module);
            CheckReferences(module, managed, ipaManaged, plugins);
            CheckManifest(module);
            CheckPluginContract(module);
            CheckGameMembers(resolver, managed, ipaManaged, plugins);
            CheckForbiddenNames(module);
            CheckEmbeddedStrings(module);

            Console.WriteLine();
            Console.WriteLine("SHA256: " + Sha256(dll));
            Console.WriteLine("Size:   " + new FileInfo(dll).Length + " bytes");
            Console.WriteLine();

            if (_failures == 0)
            {
                Console.WriteLine("PASS - " + _checks + " checks, 0 failures.");
                return 0;
            }

            Console.WriteLine("FAIL - " + _checks + " checks, " + _failures + " failure(s).");
            return 1;
        }

        private static void CheckIdentity(ModuleDefinition module)
        {
            Section("Assembly identity");
            Info("name: " + module.Assembly.Name.Name + " " + module.Assembly.Name.Version);
            Check(isTrue: module.Assembly.Name.Name == "MultiplayerLeaderboard", "assembly name is MultiplayerLeaderboard");
            Check(module.Kind == ModuleKind.Dll, "module kind is DLL");
            Check((module.Attributes & ModuleAttributes.ILOnly) != 0,
                  "IL-only managed assembly (" + module.Attributes + ")");
            Info("architecture: " + module.Architecture);
            Check(module.RuntimeVersion != null && module.RuntimeVersion.StartsWith("v4.0", StringComparison.Ordinal),
                  "runtime target is .NET Framework 4.x (" + module.RuntimeVersion + ")");
            Info("target framework attribute: " + TargetFramework(module));
        }

        private static string TargetFramework(ModuleDefinition module)
        {
            try
            {
                CustomAttribute attribute = module.Assembly.CustomAttributes
                    .FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute");
                return attribute?.ConstructorArguments[0].Value as string ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static void CheckReferences(ModuleDefinition module, string managed, string ipaManaged, string plugins)
        {
            Section("Assembly references");

            foreach (AssemblyNameReference reference in module.AssemblyReferences.OrderBy(r => r.Name))
            {
                string file = FindAssembly(reference.Name, managed, ipaManaged, plugins);
                if (file == null)
                {
                    Fail("reference does not resolve: " + reference.Name + " " + reference.Version);
                }
                else
                {
                    Info("ok  " + reference.Name + " " + reference.Version + " -> " + Path.GetFileName(file));
                }
            }

            Check(module.AssemblyReferences.Any(r => r.Name == "IPA.Loader"), "references the mod loader (IPA.Loader)");
            Check(module.AssemblyReferences.Any(r => r.Name == "BGNetCore"), "references BGNetCore (multiplayer session)");
            Check(module.AssemblyReferences.Any(r => r.Name == "Networking"), "references Networking (score sync state)");
            Check(module.AssemblyReferences.Any(r => r.Name == "Main"), "references Main (local player placement)");
            Check(module.AssemblyReferences.Any(r => r.Name == "BSML"), "references BSML (Mods pane and settings UI)");
        }

        private static string FindAssembly(string name, string managed, string ipaManaged, string plugins)
        {
            string candidate = Path.Combine(managed, name + ".dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(ipaManaged, name + ".dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(plugins, name + ".dll");
            return File.Exists(candidate) ? candidate : null;
        }

        private static void CheckManifest(ModuleDefinition module)
        {
            Section("Embedded BSIPA manifest");

            Resource resource = module.Resources.FirstOrDefault(r => r.Name.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                Fail("no embedded manifest.json (BSIPA would refuse to load the plugin)");
                return;
            }

            Info("resource: " + resource.Name);

            string json;
            EmbeddedResource embedded = resource as EmbeddedResource;
            if (embedded == null)
            {
                Fail("manifest.json is not an embedded resource");
                return;
            }

            using (Stream stream = embedded.GetResourceStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                Check(Text(root, "id") == "MultiplayerLeaderboard", "manifest id is MultiplayerLeaderboard");
                Check(Text(root, "name") == "Multiplayer Leaderboard", "manifest name is Multiplayer Leaderboard");
                Check(Text(root, "version") == "1.4.0", "manifest version is 1.4.0");
                Check(Text(root, "gameVersion") == "1.40.3", "manifest gameVersion is 1.40.3");

                bool hasBsipa = root.TryGetProperty("dependsOn", out JsonElement dependsOn) &&
                                dependsOn.TryGetProperty("BSIPA", out _);
                bool hasBsml = root.TryGetProperty("dependsOn", out JsonElement bsmlDeps) &&
                               bsmlDeps.TryGetProperty("BeatSaberMarkupLanguage", out _);
                Check(hasBsipa, "manifest depends on BSIPA");
                Check(hasBsml, "manifest depends on BeatSaberMarkupLanguage (Mods pane and settings UI)");

                if (root.TryGetProperty("dependsOn", out JsonElement deps) && deps.ValueKind == JsonValueKind.Object)
                {
                    Check(deps.EnumerateObject().Count() == 2, "manifest declares only BSIPA and BSML");
                }
            }
            catch (Exception ex)
            {
                Fail("manifest.json is not valid JSON: " + ex.Message);
            }
        }

        private static string Text(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static void CheckPluginContract(ModuleDefinition module)
        {
            Section("BSIPA plugin contract");

            TypeDefinition plugin = module.Types.FirstOrDefault(t =>
                t.CustomAttributes.Any(a => a.AttributeType.FullName == "IPA.PluginAttribute"));

            if (plugin == null)
            {
                Fail("no [Plugin] type");
                return;
            }

            Info("plugin type: " + plugin.FullName);
            Check(plugin.IsPublic, "[Plugin] type is public");

            bool init = plugin.Methods.Any(m => m.IsConstructor && m.CustomAttributes.Any(a => a.AttributeType.FullName == "IPA.InitAttribute"));
            Check(init, "has an [Init] constructor");

            MethodDefinition onStart = plugin.Methods.FirstOrDefault(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == "IPA.OnStartAttribute"));
            Check(onStart != null && onStart.IsPublic, "has a public [OnStart] method");

            MethodDefinition onExit = plugin.Methods.FirstOrDefault(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == "IPA.OnExitAttribute"));
            Check(onExit != null && onExit.IsPublic, "has a public [OnExit] method");
        }

        private static void CheckGameMembers(DefaultAssemblyResolver resolver, string managed, string ipaManaged,
                                             string plugins)
        {
            Section("Game members used by the mod");

            AssemblyDefinition bgnet = Load(resolver, managed, ipaManaged, plugins, "BGNetCore");
            AssemblyDefinition networking = Load(resolver, managed, ipaManaged, plugins, "Networking");
            AssemblyDefinition main = Load(resolver, managed, ipaManaged, plugins, "Main");

            TypeDefinition session = Type(bgnet, "MultiplayerSessionManager");
            Check(session != null, "BGNetCore.MultiplayerSessionManager exists");
            if (session != null)
            {
                Check(HasProperty(session, "connectedPlayers"), "MultiplayerSessionManager.connectedPlayers");
                Check(HasProperty(session, "connectedPlayerCount"), "MultiplayerSessionManager.connectedPlayerCount");
                Check(HasProperty(session, "isConnected"), "MultiplayerSessionManager.isConnected");
                Check(HasProperty(session, "isSyncTimeInitialized"), "MultiplayerSessionManager.isSyncTimeInitialized");
                Check(HasProperty(session, "localPlayer"), "MultiplayerSessionManager.localPlayer");
            }

            TypeDefinition player = Type(bgnet, "IConnectedPlayer");
            Check(player != null, "BGNetCore.IConnectedPlayer exists");
            if (player != null)
            {
                foreach (string member in new[] { "userId", "userName", "sortIndex", "isMe", "isConnected", "offsetSyncTime" })
                {
                    Check(HasProperty(player, member), "IConnectedPlayer." + member);
                }
            }

            TypeDefinition scoreManager = Type(networking, "ScoreSyncStateManager");
            Check(scoreManager != null, "Networking.ScoreSyncStateManager exists (MonoBehaviour)");

            TypeDefinition scoreInterface = Type(networking, "IScoreSyncStateManager`5") ??
                                            Type(networking, "IScoreSyncStateManager");
            Check(scoreInterface != null, "Networking.IScoreSyncStateManager exists");
            if (scoreInterface != null)
            {
                Info("interface: " + scoreInterface.FullName);
                Check(HasProperty(scoreInterface, "connectedPlayerCount"), "IScoreSyncStateManager.connectedPlayerCount");
                Check(HasProperty(scoreInterface, "syncTime"), "IScoreSyncStateManager.syncTime");
                Check(HasMethod(scoreInterface, "GetSyncState", 1), "IScoreSyncStateManager.GetSyncState(int)");
            }

            TypeDefinition syncState = Type(networking, "MultiplayerSyncState`3");
            Check(syncState != null, "Networking.MultiplayerSyncState<,,> exists");
            if (syncState != null)
            {
                Check(HasProperty(syncState, "player"), "MultiplayerSyncState.player");
                Check(HasMethod(syncState, "GetState", 2), "MultiplayerSyncState.GetState(type, time)");
                Check(HasMethod(syncState, "GetLatestState", 1), "MultiplayerSyncState.GetLatestState(type)");
                Check(HasMethod(syncState, "GetLatestTime", 0), "MultiplayerSyncState.GetLatestTime()");
            }

            TypeDefinition scoreTable = Type(bgnet, "StandardScoreSyncState");
            Check(scoreTable != null, "BGNetCore.StandardScoreSyncState exists");

            TypeDefinition score = Type(bgnet, "StandardScoreSyncState/Score");
            Check(score != null, "BGNetCore.StandardScoreSyncState.Score exists");
            if (score != null)
            {
                foreach (string field in new[] { "ModifiedScore", "MultipliedScore", "ImmediateMaxPossibleMultipliedScore" })
                {
                    Check(score.Fields.Any(f => f.Name == field), "Score." + field);
                }
            }

            TypeDefinition players = Type(main, "MultiplayerPlayersManager");
            Check(players != null, "Main.MultiplayerPlayersManager exists");
            if (players != null)
            {
                Check(HasProperty(players, "localPlayerTransform"), "MultiplayerPlayersManager.localPlayerTransform");
                Check(HasProperty(players, "playerSpawningFinished"), "MultiplayerPlayersManager.playerSpawningFinished");
                Check(players.Events.Any(e => e.Name == "playerDidFinishEvent"),
                      "MultiplayerPlayersManager.playerDidFinishEvent (authoritative local failure)");
                Check(players.Events.Any(e => e.Name == "playerNetworkDidFailedEvent"),
                      "MultiplayerPlayersManager.playerNetworkDidFailedEvent");
            }

            TypeDefinition completion = Type(bgnet, "MultiplayerLevelCompletionResults");
            Check(completion != null, "BGNetCore.MultiplayerLevelCompletionResults exists");

            TypeDefinition localScore = Type(main, "ScoreController");
            Check(localScore != null, "Main.ScoreController exists (live local score)");
            if (localScore != null)
            {
                foreach (string member in new[] { "modifiedScore", "multipliedScore", "immediateMaxPossibleMultipliedScore" })
                {
                    Check(HasProperty(localScore, member), "ScoreController." + member);
                }
            }

            TypeDefinition coreHud = Type(main, "CoreGameHUDController");
            Check(coreHud != null, "Main.CoreGameHUDController exists (timer anchor)");
            if (coreHud != null)
            {
                Check(HasProperty(coreHud, "songProgressPanelGO"), "CoreGameHUDController.songProgressPanelGO");
            }

            TypeDefinition combo = Type(main, "ComboUIController");
            Check(combo != null, "Main.ComboUIController exists (fallback anchor)");
            if (combo != null)
            {
                Check(combo.Fields.Any(f => f.Name == "_comboText"), "ComboUIController._comboText");
            }

            TypeDefinition provider = Type(main, "MultiplayerScoreProvider");
            Check(provider != null, "Main.MultiplayerScoreProvider exists (score source)");
            if (provider != null)
            {
                Check(provider.Fields.Any(f => f.Name == "_scoreSyncStateManager"), "MultiplayerScoreProvider._scoreSyncStateManager");
                Check(HasMethod(provider, "TryGetScore", 2), "MultiplayerScoreProvider.TryGetScore(userId, out data)");

                TypeDefinition rankedPlayer = Type(main, "MultiplayerScoreProvider/RankedPlayer");
                Check(rankedPlayer != null, "MultiplayerScoreProvider.RankedPlayer exists");
                if (rankedPlayer != null)
                {
                    Check(HasProperty(rankedPlayer, "score"), "MultiplayerScoreProvider.RankedPlayer.score");
                    Check(HasProperty(rankedPlayer, "userId"), "MultiplayerScoreProvider.RankedPlayer.userId");
                    Check(rankedPlayer.Fields.Any(f => f.Name == "_multiplayerSyncState"),
                          "MultiplayerScoreProvider.RankedPlayer._multiplayerSyncState (accuracy fields)");
                }
            }

            AssemblyDefinition tmp = Load(resolver, managed, ipaManaged, plugins, "Unity.TextMeshPro");
            Check(Type(tmp, "TMPro.TextMeshProUGUI") != null, "TMPro.TextMeshProUGUI exists");
        }

        private static AssemblyDefinition Load(DefaultAssemblyResolver resolver, string managed, string ipaManaged,
                                               string plugins, string name)
        {
            string file = FindAssembly(name, managed, ipaManaged, plugins);
            if (file == null)
            {
                Fail("cannot find assembly " + name);
                return null;
            }

            try
            {
                return AssemblyDefinition.ReadAssembly(file, new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadSymbols = false,
                    InMemory = true
                });
            }
            catch (Exception ex)
            {
                Fail("cannot read " + name + ": " + ex.Message);
                return null;
            }
        }

        private static TypeDefinition Type(AssemblyDefinition assembly, string fullName)
        {
            if (assembly == null)
            {
                return null;
            }

            return assembly.MainModule.GetType(fullName) ??
                   assembly.MainModule.Types.FirstOrDefault(t => t.FullName == fullName);
        }

        private static bool HasProperty(TypeDefinition type, string name)
        {
            return type.Properties.Any(p => p.Name == name);
        }

        private static bool HasMethod(TypeDefinition type, string name, int parameterCount)
        {
            return type.Methods.Any(m => m.Name == name && m.Parameters.Count == parameterCount);
        }

        private static void CheckForbiddenNames(ModuleDefinition module)
        {
            Section("Standalone / no other plugin");

            string[] forbidden = { "KickMod", "PingMod", "SiraUtil", "BS_Utils", "0Harmony" };
            foreach (string name in forbidden)
            {
                Check(module.AssemblyReferences.All(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)),
                      "does not reference " + name);
            }

            Check(module.AssemblyReferences.All(r => !r.Name.Contains("Harmony", StringComparison.OrdinalIgnoreCase)),
                  "installs no Harmony dependency");
        }

        private static void CheckEmbeddedStrings(ModuleDefinition module)
        {
            Section("Portability");

            Regex absoluteWindows = new Regex(@"[A-Za-z]:\\", RegexOptions.Compiled);
            List<string> offenders = new List<string>();

            foreach (TypeDefinition type in AllTypes(module))
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code != Code.Ldstr)
                        {
                            continue;
                        }

                        string value = instruction.Operand as string;
                        if (string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        if (absoluteWindows.IsMatch(value))
                        {
                            offenders.Add(type.FullName + " -> " + value);
                        }
                    }
                }
            }

            Check(offenders.Count == 0, "no absolute Windows path in emitted strings");
            foreach (string offender in offenders)
            {
                Fail("embedded path string: " + offender);
            }
        }

        private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            foreach (TypeDefinition type in module.Types)
            {
                yield return type;

                foreach (TypeDefinition nested in type.NestedTypes)
                {
                    yield return nested;
                }
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title);
        }

        private static void Info(string message)
        {
            Console.WriteLine("   " + message);
        }

        private static void Check(bool isTrue, string description)
        {
            _checks++;
            if (isTrue)
            {
                Console.WriteLine("  [ok]   " + description);
                return;
            }

            _failures++;
            Console.WriteLine("  [FAIL] " + description);
        }

        private static void Fail(string message)
        {
            _checks++;
            _failures++;
            Console.WriteLine("  [FAIL] " + message);
        }

        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string Sha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
