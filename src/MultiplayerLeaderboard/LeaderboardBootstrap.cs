using System;
using System.IO;
using System.Reflection;
using System.Text;
using MultiplayerLeaderboard.Diagnostics;
using UnityEngine;

namespace MultiplayerLeaderboard
{
    /// <summary>
    /// One-time startup: independent diagnostics, environment reporting, then the world-space leaderboard
    /// runtime. There are no Harmony patches and no dependency on any other mod, so nothing here can fail
    /// because of what else is (or is not) installed.
    /// </summary>
    internal static class LeaderboardBootstrap
    {
        internal const string ExpectedGameVersion = "1.40.3";

        private static GameObject _hostObject;

        internal static string GameRootPath => ResolveGameRoot();

        internal static string Version => typeof(LeaderboardBootstrap).Assembly.GetName().Version.ToString(3);

        public static void Run()
        {
            LeaderboardLog.SetMirror(Mirror);

            string gameRoot = ResolveGameRoot();
            string logDirectory = Path.Combine(gameRoot, "Logs", "MultiplayerLeaderboard");
            bool logOk = LeaderboardLog.Initialize(logDirectory);

            LeaderboardLog.Banner("Multiplayer Leaderboard " + Version + " - diagnostics");
            if (logOk)
            {
                LeaderboardLog.Info("Log file: " + LeaderboardLog.FilePath);
            }
            else
            {
                LeaderboardLog.Error("Could not create the log file under " + logDirectory +
                                     "; diagnostics continue in the mod-loader log only.");
            }

            LeaderboardLog.Info("DLL successfully loaded (assembly: " + SafeAssemblyLocation() + "); game " +
                                DetectGameVersion() + ", Unity " + SafeUnityVersion() + ", BSIPA " +
                                SafeVersion(typeof(IPA.PluginAttribute).Assembly) + " (no Harmony patches).");
            LeaderboardLog.Info("Game assemblies in use: BGNetCore.dll, Main.dll, Unity.TextMeshPro.dll, " +
                                "UnityEngine.UI.dll; standalone (no dependency on any other plugin).");

            LogCompatibility();
            LogGameSystemDetection();

            StatusBoard.Suspend();
            StatusBoard.Game("CONNECTED (" + DetectGameVersion() + ")");
            StatusBoard.MultiplayerSystem("NOT DETECTED (the game has not created it yet)");
            StatusBoard.Session("WAITING (not in a multiplayer lobby)");
            StatusBoard.Leaderboard("READY (waiting for a multiplayer session)");
            StatusBoard.PlayerData("WAITING");
            StatusBoard.Initialization("SUCCESS");
            StatusBoard.Resume();

            CreateHostObject();

            LeaderboardLog.Info("Multiplayer Leaderboard initialized successfully; it stays loaded while waiting for a lobby.");
        }

        private static void CreateHostObject()
        {
            try
            {
                _hostObject = new GameObject("MultiplayerLeaderboard.Runtime");
                UnityEngine.Object.DontDestroyOnLoad(_hostObject);
                _hostObject.AddComponent<LeaderboardRuntime>();
                _hostObject.AddComponent<UI.LeaderboardMenuIntegration>();
                if (UI.LeaderboardUiProbe.IsRequested)
                {
                    _hostObject.AddComponent<UI.LeaderboardUiProbe>();
                    LeaderboardLog.Info("UI probe requested by marker file; settings navigation will be exercised automatically.");
                }
                LeaderboardLog.Info("Runtime component created (idle until a multiplayer song starts).");
                LeaderboardLog.Info("Waiting for BSML's Mods pane, then registering the settings button.");
            }
            catch (Exception ex)
            {
                StatusBoard.Initialization("FAILED (runtime component could not be created)");
                LeaderboardLog.Error("Could not create the runtime component; the leaderboard is disabled.", ex);
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (_hostObject != null)
                {
                    UnityEngine.Object.Destroy(_hostObject);
                    _hostObject = null;
                }
            }
            catch
            {
                // ignored
            }

            LeaderboardLog.Info("Multiplayer Leaderboard shutting down.");
            LeaderboardLog.Close();
        }

        private static void LogCompatibility()
        {
            string unityVersion = SafeUnityVersion();
            if (unityVersion.StartsWith(ExpectedGameVersion, StringComparison.Ordinal))
            {
                LeaderboardLog.Info("Compatibility check: running " + unityVersion + ", built for " + ExpectedGameVersion +
                                    " - supported.");
                return;
            }

            LeaderboardLog.Warn("Compatibility check: running " + unityVersion + ", but this build was made for Beat Saber " +
                                ExpectedGameVersion + ". The mod will still try to run, but multiplayer members it relies on " +
                                "may have changed.");
        }

        private static void LogGameSystemDetection()
        {
            StringBuilder builder = new StringBuilder(320);
            AppendType(builder, typeof(MultiplayerSessionManager), "session");
            AppendType(builder, typeof(IScoreSyncStateManager), "score sync");
            AppendType(builder, typeof(StandardScoreSyncState), "sync state");
            AppendType(builder, typeof(MultiplayerPlayersManager), "player placement");
            AppendType(builder, typeof(TMPro.TextMeshProUGUI), "TextMeshPro");
            LeaderboardLog.Info("Detected game systems: " + builder + ".");
        }

        private static void AppendType(StringBuilder builder, Type type, string label)
        {
            try
            {
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(label).Append('=').Append(type.FullName).Append(" (" +
                     type.Assembly.GetName().Name + ".dll)");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Warn("Could not inspect " + label + ": " + ex.GetType().Name);
            }
        }

        private static void Mirror(LogLevel level, string message)
        {
            IPA.Logging.Logger logger = Plugin.Log;
            if (logger == null)
            {
                return;
            }

            switch (level)
            {
                case LogLevel.Debug:
                    logger.Debug(message);
                    break;
                case LogLevel.Warn:
                    logger.Warn(message);
                    break;
                case LogLevel.Error:
                    logger.Error(message);
                    break;
                default:
                    logger.Info(message);
                    break;
            }
        }

        private static string DetectGameVersion()
        {
            string contentVersion = TryReadContentVersion();
            if (string.IsNullOrEmpty(contentVersion))
            {
                return SafeUnityVersion();
            }

            return contentVersion + " (Application.version=" + SafeUnityVersion() + ")";
        }

        /// <summary>
        /// Reads BeatSaber.Init.GameVersionProvider.GetContentVersion() through reflection, so the plugin
        /// carries no compile-time reference to BeatSaber.Init.dll.
        /// </summary>
        private static string TryReadContentVersion()
        {
            try
            {
                Type provider = Type.GetType("BeatSaber.Init.GameVersionProvider, BeatSaber.Init", false);
                MethodInfo getter = provider?.GetMethod("GetContentVersion", BindingFlags.Public | BindingFlags.Static);
                object version = getter?.Invoke(null, null);
                string text = version?.ToString();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeUnityVersion()
        {
            try
            {
                return Application.version;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeVersion(Assembly assembly)
        {
            try
            {
                return assembly.GetName().Version.ToString(3);
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeAssemblyLocation()
        {
            try
            {
                return typeof(LeaderboardBootstrap).Assembly.Location;
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Locates the game root without any hardcoded path: the current directory when it contains
        /// Beat Saber.exe, otherwise the parent of the Plugins folder the DLL was loaded from. This is
        /// what makes the same DLL write its log correctly in someone else's installation.
        /// </summary>
        private static string ResolveGameRoot()
        {
            try
            {
                string current = Environment.CurrentDirectory;
                if (!string.IsNullOrEmpty(current) && File.Exists(Path.Combine(current, "Beat Saber.exe")))
                {
                    return current;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                string assemblyDirectory = Path.GetDirectoryName(typeof(LeaderboardBootstrap).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDirectory))
                {
                    DirectoryInfo parent = Directory.GetParent(assemblyDirectory);
                    if (parent != null)
                    {
                        return parent.FullName;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return Environment.CurrentDirectory;
        }
    }
}
