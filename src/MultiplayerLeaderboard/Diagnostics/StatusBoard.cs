using System;
using System.Text;

namespace MultiplayerLeaderboard.Diagnostics
{
    /// <summary>
    /// The mod's connection / initialization diagnostics. Every value is set from a fact that was
    /// actually observed: nothing is reported as connected or available before it is. A status block is
    /// written only when at least one value changes, so the log stays readable.
    /// </summary>
    internal static class StatusBoard
    {
        private static string _initialization = "PENDING";
        private static string _game = "NOT DETECTED";
        private static string _multiplayerSystem = "NOT DETECTED (the game has not created it yet)";
        private static string _session = "WAITING (not in a multiplayer lobby)";
        private static string _leaderboard = "WAITING (the mod is loaded and idle)";
        private static string _playerData = "WAITING";
        private static string _signature;
        private static int _suspended;
        private static bool _dirty;

        /// <summary>Groups several value changes into one status block (used when one event changes many rows).</summary>
        internal static void Suspend() { _suspended++; }

        internal static void Resume()
        {
            if (_suspended > 0)
            {
                _suspended--;
            }

            Flush();
        }

        internal static void Initialization(string value) { Set(ref _initialization, value); }
        internal static void Game(string value) { Set(ref _game, value); }
        internal static void MultiplayerSystem(string value) { Set(ref _multiplayerSystem, value); }
        internal static void Session(string value) { Set(ref _session, value); }
        internal static void Leaderboard(string value) { Set(ref _leaderboard, value); }
        internal static void PlayerData(string value) { Set(ref _playerData, value); }

        private static void Set(ref string field, string value)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            _dirty = true;
            Flush();
        }

        internal static void Flush(bool force = false)
        {
            if (_suspended > 0)
            {
                return;
            }

            if (!force && !_dirty)
            {
                return;
            }

            string signature = string.Join("|", new[]
            {
                _initialization,
                _game,
                _multiplayerSystem,
                _session,
                _leaderboard,
                _playerData
            });

            if (!force && string.Equals(signature, _signature, StringComparison.Ordinal))
            {
                return;
            }

            _signature = signature;
            _dirty = false;

            // One line: the same six facts, but without a six-line block per state change.
            StringBuilder builder = new StringBuilder(256);
            builder.Append("STATUS: init=").Append(_initialization)
                   .Append(" | game=").Append(_game)
                   .Append(" | multiplayer=").Append(_multiplayerSystem)
                   .Append(" | session=").Append(_session)
                   .Append(" | leaderboard=").Append(_leaderboard)
                   .Append(" | playerdata=").Append(_playerData);

            LeaderboardLog.Info(builder.ToString());
        }
    }
}
