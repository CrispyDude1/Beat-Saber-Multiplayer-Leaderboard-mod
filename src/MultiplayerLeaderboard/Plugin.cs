using IPA;
using IPA.Logging;

namespace MultiplayerLeaderboard
{
    /// <summary>
    /// BSIPA entry point. This is a standalone mod: it needs only the mod loader and the game's own
    /// assemblies, installs no Harmony patches and references no other plugin.
    /// </summary>
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Logger Log { get; private set; }

        [Init]
        public Plugin(Logger logger)
        {
            Log = logger;
        }

        [OnStart]
        public void OnStart()
        {
            // The game's assemblies and Unity are loaded at this point.
            try
            {
                LeaderboardBootstrap.Run();
            }
            catch (System.Exception ex)
            {
                Diagnostics.LeaderboardLog.Error("Unhandled exception during startup; the mod is inert.", ex);
                Diagnostics.StatusBoard.Initialization("FAILED (see the log)");
                Log?.Error("Multiplayer Leaderboard startup failed: " + ex);
            }
        }

        [OnExit]
        public void OnExit()
        {
            try
            {
                LeaderboardBootstrap.Shutdown();
            }
            catch (System.Exception ex)
            {
                Log?.Error("Multiplayer Leaderboard shutdown failed: " + ex);
            }
        }
    }
}
