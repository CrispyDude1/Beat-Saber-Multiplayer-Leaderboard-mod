using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MultiplayerLeaderboard.Diagnostics;

namespace MultiplayerLeaderboard.Gameplay
{
    /// <summary>
    /// Reads every connected player's live score and accuracy from the game's own multiplayer score sync.
    ///
    /// The local player's row uses the live ScoreController, because that is the value the game shows on
    /// the local HUD. Remote rows use the game's own MultiplayerScoreProvider, which is the same source its
    /// multiplayer leaderboard and duel score display use. Their accuracy comes from that provider's own
    /// per-player sync state: the remote client's multiplied score divided by the maximum multiplied score
    /// reachable for the notes it has already played. Neither value is estimated locally.
    ///
    /// Remote rows deliberately read the latest received state instead of a time-aligned interpolation:
    /// the provider already supplies the game's display-aligned score, and the latest packet is the most
    /// recent accuracy data. A remote player with no packet yet has no accuracy to report.
    /// </summary>
    internal static class ScoreReader
    {
        private static FieldInfo _rankedSyncStateField;
        private static bool _rankedSyncStateLookupDone;
        private static readonly HashSet<string> _loggedMissingRemoteAccuracy = new HashSet<string>();
        private static readonly List<MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>>
            StateBuffer = new List<MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>>(16);
        private static readonly Dictionary<string, MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>>
            ProviderStates = new Dictionary<string, MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>>(16);

        internal static void BeginSong()
        {
            EndSong();
        }

        internal static void EndSong()
        {
            _loggedMissingRemoteAccuracy.Clear();
            StateBuffer.Clear();
            ProviderStates.Clear();
        }

        internal static bool TryRead(IScoreSyncStateManager manager, MultiplayerScoreProvider provider,
                                     ScoreController localScore, bool localFailureConfirmed,
                                     bool allowRemoteFailure, MistakeTracker mistakeTracker, List<PlayerRow> rows,
                                     Dictionary<string, PlayerRow> rowCache, out string problem)
        {
            rows.Clear();
            problem = null;

            if (manager == null)
            {
                problem = "the game's multiplayer score sync system is not present";
                return false;
            }

            int count;
            try
            {
                count = manager.connectedPlayerCount;
            }
            catch (Exception ex)
            {
                problem = "reading the player count failed: " + ex.GetType().Name;
                return false;
            }

            if (count <= 0)
            {
                problem = "the score sync system reports no players yet";
                return false;
            }

            List<MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>> states = StateBuffer;
            states.Clear();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> state =
                        manager.GetSyncState(i);
                    if (state != null && state.player != null)
                    {
                        states.Add(state);
                    }
                }
                catch (Exception ex)
                {
                    LeaderboardLog.Debug("Score state " + i + " could not be read: " + ex.GetType().Name);
                }
            }

            if (states.Count == 0)
            {
                problem = "no player score states are available yet";
                return false;
            }

            try
            {
                for (int i = 0; i < states.Count; i++)
                {
                    MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> state = states[i];
                    PlayerRow row = ReadRow(state, provider, localScore, localFailureConfirmed, allowRemoteFailure,
                                            mistakeTracker, rowCache);
                    if (row != null)
                    {
                        rows.Add(row);
                    }
                }
            }
            finally
            {
                // The sync-state objects belong to the current scene. Do not retain them after the row
                // data has been copied, or a static buffer would keep the old gameplay scene alive.
                states.Clear();
            }

            if (rows.Count == 0)
            {
                problem = "every player score state was unreadable";
                return false;
            }

            Rank(rows);
            return true;
        }

        private static PlayerRow ReadRow(MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> state,
                                         MultiplayerScoreProvider provider, ScoreController localScore,
                                         bool localFailureConfirmed, bool allowRemoteFailure,
                                         MistakeTracker mistakeTracker,
                                         Dictionary<string, PlayerRow> rowCache)
        {
            try
            {
                IConnectedPlayer player = state.player;
                if (player == null)
                {
                    return null;
                }

                string userId = SafeUserId(player);
                string userName = SafeName(player);
                string cacheKey = !string.IsNullOrEmpty(userId)
                    ? userId
                    : (!string.IsNullOrEmpty(userName) ? userName : "#" + SafeSortIndex(player).ToString(
                        System.Globalization.CultureInfo.InvariantCulture));

                if (!player.isConnected)
                {
                    // Gone from the session: the next scan will no longer include it either.
                    return null;
                }

                bool participant = false;
                bool stateSaysFailed = false;
                bool isMe = SafeIsMe(player);
                try
                {
                    participant = player.WasActiveAtLevelStart();
                    if (!isMe)
                    {
                        stateSaysFailed = player.IsFailed();
                    }
                }
                catch (Exception ex)
                {
                    LeaderboardLog.Debug("Player state could not be read for \"" + SafeName(player) + "\": " + ex.GetType().Name);
                }

                // IConnectedPlayer.IsFailed() is derived from the multiplayer "is_active" state: it
                // returns true whenever a player was active at level start but is not currently active
                // or finished. That state is not a failure event and can be absent during the opening
                // moments of a song, so using it for the local player produced a false "FAILED" row at
                // zero score. The local player's real result comes from MultiplayerPlayersManager's
                // playerDidFinishEvent instead. For remote players keep the game's state, but wait a
                // moment for their first state packet before calling an empty row a failure.
                bool failed = isMe ? localFailureConfirmed : (stateSaysFailed && allowRemoteFailure);
                bool useLiveLocalScore = isMe && participant && localScore != null;

                int modifiedScore;
                int multipliedScore;
                int maxMultipliedScore;
                int combo;
                bool hasData;
                bool hasCombo;

                if (useLiveLocalScore)
                {
                    // The local player's synced row can stay at 0 for the whole song even though the game
                    // HUD is scoring: read the live ScoreController that the game itself displays instead.
                    modifiedScore = localScore.modifiedScore;
                    multipliedScore = localScore.multipliedScore;
                    maxMultipliedScore = localScore.immediateMaxPossibleMultipliedScore;
                    hasData = true;
                    int stateModified;
                    int stateMultiplied;
                    int stateMax;
                    hasCombo = TryReadLatest(state, out stateModified, out stateMultiplied, out stateMax, out combo);
                    if (!hasCombo)
                    {
                        // The local combo is zero until the first combo event reaches the sync state.
                        combo = 0;
                        hasCombo = true;
                    }
                }
                else
                {
                    // Remote rows use the game's own score provider. On a duel the player's score is
                    // displayed from this provider even when the score-state instance found by a scene
                    // scan has not received that player's deltas. Read the provider's own sync state for
                    // the accuracy denominator so both values come from the same packet stream.

                    // deep seek really sucks at this coding thing. enjoy its amazig comments and useless line of code just for state.
                    MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> remoteState =
                        state;
                    int providerScore = 0;
                    bool hasProviderRow = false;

                    if (!isMe && provider != null)
                    {
                        try
                        {
                            string remoteId = SafeUserId(player);
                            MultiplayerScoreProvider.RankedPlayer ranked;
                            if (provider.TryGetScore(remoteId, out ranked) && ranked != null)
                            {
                                hasProviderRow = true;
                                providerScore = Math.Max(0, ranked.score);
                                MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> providerState =
                                    CachedProviderState(remoteId, ranked);
                                if (providerState != null)
                                {
                                    remoteState = providerState;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LeaderboardLog.Debug("Remote score provider could not be read for \"" +
                                                 SafeName(player) + "\": " + ex.GetType().Name);
                        }
                    }

                    bool providerHasData = TryReadLatest(remoteState, out modifiedScore,
                                                         out multipliedScore, out maxMultipliedScore,
                                                         out combo);

                    int managerModified = 0;
                    int managerMultiplied = 0;
                    int managerMax = 0;
                    int managerCombo = 0;
                    bool managerHasData = false;
                    if (!ReferenceEquals(remoteState, state))
                    {
                        managerHasData = TryReadLatest(state, out managerModified,
                                                       out managerMultiplied, out managerMax,
                                                       out managerCombo);
                    }

                    if (!providerHasData && managerHasData)
                    {
                        combo = managerCombo;
                    }
                    if (managerHasData)
                    {
                        modifiedScore = Math.Max(modifiedScore, managerModified);
                        if (maxMultipliedScore <= 0 && managerMax > 0)
                        {
                            multipliedScore = managerMultiplied;
                            maxMultipliedScore = managerMax;
                        }
                    }

                    hasData = providerHasData || managerHasData || hasProviderRow;
                    hasCombo = providerHasData || managerHasData;
                    if (hasProviderRow)
                    {
                        modifiedScore = Math.Max(modifiedScore, providerScore);
                    }

                    if (!isMe && hasProviderRow && providerScore > 0 && maxMultipliedScore <= 0 &&
                        _loggedMissingRemoteAccuracy.Add(SafeUserId(player)))
                    {
                        LeaderboardLog.Warn("Remote score is available for \"" + SafeName(player) +
                                            "\", but its accuracy state has not arrived yet.");
                    }
                }

                PlayerRow row;
                if (rowCache == null || !rowCache.TryGetValue(cacheKey, out row) || row == null)
                {
                    row = new PlayerRow();
                    if (rowCache != null)
                    {
                        rowCache[cacheKey] = row;
                    }
                }

                row.UserId = userId;
                row.UserName = userName;
                row.CacheKey = cacheKey;
                row.SortIndex = SafeSortIndex(player);
                row.IsMe = isMe;
                row.IsParticipant = participant;
                row.IsFailed = failed;
                row.HasScoreData = hasData;
                row.Score = Math.Max(0, modifiedScore);
                row.HasAccuracy = false;
                row.Accuracy = 0f;
                row.Combo = Math.Max(0, combo);
                row.HasCombo = hasCombo;
                row.Mistakes = 0;
                row.HasMistakes = false;
                if (mistakeTracker != null)
                {
                    if (isMe)
                    {
                        mistakeTracker.SetLocalUser(userId);
                    }

                    row.Mistakes = mistakeTracker.GetCount(userId);
                    row.HasMistakes = mistakeTracker.IsTracking(isMe);
                }

                if (participant && !failed && hasData)
                {
                    if (maxMultipliedScore > 0)
                    {
                        double accuracy = (double)multipliedScore / maxMultipliedScore;
                        if (accuracy < 0d)
                        {
                            accuracy = 0d;
                        }
                        else if (accuracy > 1d)
                        {
                            accuracy = 1d;
                        }

                        row.Accuracy = (float)(accuracy * 100d);
                        row.HasAccuracy = true;
                    }
                    else if (modifiedScore == 0 && multipliedScore == 0)
                    {
                    // if score is zero, just put "-" instead of anything else. (i saw deepseeks god damn comment and had to change it. whatever deep seek model crispyflow was using to create this mode, REALLY hates the "less is more" format lmao)
                        row.Accuracy = 0f;
                        row.HasAccuracy = true;
                    }
                }

                return row;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("A player score row could not be built: " + ex.GetType().Name);
                return null;
            }
        }

        private static bool TryReadLatest(MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> state,
                                          out int modifiedScore, out int multipliedScore, out int maxMultipliedScore,
                                          out int combo)
        {
            modifiedScore = 0;
            multipliedScore = 0;
            maxMultipliedScore = 0;
            combo = 0;

            try
            {
                if (state.GetLatestTime() <= 0)
                {
                    return false;
                }

                modifiedScore = state.GetLatestState(StandardScoreSyncState.Score.ModifiedScore);
                multipliedScore = state.GetLatestState(StandardScoreSyncState.Score.MultipliedScore);
                maxMultipliedScore = state.GetLatestState(StandardScoreSyncState.Score.ImmediateMaxPossibleMultipliedScore);
                combo = state.GetLatestState(StandardScoreSyncState.Score.Combo);
                return true;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading remote score fields failed: " + ex.GetType().Name);
                return false;
            }
        }

        private static MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>
            CachedProviderState(string userId, MultiplayerScoreProvider.RankedPlayer ranked)
        {
            MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> cached;
            if (!string.IsNullOrEmpty(userId) && ProviderStates.TryGetValue(userId, out cached) && cached != null)
            {
                return cached;
            }

            MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int> found =
                ProviderSyncState(ranked);
            if (found != null && !string.IsNullOrEmpty(userId))
            {
                ProviderStates[userId] = found;
            }

            return found;
        }

        private static MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>
            ProviderSyncState(MultiplayerScoreProvider.RankedPlayer ranked)
        {
            if (!_rankedSyncStateLookupDone)
            {
                _rankedSyncStateLookupDone = true;
                _rankedSyncStateField = typeof(MultiplayerScoreProvider.RankedPlayer).GetField(
                    "_multiplayerSyncState", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (_rankedSyncStateField == null)
            {
                return null;
            }

            try
            {
                return _rankedSyncStateField.GetValue(ranked) as
                       MultiplayerSyncState<StandardScoreSyncState, StandardScoreSyncState.Score, int>;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Provider score state could not be read: " + ex.GetType().Name);
                return null;
            }
        }

        private static void Rank(List<PlayerRow> rows)
        {
            rows.Sort(Compare);

            int rank = 1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsParticipant)
                {
                    rows[i].Rank = rank++;
                }
                else
                {
                    rows[i].Rank = 0;
                }
            }
        }

        /// <summary>
        /// Participants first (failed ones after everyone still playing, exactly like the game's own board),
        /// then by live score, then by lobby join order so equal scores do not swap places every update.
        /// Spectators who joined after the level started follow with no rank.
        /// </summary>
        private static int Compare(PlayerRow a, PlayerRow b)
        {
            if (a.IsParticipant != b.IsParticipant)
            {
                return a.IsParticipant ? -1 : 1;
            }

            if (a.IsFailed != b.IsFailed)
            {
                return a.IsFailed ? 1 : -1;
            }

            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            int byIndex = a.SortIndex.CompareTo(b.SortIndex);
            if (byIndex != 0)
            {
                return byIndex;
            }

            return string.CompareOrdinal(a.DisplayName, b.DisplayName);
        }

        private static string SafeName(IConnectedPlayer player)
        {
            try
            {
                return player.userName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeUserId(IConnectedPlayer player)
        {
            try
            {
                return player.userId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int SafeSortIndex(IConnectedPlayer player)
        {
            try
            {
                return player.sortIndex;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        private static bool SafeIsMe(IConnectedPlayer player)
        {
            try
            {
                return player.isMe;
            }
            catch
            {
                return false;
            }
        }

        internal static string Describe(IReadOnlyList<PlayerRow> rows)
        {
            StringBuilder builder = new StringBuilder(rows.Count * 28);
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                PlayerRow row = rows[i];
                builder.Append(row.RankText).Append(' ')
                       .Append(row.DisplayName).Append(' ')
                       .Append(row.AccuracyText).Append(' ')
                       .Append(row.ScoreText);
            }

            return builder.ToString();
        }
    }
}
