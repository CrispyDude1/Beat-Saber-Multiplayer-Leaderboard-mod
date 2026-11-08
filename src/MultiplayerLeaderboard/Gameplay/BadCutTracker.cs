using System;
using System.Collections.Generic;
using System.Reflection;
using MultiplayerLeaderboard.Diagnostics;
using UnityEngine;

namespace MultiplayerLeaderboard.Gameplay
{
    /// <summary>
    /// Tracks each player's mistakes from the game's own gameplay events. A mistake is a missed
    /// block, a bad cut, or a bomb hit. The local player is read from ScoreController's scoring
    /// events for misses/bad cuts and from the local BeatmapObjectManager for bomb hits; remote
    /// players are read from the multiplayer note-cut and note-miss RPCs. No value is inferred from
    /// score changes, and combo is deliberately not modified here.
    /// </summary>
    internal sealed class MistakeTracker
    {
        private const int RpcScanLimit = 4;
        private const float RpcScanInterval = 2f;

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(16);
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly List<string> _pruneBuffer = new List<string>(16);

        private IGameplayRpcManager _rpc;
        private ScoreController _localScore;
        private BeatmapObjectManager _localBeatmap;
        private string _localUserId = string.Empty;
        private float _nextRpcScan;
        private int _rpcScans;
        private bool _rpcUnavailableLogged;
        private bool _rpcSubscribed;
        private bool _localSubscribed;
        private bool _localBeatmapSubscribed;
        private static FieldInfo _beatmapField;
        private static bool _beatmapFieldLookupDone;

        internal void BeginSong(MultiplayerSessionManager session)
        {
            EndSong();

            try
            {
                if (session != null && session.localPlayer != null)
                {
                    _localUserId = session.localPlayer.userId ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Local player id for mistake tracking could not be read: " + ex.GetType().Name);
            }

            TryResolveRpc();
        }

        internal void Update()
        {
            if (_rpc != null || _rpcScans >= RpcScanLimit || Time.unscaledTime < _nextRpcScan)
            {
                return;
            }

            _nextRpcScan = Time.unscaledTime + RpcScanInterval;
            TryResolveRpc();
        }

        internal void SetLocalScore(ScoreController score)
        {
            if (ReferenceEquals(_localScore, score))
            {
                if (score != null && _localBeatmap == null)
                {
                    TryResolveLocalBeatmap();
                }

                return;
            }

            UnsubscribeLocal();
            _localScore = score;
            if (_localScore != null)
            {
                _localScore.scoringForNoteStartedEvent += HandleLocalScore;
                _localSubscribed = true;
                TryResolveLocalBeatmap();
            }
        }

        internal void SetLocalUser(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                _localUserId = userId;
            }
        }

        internal int GetCount(string userId)
        {
            int count;
            return !string.IsNullOrEmpty(userId) && _counts.TryGetValue(userId, out count) ? count : 0;
        }

        internal bool IsTracking(bool isMe)
        {
            return isMe ? _localScore != null : _rpc != null;
        }

        internal void Prune(List<PlayerRow> rows)
        {
            _seen.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].UserId))
                {
                    _seen.Add(rows[i].UserId);
                }
            }

            if (_seen.Count == 0)
            {
                return;
            }

            _pruneBuffer.Clear();
            foreach (KeyValuePair<string, int> pair in _counts)
            {
                if (!_seen.Contains(pair.Key) && !string.Equals(pair.Key, _localUserId, StringComparison.Ordinal))
                {
                    _pruneBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pruneBuffer.Count; i++)
            {
                _counts.Remove(_pruneBuffer[i]);
            }
        }

        internal void EndSong()
        {
            UnsubscribeLocal();

            if (_rpc != null && _rpcSubscribed)
            {
                try
                {
                    _rpc.noteWasCutEvent -= HandleRemoteCut;
                    _rpc.noteWasMissedEvent -= HandleRemoteMiss;
                }
                catch
                {
                    // The gameplay RPC manager is already being torn down.
                }
            }

            _rpc = null;
            _rpcSubscribed = false;
            _localScore = null;
            _localBeatmap = null;
            _localBeatmapSubscribed = false;
            _localUserId = string.Empty;
            _nextRpcScan = 0f;
            _rpcScans = 0;
            _rpcUnavailableLogged = false;
            _counts.Clear();
            _seen.Clear();
            _pruneBuffer.Clear();
        }

        private void HandleLocalScore(ScoringElement element)
        {
            if (element == null || element.noteData == null ||
                element.noteData.gameplayType == NoteData.GameplayType.Bomb)
            {
                return;
            }

            if (element is BadCutScoringElement || element is MissScoringElement)
            {
                Add(_localUserId);
            }
        }

        private void HandleLocalBombCut(NoteController noteController, in NoteCutInfo cutInfo)
        {
            if (cutInfo.noteData != null && cutInfo.noteData.gameplayType == NoteData.GameplayType.Bomb)
            {
                Add(_localUserId);
            }
        }

        private void HandleRemoteCut(string userId, long syncTime, float songTime,
                                     NoteCutInfoNetSerializable cutInfo)
        {
            if (cutInfo == null || IsLocalUser(userId))
            {
                return;
            }

            if (cutInfo.gameplayType == NoteData.GameplayType.Bomb ||
                cutInfo.colorType == ColorType.None)
            {
                // A bomb cut is a mistake regardless of cutWasOk.
                Add(userId);
                return;
            }

            if (!cutInfo.cutWasOk)
            {
                Add(userId);
            }
        }

        private void HandleRemoteMiss(string userId, long syncTime, float songTime,
                                      NoteMissInfoNetSerializable missInfo)
        {
            if (missInfo == null || missInfo.colorType == ColorType.None || IsLocalUser(userId))
            {
                // Bombs that pass the player are not mistakes.
                return;
            }

            Add(userId);
        }

        private bool IsLocalUser(string userId)
        {
            return !string.IsNullOrEmpty(_localUserId) &&
                   string.Equals(userId, _localUserId, StringComparison.Ordinal);
        }

        private void Add(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            int count;
            _counts.TryGetValue(userId, out count);
            _counts[userId] = count + 1;
        }

        private void TryResolveRpc()
        {
            if (_rpc != null)
            {
                return;
            }

            _rpcScans++;

            try
            {
                MultiplayerController[] controllers = Resources.FindObjectsOfTypeAll<MultiplayerController>();
                if (controllers != null)
                {
                    for (int i = 0; i < controllers.Length; i++)
                    {
                        IGameplayRpcManager rpc = ReadRpc(controllers[i], "_gameplayRpcManager");
                        if (rpc != null)
                        {
                            Attach(rpc);
                            return;
                        }
                    }
                }

                MultiplayerLocalActiveClient[] clients =
                    Resources.FindObjectsOfTypeAll<MultiplayerLocalActiveClient>();
                if (clients != null)
                {
                    for (int i = 0; i < clients.Length; i++)
                    {
                        IGameplayRpcManager rpc = ReadRpc(clients[i], "_rpcManager");
                        if (rpc != null)
                        {
                            Attach(rpc);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Mistake RPC lookup failed: " + ex.GetType().Name);
            }

            if (!_rpcUnavailableLogged && _rpcScans >= 3)
            {
                _rpcUnavailableLogged = true;
                LeaderboardLog.Info("Mistake tracking: the multiplayer note event RPC is not available yet; " +
                                    "remote mistake counts will show '-' until it is found.");
            }
        }

        private void Attach(IGameplayRpcManager rpc)
        {
            _rpc = rpc;
            _rpc.noteWasCutEvent += HandleRemoteCut;
            _rpc.noteWasMissedEvent += HandleRemoteMiss;
            _rpcSubscribed = true;
            LeaderboardLog.Info("Mistake tracking: connected to the game's multiplayer note events " +
                                "(misses and bad cuts from the RPC, bombs from cut colour).");
        }

        private void TryResolveLocalBeatmap()
        {
            if (_localScore == null || _localBeatmap != null)
            {
                return;
            }

            try
            {
                if (!_beatmapFieldLookupDone)
                {
                    _beatmapFieldLookupDone = true;
                    _beatmapField = typeof(ScoreController).GetField(
                        "_beatmapObjectManager", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (_beatmapField == null)
                {
                    return;
                }

                BeatmapObjectManager manager = _beatmapField.GetValue(_localScore) as BeatmapObjectManager;
                if (manager == null)
                {
                    return;
                }

                _localBeatmap = manager;
                _localBeatmap.noteWasCutEvent += HandleLocalBombCut;
                _localBeatmapSubscribed = true;
                LeaderboardLog.Debug("Mistake tracking: local bomb hits are read from the gameplay beatmap manager.");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Local beatmap manager lookup for mistake tracking failed: " + ex.GetType().Name);
            }
        }

        private static IGameplayRpcManager ReadRpc(object owner, string fieldName)
        {
            if (owner == null)
            {
                return null;
            }

            try
            {
                FieldInfo field = owner.GetType().GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return field != null ? field.GetValue(owner) as IGameplayRpcManager : null;
            }
            catch
            {
                return null;
            }
        }

        private void UnsubscribeLocal()
        {
            if (_localScore != null && _localSubscribed)
            {
                try
                {
                    _localScore.scoringForNoteStartedEvent -= HandleLocalScore;
                }
                catch
                {
                    // The gameplay scene is already being torn down.
                }
            }

            if (_localBeatmap != null && _localBeatmapSubscribed)
            {
                try
                {
                    _localBeatmap.noteWasCutEvent -= HandleLocalBombCut;
                }
                catch
                {
                    // The gameplay scene is already being torn down.
                }
            }

            _localSubscribed = false;
            _localBeatmapSubscribed = false;
            _localBeatmap = null;
        }
    }
}
