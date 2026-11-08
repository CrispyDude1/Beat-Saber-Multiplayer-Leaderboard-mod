using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using MultiplayerLeaderboard.Diagnostics;
using MultiplayerLeaderboard.Gameplay;
using MultiplayerLeaderboard.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MultiplayerLeaderboard
{
    /// <summary>
    /// Detects the game's multiplayer session and the multiplayer gameplay scene, creates the world-space
    /// leaderboard once per song at a fixed position, and keeps its rows updated from the game's own score
    /// sync state. Outside a multiplayer song it stays loaded and idle; that is a normal state, not an error.
    /// </summary>
    internal sealed class LeaderboardRuntime : MonoBehaviour
    {
        internal static LeaderboardRuntime Instance { get; private set; }

        internal UiSettings Settings => _uiSettings;
        internal WorldLeaderboardPanel Panel => _panel;
        internal bool HasPanel => _panel != null && _panel.IsAlive;
        internal bool IsSpectating => _spectatorAnchor != null;
        internal Vector3 RotationEuler
        {
            get
            {
                if (_panel != null)
                {
                    return _panel.RotationEuler;
                }

                return NormalizeEuler(_uiSettings.Rotation.eulerAngles);
            }
        }

        private const float TickInterval = 0.25f;

        // Rows are read at 4 Hz, but visible text is refreshed less often. Every label change costs a
        // TMP mesh regeneration plus a canvas rebuild on the frame it is applied; at 90 Hz the frame
        // budget has no headroom, so those frames become the 20-35 ms hitches. Measured while
        // spectating: 108 apply frames and 83 frames over 20 ms in 30 s. Spectating uses half the
        // normal rate because the panel fills more of the view and measured five times as many slow
        // frames there.
        private const float UiRefreshInterval = 0.5f;
        private const float SpectatorUiRefreshInterval = 1f;
        private const float ManagerScanInterval = 1f;

        // A full asset scan for the session manager costs about 6 ms in the menu/lobby (measured:
        // avg_update_ms 1.57 ms at four ticks per second, max 6.9 ms). Repeating it every second
        // while no session exists produced a visible spike once per second. The scan now backs off
        // to one attempt every four seconds; a session is still picked up long before a song starts.
        private const float ManagerScanMaxInterval = 4f;

        // The ranking line only changes when scores change places; a thirty-second check keeps the
        // order visible in the log without writing it five times a minute for the whole song.
        private const float RankingLogInterval = 30f;
        private const float LocalScoreRetryInterval = 2f;
        private const int LocalScoreFallbackScanLimit = 2;

        // Fallback panel placement, relative to the local player's spawn in the multiplayer play space.
        // Normally the panel is placed directly above the game's own combo counter instead. The panel is
        // placed once and then never moved: it is world space, not a head-locked HUD.
        private const float PanelForwardOffset = 2.00f;
        private const float PanelUpOffset = 2.05f;
        private const float LocalEyeHeight = 1.70f;

        // The spectator leaderboard is the normal panel, re-used and anchored to the selected
        // player's platform. The built-in spectator bar is the authoritative placement reference;
        // these fallbacks are used only if that native bar cannot be resolved.
        private const float SpectatorPanelFallbackDistance = 1.55f;
        private const float SpectatorPanelFallbackCenterHeight = 0.12f;
        private const float SpectatorPanelGapAboveNativeBar = 0.08f;
        private const float SpectatorPanelBackOffset = 0.18f;
        private const int NativeSpectatorInfoScanLimit = 2;
        private const float NativeSpectatorInfoRetryInterval = 1f;
        private const int SpectatorReferenceScanLimit = 4;
        private const int SpectatorBarCheckLimit = 4;
        private const float SpectatorOffsetSaveDelay = 0.4f;
        private const float SpectatorNudgeStep = 0.05f;

        private static FieldInfo _scoreSyncField;
        private static bool _scoreSyncFieldLookupDone;
        private static FieldInfo _comboTextField;
        private static bool _comboTextFieldLookupDone;

        private MultiplayerSessionManager _session;
        private IScoreSyncStateManager _scoreSync;
        private MultiplayerScoreProvider _scoreProvider;
        private MultiplayerPlayersManager _players;
        private ScoreController _localScore;
        private Camera _camera;
        private WorldLeaderboardPanel _panel;
        private MultiplayerSpectatingSpotPickerViewController _spectatorPicker;
        private MultiplayerSpectatorController _spectatorController;
        private MultiplayerLocalInactivePlayerInGameMenuViewController _spectatorMenu;
        private RectTransform _spectatorBar;
        private IMultiplayerSpectatingSpot _spectatorSpot;
        private Transform _spectatorAnchor;
        private Vector3 _spectatorHomePosition;
        private Quaternion _spectatorHomeRotation = Quaternion.identity;
        private Vector3 _spectatorBasePosition;
        private Quaternion _spectatorBaseRotation = Quaternion.identity;
        private GameObject _nativeSpectatorInfo;
        private int _nativeSpectatorInfoScans;
        private bool _nativeSpectatorInfoScanned;
        private int _spectatorReferenceScans;
        private int _spectatorBarChecks;
        private float _nextNativeSpectatorInfoScan;
        private float _nextSpectatorScan;
        private float _nextSpectatorBarCheck;
        private bool _spectatorPositionedFromNativeBar;
        private bool _spectatorOffsetDirty;
        private float _spectatorOffsetSaveAt;
        private long _lastRenderSignature;
        private bool _forceRender;
        private bool _rankFlashWasActive;
        private string _spectatorFooterName;
        private string _spectatorFooter;
        private static FieldInfo _spectatorMainBarField;
        private static FieldInfo _spectatingSpotPlayerField;
        private UiSettings _uiSettings;

        private float _nextTick;
        private float _nextManagerScan;
        private float _managerScanBackoff = ManagerScanInterval;
        private float _nextScoreScan;
        private float _scoreScanBackoff = ManagerScanInterval;
        private float _nextRankingLog;
        private float _nextUiRefresh;
        private float _nextCameraResolve;
        private float _nextStageWarning;
        private float _nextSessionPlayerScan;
        private float _nextLocalScoreScan;
        private int _localScoreFallbackScans;
        private string _lastSceneName;
        private string _lastPlayerSummary;
        private string _lastOrder;
        private string _lastRowProblem;
        private int _lastRowCount;
        private Camera _lastLoggedCamera;
        private int _cameraLogCount;
        private float _nextCameraWarning;
        private bool _loggedMissingNativeSpectatorInfo;
        private bool _loggedSessionDetected;
        private bool _loggedWaiting;
        private bool _loggedProviderScoreSource;
        private bool _loggedMissingScoreSource;
        private bool _loggedSongEntry;
        private float _sceneEnteredAt;
        private float _panelBuildRealtime;
        private bool _panelFirstViewLogged;
        private static readonly Plane[] _frustumPlanes = new Plane[6];
        private bool _loggedStageWait;
        private bool _loggedNonMultiplayerScoreSource;
        private bool _panelPreview;
        private float _songEntryTime;
        private MultiplayerPlayersManager _playersSubscribed;
        private bool _localFailureConfirmed;
        private bool _perfSongActive;
        private MistakeTracker _mistakes;

        private readonly HashSet<string> _loggedPlayerData = new HashSet<string>();
        private readonly Dictionary<string, string> _sessionNames = new Dictionary<string, string>();
        private readonly Dictionary<string, PlayerRow> _rowCache = new Dictionary<string, PlayerRow>(12);
        private readonly List<PlayerRow> _rowBuffer = new List<PlayerRow>(12);
        private readonly List<string> _sessionPlayerBuffer = new List<string>(12);
        private readonly Dictionary<string, string> _sessionNameBuffer = new Dictionary<string, string>(12);
        private readonly HashSet<string> _rowCacheSeen = new HashSet<string>();
        private readonly List<string> _rowCachePrune = new List<string>(12);
        private static List<PlayerRow> _previewRows;

        private int _lastStatusPlayers = -1;
        private int _lastStatusRows = -1;
        private bool _statusSongActive;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                LeaderboardLog.Warn("A duplicate leaderboard runtime was created; destroying the duplicate so only one update loop runs.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _uiSettings = UiSettings.Load();
            PerformanceDiagnostics.SetEnabled(_uiSettings.PerformanceLogging);
            LeaderboardLog.Info("UI settings file: " + UiSettings.SettingsPath);
            LeaderboardLog.Info("Visibility settings: leaderboard " +
                                (_uiSettings.Enabled ? "enabled" : "disabled") + ", Test Mode " +
                                (_uiSettings.TestMode ? "on" : "off") + ", performance diagnostics " +
                                (_uiSettings.PerformanceLogging ? "on" : "off") + ", text bloom " +
                                (_uiSettings.Bloom ? "on" : "off") + ".");
            PerformanceDiagnostics.Lifecycle("runtime-created",
                "one update loop; panel is created only for a multiplayer song or Test Mode");
        }

        private void Update()
        {
            // Frame-time sampling is active only while a song is being monitored. It does not add a
            // second update loop; this component already runs every frame and returns early between ticks.
            PerformanceDiagnostics.Frame();

            // TMP text updates are queued by Render and applied a couple of labels at a time by this
            // per-frame pump. A full-state sync can change every row at once; spreading those mesh
            // rebuilds avoids one long frame while keeping the 4 Hz data refresh unchanged.
            if (_panel != null && _panel.IsAlive)
            {
                // One row has six labels. A budget of six lets a row's changes apply in one frame,
                // so its small canvas rebuilds once instead of once per label frame, while still
                // keeping a ten-player refresh from turning into one large burst. Spectator mode
                // reuses this same panel; it never has a second UI instance to flush.
                int applied = _panel.FlushUi(6);
                if (applied > 0)
                {
                    PerformanceDiagnostics.RecordUiApply(applied);
                }
            }

            if (_panel != null && _spectatorAnchor == null && Input.GetKeyDown(KeyCode.F8))
            {
                _panel.ToggleEditMode();
            }

            // Diagnostic only: measures the current scene (for example a solo level or the menu) with
            // no leaderboard panel, so a frame drop can be compared against a run where the panel was
            // never built. It changes nothing about the mod's normal behaviour.
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (PerformanceDiagnostics.IsActive && PerformanceDiagnostics.IsManual)
                {
                    PerformanceDiagnostics.EndManual("local baseline stopped (F9)");
                }
                else if (!PerformanceDiagnostics.IsActive)
                {
                    PerformanceDiagnostics.BeginManual("local baseline (F9, no panel)");
                }
            }

            float now = Time.unscaledTime;
            if (now < _nextTick)
            {
                return;
            }

            _nextTick = now + TickInterval;

            try
            {
                Tick(now);
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("A leaderboard update failed; the mod keeps running and will retry.", ex);
            }

            PerformanceDiagnostics.Tick();
        }

        private void Tick(float now)
        {
            long timing = PerformanceDiagnostics.BeginTiming();
            bool wasActive = PerformanceDiagnostics.IsActive;
            long allocatedBefore = wasActive ? GC.GetTotalMemory(false) : 0L;

            // One status block per tick at most, even when a session event changes several fields.
            StatusBoard.Suspend();
            try
            {
                TickCore(now);
            }
            finally
            {
                StatusBoard.Resume();
                PerformanceDiagnostics.EndUpdate(timing);
                if (wasActive)
                {
                    PerformanceDiagnostics.RecordTickAllocation(GC.GetTotalMemory(false) - allocatedBefore);
                }
            }
        }

        private void TickCore(float now)
        {
            if (_spectatorOffsetDirty && now >= _spectatorOffsetSaveAt)
            {
                _spectatorOffsetDirty = false;
                PersistCurrent();
            }

            TrackScene();
            ResolveSession(now);
            ResolveCamera();

            if (_scoreSync != null && !IsUnityAlive(_scoreSync))
            {
                LeaderboardLog.Debug("The previous multiplayer score source was destroyed with its scene.");
                _scoreSync = null;
                _scoreProvider = null;
                UnsubscribeFromPlayers();
                _players = null;
                _localScore = null;
                if (_mistakes != null)
                {
                    _mistakes.EndSong();
                    _mistakes = null;
                }
            }

            if (_panel != null && !_panel.IsAlive)
            {
                LeaderboardLog.Info("The leaderboard panel was destroyed with the gameplay scene.");
                ResetPanelState();
            }

            if (_scoreSync == null || _scoreProvider == null)
            {
                TryResolveScoreSource();
            }

            // A score sync object can also be created in a solo level, so its presence alone is not proof
            // that this is a multiplayer song. Require a connected multiplayer session as well. The
            // session's connected player list can stay empty for a whole song when the local player is
            // hosting alone, so capacity is used instead of that list.
            bool gameplay = IsGameplayScene() || IsScoreManagerInGameplayScene();
            bool connectedMultiplayer = IsConnectedMultiplayerSession();
            bool inMultiplayerSong = gameplay && _scoreSync != null && _scoreProvider != null && connectedMultiplayer;

            if (gameplay && _scoreSync != null && !connectedMultiplayer && !_loggedNonMultiplayerScoreSource)
            {
                _loggedNonMultiplayerScoreSource = true;
                LeaderboardLog.Info("Leaderboard hidden: a gameplay score source exists, but the game is not in a connected multiplayer session.");
            }

            if (inMultiplayerSong)
            {
                _loggedNonMultiplayerScoreSource = false;
            }

            if (inMultiplayerSong)
            {
                if (!_loggedSongEntry)
                {
                    _loggedSongEntry = true;
                    _songEntryTime = now;
                    _localFailureConfirmed = false;
                    _nextStageWarning = 0f;
                    _nextLocalScoreScan = 0f;
                    _localScoreFallbackScans = 0;
                    _rowCache.Clear();
                    _rowBuffer.Clear();
                    ScoreReader.BeginSong();
                    if (_mistakes != null)
                    {
                        _mistakes.EndSong();
                    }

                    _mistakes = new MistakeTracker();
                    _mistakes.BeginSong(_session);
                    _mistakes.SetLocalScore(_localScore);
                    LogSongEntry();
                    PerformanceDiagnostics.BeginSong(SafeScorePlayerCount(), _uiSettings.Enabled);
                    _perfSongActive = true;
                }

                if (!_uiSettings.Enabled)
                {
                    if (_panel != null)
                    {
                        DisposePanel("the leaderboard is disabled in settings");
                    }
                }
                else
                {
                    if (_panel != null && _panelPreview)
                    {
                        DisposePanel("the real multiplayer score source became available");
                    }

                    if (_panel == null)
                    {
                        CreatePanel(false);
                    }

                    ResolveLocalScore(now);

                    if (_mistakes == null)
                    {
                        _mistakes = new MistakeTracker();
                        _mistakes.BeginSong(_session);
                        _mistakes.SetLocalScore(_localScore);
                    }

                    if (_mistakes != null)
                    {
                        _mistakes.Update();
                    }

                    if (_panel != null)
                    {
                        RefreshRows(now);
                    }
                }

                ReportSongStatus();
            }
            else
            {
                if (_perfSongActive)
                {
                    _perfSongActive = false;
                    PerformanceDiagnostics.EndSong("left the multiplayer song");
                }

                if (_mistakes != null)
                {
                    _mistakes.EndSong();
                    _mistakes = null;
                }

                _loggedSongEntry = false;
                _loggedStageWait = false;
                _statusSongActive = false;
                ReportLobbyStatus();

                if (_uiSettings.Enabled && _uiSettings.TestMode)
                {
                    if (!IsTestSceneReady())
                    {
                        if (_panel != null)
                        {
                            DisposePanel("the game is changing scenes");
                        }
                    }
                    else
                    {
                        if (_panel == null)
                        {
                            CreatePanel(true);
                        }

                        if (_panel != null)
                        {
                            _panel.Render(BuildPreviewRows());
                            LogFirstPanelView();
                        }
                    }
                }
                else if (_panel != null)
                {
                    DisposePanel(_uiSettings.Enabled
                        ? "Test Mode is off and no multiplayer song is running"
                        : "the leaderboard is disabled in settings");
                }
            }

            TrackSessionPlayers(now);
        }

        // ---------------------------------------------------------------- session / scene detection

        private void ResolveSession(float now)
        {
            if (_session != null)
            {
                return;
            }

            if (now < _nextManagerScan)
            {
                return;
            }

            _nextManagerScan = now + _managerScanBackoff;
            _managerScanBackoff = Mathf.Min(_managerScanBackoff * 2f, ManagerScanMaxInterval);

            try
            {
                MultiplayerSessionManager[] managers = Resources.FindObjectsOfTypeAll<MultiplayerSessionManager>();
                if (managers == null || managers.Length == 0)
                {
                    return;
                }

                MultiplayerSessionManager best = null;
                for (int i = 0; i < managers.Length; i++)
                {
                    MultiplayerSessionManager candidate = managers[i];
                    if (candidate == null || candidate.gameObject == null)
                    {
                        continue;
                    }

                    Scene scene = candidate.gameObject.scene;
                    if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                    {
                        continue;
                    }

                    bool live = false;
                    try
                    {
                        live = candidate.isConnected && candidate.connectedPlayerCount > 0;
                    }
                    catch
                    {
                        live = false;
                    }

                    if (live)
                    {
                        best = candidate;
                        break;
                    }

                    if (best == null && candidate.gameObject.activeInHierarchy)
                    {
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    return;
                }

                _session = best;
                LeaderboardLog.Info("Multiplayer system detected: the game's session manager is ready.");
                StatusBoard.MultiplayerSystem("DETECTED");

                try
                {
                    LeaderboardLog.Info("Multiplayer connection: " + (_session.isConnected ? "connected" : "not connected yet") +
                                        ", " + _session.connectedPlayerCount + " player(s), max " + _session.maxPlayerCount +
                                        (_session.isConnectionOwner ? ", local client is the host" : string.Empty) + ".");
                }
                catch (Exception ex)
                {
                    LeaderboardLog.Debug("Reading the initial session state failed: " + ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Multiplayer session lookup failed: " + ex.GetType().Name);
            }
        }

        private void TrackScene()
        {
            string scene = SafeSceneName();
            if (string.Equals(scene, _lastSceneName, StringComparison.Ordinal))
            {
                return;
            }

            _lastSceneName = scene;
            _sceneEnteredAt = Time.realtimeSinceStartup;

            // A new scene is the one moment a session or score source is likely to appear, so scan
            // promptly once after the change and only then let the interval grow again. The schedule
            // itself has to be reset too: after a long menu the next scan could otherwise be up to
            // ManagerScanMaxInterval away, which delays the panel by that much after a level loads.
            _managerScanBackoff = ManagerScanInterval;
            _scoreScanBackoff = ManagerScanInterval;
            _nextManagerScan = 0f;
            _nextScoreScan = 0f;
            LeaderboardLog.Debug("Scene changed: '" + scene + "'.");
        }

        /// <summary>
        /// Time from entering the current scene to now, in milliseconds. This is the number that
        /// separates "the panel takes 2 s to appear" from "the UI takes 5 ms to build".
        /// </summary>
        private float SinceSceneMs()
        {
            return _sceneEnteredAt > 0f ? (Time.realtimeSinceStartup - _sceneEnteredAt) * 1000f : -1f;
        }

        /// <summary>
        /// One-shot timing of the first frame the panel is actually inside the rendering camera's
        /// frustum. The panel is built before the song starts, so this is what the eye can see, not
        /// what the builder did. Called from the 4 Hz render path and logs at most once per panel.
        /// </summary>
        private void LogFirstPanelView()
        {
            if (_panelFirstViewLogged || _panel == null || _camera == null || !_camera.isActiveAndEnabled)
            {
                return;
            }

            Vector3 position = _panel.WorldPosition;
            Vector3 eye = _camera.transform.position;
            Vector3 toPanel = position - eye;
            float distance = toPanel.magnitude;
            if (distance < 0.01f)
            {
                return;
            }

            bool inFrustum;
            float angle;
            try
            {
                GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanes);
                inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, new Bounds(position, Vector3.one * 0.1f));
                angle = Vector3.Angle(_camera.transform.forward, toPanel);
            }
            catch (Exception ex)
            {
                _panelFirstViewLogged = true;
                LeaderboardLog.Debug("Could not test whether the panel is in view: " + ex.GetType().Name);
                return;
            }

            if (!inFrustum)
            {
                return;
            }

            _panelFirstViewLogged = true;
            LeaderboardLog.Info("PERF panel first in view: since_build_ms=" +
                                ((Time.realtimeSinceStartup - _panelBuildRealtime) * 1000f)
                                .ToString("0", CultureInfo.InvariantCulture) +
                                ", since_scene_ms=" + SinceSceneMs().ToString("0", CultureInfo.InvariantCulture) +
                                ", distance=" + distance.ToString("0.00", CultureInfo.InvariantCulture) +
                                " m, angle=" + angle.ToString("0", CultureInfo.InvariantCulture) + " deg.");
        }

        private bool IsScoreManagerInGameplayScene()
        {
            MonoBehaviour behaviour = _scoreSync as MonoBehaviour;
            if (behaviour == null || behaviour.gameObject == null)
            {
                return false;
            }

            Scene scene = behaviour.gameObject.scene;
            if (!scene.IsValid())
            {
                return false;
            }

            return string.Equals(scene.name, "GameCore", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scene.name, "GameplayCore", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGameplayScene()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid())
                {
                    return false;
                }

                return string.Equals(scene.name, "GameCore", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(scene.name, "GameplayCore", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeSceneName()
        {
            try
            {
                return SceneManager.GetActiveScene().name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void LogSongEntry()
        {
            int synced = SafeScorePlayerCount();
            int sessionPlayers = SafeSessionPlayerCount();
            bool connected = false;
            if (_session != null)
            {
                try
                {
                    connected = _session.isConnected;
                }
                catch
                {
                    connected = false;
                }
            }

            bool playersManager = false;
            try
            {
                playersManager = _players != null || Resources.FindObjectsOfTypeAll<MultiplayerPlayersManager>().Length > 0;
            }
            catch
            {
                playersManager = false;
            }

            LeaderboardLog.Info("Multiplayer song detected (scene '" + SafeSceneName() + "'): score sync reports " + synced +
                                " player(s), session reports " + sessionPlayers + " player(s) (" + (connected ? "connected" : "not connected") +
                                "), multiplayer player placement " + (playersManager ? "available" : "not found yet") + ".");
        }

        private int SafeScorePlayerCount()
        {
            if (_scoreSync == null)
            {
                return 0;
            }

            try
            {
                return _scoreSync.connectedPlayerCount;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsConnectedMultiplayerSession()
        {
            if (_session == null)
            {
                return false;
            }

            try
            {
                if (!_session.isConnected)
                {
                    return false;
                }

                // A private lobby can report zero connected players while the local host is alone, so
                // the lobby's capacity is the reliable marker. A solo session has capacity 1 (or 0).
                return _session.maxPlayerCount > 1 || _session.connectedPlayerCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private int SafeSessionPlayerCount()
        {
            try
            {
                return _session != null ? _session.connectedPlayerCount : 0;
            }
            catch
            {
                return 0;
            }
        }
        private void TryResolveScoreSource()
        {
            // The leaderboard is only used in a gameplay scene. Scanning every loaded asset in the
            // menu/lobby produced the recurring 8-9 ms update spikes in the baseline log even when
            // the panel did not exist. Wait for GameCore/GameplayCore instead; TrackScene resets the
            // scan schedule when that scene loads.
            if (!IsGameplayScene() && !IsScoreManagerInGameplayScene())
            {
                return;
            }

            if (Time.unscaledTime < _nextScoreScan)
            {
                return;
            }

            _nextScoreScan = Time.unscaledTime + _scoreScanBackoff;
            _scoreScanBackoff = Mathf.Min(_scoreScanBackoff * 2f, ManagerScanMaxInterval);

            try
            {
                // Prefer the exact score-state manager used by the game's own MultiplayerScoreProvider.
                // On a duel that provider is what feeds the game's opponent score display; reading its
                // manager avoids selecting another score-state instance that never received the remote
                // player's deltas.
                if (!_scoreSyncFieldLookupDone)
                {
                    _scoreSyncFieldLookupDone = true;
                    _scoreSyncField = typeof(MultiplayerScoreProvider).GetField(
                        "_scoreSyncStateManager", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                MultiplayerScoreProvider[] providers = Resources.FindObjectsOfTypeAll<MultiplayerScoreProvider>();
                if (_scoreSyncField != null && providers != null)
                {
                    MultiplayerScoreProvider bestProvider = null;
                    IScoreSyncStateManager bestManager = null;

                    for (int i = 0; i < providers.Length; i++)
                    {
                        MultiplayerScoreProvider provider = providers[i];
                        if (provider == null || provider.gameObject == null)
                        {
                            continue;
                        }

                        Scene scene = provider.gameObject.scene;
                        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                        {
                            continue;
                        }

                        IScoreSyncStateManager manager = _scoreSyncField.GetValue(provider) as IScoreSyncStateManager;
                        if (manager == null)
                        {
                            continue;
                        }

                        if (provider.isActiveAndEnabled)
                        {
                            bestProvider = provider;
                            bestManager = manager;
                            break;
                        }

                        if (bestProvider == null)
                        {
                            bestProvider = provider;
                            bestManager = manager;
                        }
                    }

                    if (bestProvider != null && bestManager != null)
                    {
                        _scoreProvider = bestProvider;
                        _scoreSync = bestManager;
                        _loggedMissingScoreSource = false;
                        if (!_loggedProviderScoreSource)
                        {
                            _loggedProviderScoreSource = true;
                            LeaderboardLog.Info("Multiplayer score system detected: using the game's own MultiplayerScoreProvider and its score sync state.");
                        }

                        return;
                    }
                }

                // If a manager was already found through the fallback scan, keep it while the provider
                // lookup retries on later frames.
                if (_scoreSync != null)
                {
                    return;
                }

                ScoreSyncStateManager[] managers = Resources.FindObjectsOfTypeAll<ScoreSyncStateManager>();
                if (managers != null)
                {
                    ScoreSyncStateManager best = null;
                    for (int i = 0; i < managers.Length; i++)
                    {
                        ScoreSyncStateManager candidate = managers[i];
                        if (candidate == null || candidate.gameObject == null)
                        {
                            continue;
                        }

                        Scene scene = candidate.gameObject.scene;
                        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                        {
                            continue;
                        }

                        if (candidate.isActiveAndEnabled)
                        {
                            best = candidate;
                            break;
                        }

                        if (best == null)
                        {
                            best = candidate;
                        }
                    }

                    if (best != null)
                    {
                        _scoreSync = best;
                        _loggedMissingScoreSource = false;
                        LeaderboardLog.Info("Multiplayer score system detected: reading the game's own ScoreSyncStateManager.");
                        return;
                    }
                }

                if (!_loggedMissingScoreSource)
                {
                    _loggedMissingScoreSource = true;
                    LeaderboardLog.Debug("No multiplayer score source in this scene yet; waiting for the multiplayer song.");
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Multiplayer score system lookup failed: " + ex.GetType().Name);
            }
        }

        private void TrackSessionPlayers(float now)
        {
            if (_session == null || now < _nextSessionPlayerScan)
            {
                return;
            }

            // The session roster changes rarely. Compare a reused buffer twice a second instead of
            // allocating a new list/dictionary and a summary string on every leaderboard tick.
            _nextSessionPlayerScan = now + 0.5f;
            _sessionPlayerBuffer.Clear();
            _sessionNameBuffer.Clear();

            try
            {
                IReadOnlyList<IConnectedPlayer> connected = _session.connectedPlayers;
                if (connected == null)
                {
                    return;
                }

                for (int i = 0; i < connected.Count; i++)
                {
                    IConnectedPlayer player = connected[i];
                    if (player == null)
                    {
                        continue;
                    }

                    string id = SafeId(player);
                    if (string.IsNullOrEmpty(id) || _sessionNameBuffer.ContainsKey(id))
                    {
                        continue;
                    }

                    _sessionNameBuffer[id] = SafeName(player);
                    _sessionPlayerBuffer.Add(id);
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the session player list failed: " + ex.GetType().Name);
                return;
            }

            bool rosterChanged = _sessionNames.Count != _sessionNameBuffer.Count;
            if (!rosterChanged)
            {
                for (int i = 0; i < _sessionPlayerBuffer.Count; i++)
                {
                    string id = _sessionPlayerBuffer[i];
                    string current;
                    if (!_sessionNames.TryGetValue(id, out current) ||
                        !string.Equals(current, _sessionNameBuffer[id], StringComparison.Ordinal))
                    {
                        rosterChanged = true;
                        break;
                    }
                }
            }

            if (!rosterChanged)
            {
                return;
            }

            bool connectedSession = false;
            try
            {
                connectedSession = _session.isConnected;
            }
            catch
            {
                connectedSession = false;
            }

            if (connectedSession)
            {
                for (int i = 0; i < _sessionPlayerBuffer.Count; i++)
                {
                    string id = _sessionPlayerBuffer[i];
                    if (!_sessionNames.ContainsKey(id) && _sessionNames.Count > 0)
                    {
                        LeaderboardLog.Info("Player joined: \"" + _sessionNameBuffer[id] + "\" (id " + ShortId(id) + ").");
                    }
                }

                foreach (KeyValuePair<string, string> pair in _sessionNames)
                {
                    if (!_sessionNameBuffer.ContainsKey(pair.Key))
                    {
                        LeaderboardLog.Info("Player left: \"" + pair.Value + "\" (id " + ShortId(pair.Key) + ").");
                    }
                }
            }

            _sessionNames.Clear();
            foreach (KeyValuePair<string, string> pair in _sessionNameBuffer)
            {
                _sessionNames[pair.Key] = pair.Value;
            }

            string summary = DescribePlayers(_sessionNameBuffer, _sessionPlayerBuffer);
            _lastPlayerSummary = summary;

            if (connectedSession && _sessionPlayerBuffer.Count > 0)
            {
                if (!_loggedSessionDetected)
                {
                    _loggedSessionDetected = true;
                    LeaderboardLog.Info("Multiplayer session detected: " + _sessionPlayerBuffer.Count + " player(s) [" + summary + "].");
                }
                else
                {
                    LeaderboardLog.Info("Session player list changed: " + _sessionPlayerBuffer.Count + " player(s) [" + summary + "].");
                }
            }
            else if (_sessionPlayerBuffer.Count == 0 && _loggedSessionDetected)
            {
                _loggedSessionDetected = false;
                LeaderboardLog.Info("Multiplayer session ended; the leaderboard is waiting for the next session.");
            }
        }

        // ---------------------------------------------------------------- panel

        /// <summary>
        /// Places the panel's bottom-centre just above the game's own combo counter, which is the world-space
        /// HUD element at the bottom centre of the play area. If the combo counter cannot be found, a
        /// centred fallback above the play area is used.
        /// </summary>
        private void ResolvePanelPlacement(Vector3 anchor, Quaternion stage, out Vector3 position,
                                           out Quaternion rotation, out string placement)
        {
            Vector3 eye = new Vector3(anchor.x, anchor.y + LocalEyeHeight, anchor.z);
            Vector3 faceFrom = _camera != null ? _camera.transform.position : eye;

            RectTransform timer = FindTimerAnchor();
            if (timer != null)
            {
                Vector3 timerTop;
                if (TryGetTimerTop(timer, out timerTop))
                {
                    position = new Vector3(timerTop.x, timerTop.y + 0.05f, timerTop.z);
                    placement = "bottom just above the song-progress timer HUD, whose top centre is " + Format(timerTop);
                    rotation = FacePlayer(position, faceFrom, stage);
                    LogTimerAnchor(timer, timerTop);
                    return;
                }
            }

            Transform combo = FindComboAnchor();

            if (combo != null)
            {
                Vector3 comboPosition = combo.position;
                Vector3 toCombo = comboPosition - anchor;
                toCombo.y = 0f;

                bool inFront = toCombo.sqrMagnitude > 0.25f &&
                               Vector3.Dot(toCombo.normalized, stage * Vector3.forward) > 0.2f;

                if (inFront && comboPosition.y > anchor.y + 0.2f && comboPosition.y < anchor.y + 4f)
                {
                    float halfHeight = 0.10f;
                    RectTransform comboRect = combo as RectTransform;
                    if (comboRect != null)
                    {
                        float worldHeight = Mathf.Abs(comboRect.rect.height * comboRect.lossyScale.y);
                        if (worldHeight > 0.01f && worldHeight < 2f)
                        {
                            halfHeight = worldHeight * 0.5f;
                        }
                    }

                    // Clear the combo text, but never drop the panel so low that it sits in the note lane.
                    float bottom = Mathf.Max(comboPosition.y + halfHeight + 0.06f, anchor.y + 1.25f);
                    position = new Vector3(comboPosition.x, bottom, comboPosition.z);
                    placement = "bottom just above the combo HUD at " + Format(comboPosition) +
                                " (combo height " + (halfHeight * 2f).ToString("0.00", CultureInfo.InvariantCulture) + " m)";
                    rotation = FacePlayer(position, faceFrom, stage);
                    return;
                }
            }

            position = anchor + stage * new Vector3(0f, PanelUpOffset, PanelForwardOffset);
            placement = "centered above the play area (timer and combo HUDs not found)";
            rotation = FacePlayer(position, faceFrom, stage);
        }

        private static Quaternion FacePlayer(Vector3 position, Vector3 eye, Quaternion stage)
        {
            Vector3 toPlayer = eye - position;
            toPlayer.y = 0f;

            // Unity UI fronts face local -Z, so the panel's +Z must point away from the player for the
            // readable face to look at the player's spawn.
            return toPlayer.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(-toPlayer.normalized, Vector3.up)
                : stage * Quaternion.Euler(0f, 180f, 0f);
        }

        private static Transform FindComboAnchor()
        {
            try
            {
                ComboUIController[] combos = Resources.FindObjectsOfTypeAll<ComboUIController>();
                if (combos == null)
                {
                    return null;
                }

                for (int i = 0; i < combos.Length; i++)
                {
                    ComboUIController combo = combos[i];
                    if (combo == null || combo.gameObject == null || !combo.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Scene scene = combo.gameObject.scene;
                    if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                    {
                        continue;
                    }

                    if (!_comboTextFieldLookupDone)
                    {
                        _comboTextFieldLookupDone = true;
                        _comboTextField = typeof(ComboUIController).GetField(
                            "_comboText", BindingFlags.Instance | BindingFlags.NonPublic);
                    }

                    Component text = null;
                    if (_comboTextField != null)
                    {
                        try
                        {
                            text = _comboTextField.GetValue(combo) as Component;
                        }
                        catch
                        {
                            text = null;
                        }
                    }

                    Transform anchor = text != null ? text.transform : combo.transform;
                    if (anchor == null)
                    {
                        continue;
                    }

                    if (string.Equals(scene.name, "GameCore", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(scene.name, "GameplayCore", StringComparison.OrdinalIgnoreCase))
                    {
                        return anchor;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Combo HUD lookup failed: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Finds the game's song-progress HUD: the panel containing the progress bar, elapsed/total timer and
        /// notes-remaining label shown at the top centre of the play view. This is the anchor the user
        /// specified, and it is a real game object rather than a guessed screen coordinate.
        /// </summary>
        private static RectTransform FindTimerAnchor()
        {
            try
            {
                CoreGameHUDController[] huds = Resources.FindObjectsOfTypeAll<CoreGameHUDController>();
                if (huds == null)
                {
                    return null;
                }

                for (int i = 0; i < huds.Length; i++)
                {
                    CoreGameHUDController hud = huds[i];
                    if (hud == null || hud.gameObject == null || !hud.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Scene scene = hud.gameObject.scene;
                    if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                    {
                        continue;
                    }

                    if (!string.Equals(scene.name, "GameCore", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(scene.name, "GameplayCore", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    GameObject progress = hud.songProgressPanelGO;
                    if (progress == null || !progress.activeInHierarchy)
                    {
                        continue;
                    }

                    RectTransform rect = progress.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        return rect;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Song-progress timer lookup failed: " + ex.GetType().Name);
            }

            return null;
        }

        private bool TryGetTimerTop(RectTransform timer, out Vector3 topCentre)
        {
            topCentre = Vector3.zero;

            try
            {
                Vector3[] corners = new Vector3[4];
                timer.GetWorldCorners(corners);
                Vector3 top = (corners[1] + corners[2]) * 0.5f;

                Canvas canvas = timer.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    if (_camera == null)
                    {
                        return false;
                    }

                    topCentre = _camera.ScreenToWorldPoint(new Vector3(top.x, top.y, 2.0f));
                    return true;
                }

                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    Camera camera = canvas.worldCamera != null ? canvas.worldCamera : _camera;
                    if (camera == null)
                    {
                        return false;
                    }

                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, top);
                    topCentre = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 2.0f));
                    return true;
                }

                topCentre = top;
                return true;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the song-progress timer position failed: " + ex.GetType().Name);
                return false;
            }
        }

        private static void LogTimerAnchor(RectTransform timer, Vector3 topCentre)
        {
            try
            {
                Canvas canvas = timer.GetComponentInParent<Canvas>();
                Rect rect = timer.rect;
                LeaderboardLog.Info("Song-progress timer HUD found: '" + timer.name + "', canvas render mode " +
                                    (canvas != null ? canvas.renderMode.ToString() : "none") + ", top centre " +
                                    Format(topCentre) + ", rect " + rect.width.ToString("0", CultureInfo.InvariantCulture) +
                                    " x " + rect.height.ToString("0", CultureInfo.InvariantCulture) + " px, world scale " +
                                    timer.lossyScale.x.ToString("0.####", CultureInfo.InvariantCulture) + ".");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Timer HUD diagnostics failed: " + ex.GetType().Name);
            }
        }

        private void ResolveLocalScore(float now)
        {
            if (_localScore != null && IsUnityAlive(_localScore))
            {
                if (_mistakes != null)
                {
                    _mistakes.SetLocalScore(_localScore);
                }

                return;
            }

            _localScore = null;
            if (_mistakes != null)
            {
                _mistakes.SetLocalScore(null);
            }

            // Never perform a global Resources scan four times a second. That scan is expensive
            // (several milliseconds when the scene has many objects) and, while a player is
            // inactive or spectating, there is no local ScoreController to find. Look directly at
            // the local player first; only a small, bounded fallback uses the global scan.
            if (now < _nextLocalScoreScan)
            {
                return;
            }

            _nextLocalScoreScan = now + LocalScoreRetryInterval;

            // The inactive/spectating facade has no live ScoreController. Do not walk its hierarchy
            // every two seconds; the row correctly uses the synced multiplayer state in that state.
            // The game keeps the old active facade reference after DisablePlayer(), so checking only
            // activeLocalPlayerFacade would mistake a spectator for an active player.
            if (!IsLocalPlayerActive())
            {
                return;
            }

            ScoreController direct = FindLocalScoreOnPlayer();
            if (direct != null)
            {
                _localScore = direct;
                if (_mistakes != null)
                {
                    _mistakes.SetLocalScore(direct);
                }

                LeaderboardLog.Debug("Local score source detected on the local multiplayer player: reading the live ScoreController for your own row.");
                return;
            }

            // A global scan cannot produce an active score controller while the local player is in
            // the inactive/spectating facade. Skip it entirely in that state; the row correctly uses
            // the synced state instead, and this was the 8 ms per-tick cost seen in the 1.2.3 log.
            if (_localScoreFallbackScans >= LocalScoreFallbackScanLimit)
            {
                return;
            }

            _localScoreFallbackScans++;

            try
            {
                ScoreController[] scores = Resources.FindObjectsOfTypeAll<ScoreController>();
                if (scores == null)
                {
                    return;
                }

                for (int i = 0; i < scores.Length; i++)
                {
                    ScoreController candidate = scores[i];
                    if (candidate == null || candidate.gameObject == null || !candidate.isActiveAndEnabled)
                    {
                        continue;
                    }

                    Scene scene = candidate.gameObject.scene;
                    if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                    {
                        continue;
                    }

                    if (!string.Equals(scene.name, "GameCore", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(scene.name, "GameplayCore", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _localScore = candidate;
                    if (_mistakes != null)
                    {
                        _mistakes.SetLocalScore(candidate);
                    }

                    LeaderboardLog.Debug("Local score source detected by the fallback scan: reading the live ScoreController for your own row.");
                    return;
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Local score source lookup failed: " + ex.GetType().Name);
            }
        }

        private ScoreController FindLocalScoreOnPlayer()
        {
            if (_players == null)
            {
                return null;
            }

            try
            {
                Transform local = _players.localPlayerTransform;
                if (local != null)
                {
                    ScoreController candidate = local.GetComponentInChildren<ScoreController>(true);
                    if (candidate != null && candidate.isActiveAndEnabled)
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the local player's ScoreController failed: " + ex.GetType().Name);
            }

            try
            {
                MonoBehaviour facade = _players.activeLocalPlayerFacade as MonoBehaviour;
                if (facade != null)
                {
                    ScoreController candidate = facade.GetComponentInChildren<ScoreController>(true);
                    if (candidate != null && candidate.isActiveAndEnabled)
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the active local player facade failed: " + ex.GetType().Name);
            }

            return null;
        }

        private void CreatePanel(bool preview)
        {
            Vector3 anchor;
            Quaternion stage;
            string source;
            Vector3 position;
            Quaternion rotation;
            string placement;

            if (preview)
            {
                if (!TryResolveTestPlacement(out anchor, out stage, out position, out rotation, out placement))
                {
                    return;
                }

                source = "Test Mode camera placement (no multiplayer play space)";
            }
            else
            {
                if (!TryResolveStage(Time.unscaledTime, out anchor, out stage, out source))
                {
                    if (!_loggedStageWait)
                    {
                        _loggedStageWait = true;
                        LeaderboardLog.Info("Waiting for the multiplayer level to spawn the local player before placing the leaderboard.");
                    }

                    return;
                }

                _loggedStageWait = false;
                ResolvePanelPlacement(anchor, stage, out position, out rotation, out placement);
            }

            bool restoredSaved = false;
            if (_uiSettings.HasPosition)
            {
                // A saved gameplay position is meaningful in the play space. In a menu, use it only
                // when it is still near the camera; otherwise the Test Mode panel would open in the
                // previous arena's coordinates and be invisible.
                bool nearCamera = _camera == null ||
                                  Vector3.Distance(_uiSettings.Position, _camera.transform.position) <= 8f;
                if (!preview || IsGameplayScene() || nearCamera)
                {
                    position = _uiSettings.Position;
                    rotation = _uiSettings.Rotation;
                    placement = "saved UI settings from " + UiSettings.SettingsPath;
                    restoredSaved = true;
                }
            }

            try
            {
                long buildStarted = PerformanceDiagnostics.Timestamp();
                _panel = WorldLeaderboardPanel.Create(position, rotation, _uiSettings, preview);
                double buildMs = PerformanceDiagnostics.ElapsedMs(buildStarted);
                _panelPreview = preview;
                _panelBuildRealtime = Time.realtimeSinceStartup;
                _panelFirstViewLogged = false;
                if (_camera != null)
                {
                    _panel.SetCamera(_camera);
                }

                if (preview)
                {
                    _panel.EnterTestEditing();
                    PerformanceDiagnostics.BeginManual("Test Mode panel in the main menu");

                    // A restored user position is left exactly where it was saved.
                    if (!restoredSaved)
                    {
                        _panel.Render(BuildPreviewRows());
                    }
                }

                Vector3 eye = new Vector3(anchor.x, anchor.y + LocalEyeHeight, anchor.z);
                Vector3 offset = position - eye;
                Vector3 localOffset = Quaternion.Inverse(stage) * offset;
                float distance = offset.magnitude;
                float leftAngle = Mathf.Atan2(-localOffset.x, localOffset.z) * Mathf.Rad2Deg;
                float upAngle = Mathf.Atan2(localOffset.y, Mathf.Sqrt(
                    localOffset.x * localOffset.x + localOffset.z * localOffset.z)) * Mathf.Rad2Deg;

                LeaderboardLog.Info("Leaderboard created at a fixed world-space position " + Format(position) +
                                    " (" + placement + "; stage source: " + source + "), " +
                                    distance.ToString("0.00", CultureInfo.InvariantCulture) +
                                    " m from the player's eye line, " + leftAngle.ToString("0", CultureInfo.InvariantCulture) +
                                    " deg left and " + upAngle.ToString("0", CultureInfo.InvariantCulture) + " deg up; " +
                                    (preview ? "PREVIEW DATA" : SafeScorePlayerCount() + " player(s) in the score sync table") +
                                    ".");
                LeaderboardLog.Info("World-space anchoring: the panel transform is set once here and is never updated, " +
                                    "so it cannot follow or rotate with the headset.");
                PerformanceDiagnostics.Lifecycle("panel-created",
                    (preview ? "Test Mode preview" : "multiplayer song") + ", " +
                    (preview ? "preview rows" : SafeScorePlayerCount() + " score-sync player(s)") +
                    ", build_ms=" + buildMs.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", phases={" + _panel.CreationPhases + "}" +
                    ", canvases=" + _panel.CanvasCount + ", labels=" + _panel.LabelCount +
                    ", scene_to_panel_ms=" + SinceSceneMs().ToString("0", CultureInfo.InvariantCulture) + ".");

                _lastRowProblem = null;
                _lastRowCount = 0;
                _lastOrder = null;
                _lastRenderSignature = 0L;
                _forceRender = true;
                _rankFlashWasActive = false;
                _spectatorFooterName = null;
                _spectatorFooter = null;
                _spectatorReferenceScans = 0;
                _nextSpectatorScan = 0f;
                _spectatorBarChecks = 0;
                _nativeSpectatorInfo = null;
                _nativeSpectatorInfoScanned = false;
                ResetNativeSpectatorInfoScan();
                _loggedPlayerData.Clear();
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("Could not create the leaderboard panel; retrying.", ex);
            }
        }

        private static bool IsTestSceneReady()
        {
            string scene = SafeSceneName();
            return IsGameplayScene() ||
                   string.Equals(scene, "MainMenu", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveTestPlacement(out Vector3 anchor, out Quaternion stage, out Vector3 position,
                                             out Quaternion rotation, out string placement)
        {
            anchor = Vector3.zero;
            stage = Quaternion.identity;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            placement = string.Empty;

            ResolveCamera();
            ResolveTestCamera();
            if (_camera == null || _camera.transform.position.y < 0.5f)
            {
                // The loading/menu camera starts at the origin before it is placed. Waiting avoids
                // creating the preview at floor level and leaving it there.
                return false;
            }

            Vector3 viewForward = _camera.transform.forward;
            if (viewForward.sqrMagnitude < 0.001f)
            {
                viewForward = Vector3.forward;
            }

            viewForward.Normalize();
            Vector3 forward = viewForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            anchor = Flatten(_camera.transform.position);
            stage = Quaternion.LookRotation(forward, Vector3.up);

            // Anchor the bottom edge to a point slightly below the centre of the viewport. This uses
            // the camera's real projection, so the preview is fully visible even when the menu camera
            // is pitched up or down. The panel itself stays world-anchored after this single placement.
            // Keep the default low enough to be fully visible on both the main-menu camera and the
            // in-game viewpoint; the user's first drag/save replaces this default for good.
            position = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.12f, 2.0f));
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            placement = "2 m in front of the current camera, lower-middle of the view (Test Mode)";
            return true;
        }

        private void ResolveTestCamera()
        {
            try
            {
                Camera best = _camera;
                Camera[] cameras = Camera.allCameras;
                if (cameras == null)
                {
                    return;
                }

                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera candidate = cameras[i];
                    if (candidate == null || !candidate.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.name, "AppInitCamera", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(candidate.name, "MenuMainCamera", StringComparison.OrdinalIgnoreCase))
                    {
                        best = candidate;
                        break;
                    }

                    if (best == null || best.transform.position.y < 1f)
                    {
                        best = candidate;
                    }
                }

                if (best != null)
                {
                    _camera = best;
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Test Mode camera selection failed: " + ex.GetType().Name);
            }
        }

        private static List<PlayerRow> BuildPreviewRows()
        {
            if (_previewRows != null)
            {
                return _previewRows;
            }

            _previewRows = new List<PlayerRow>(6);
            for (int i = 0; i < 6; i++)
            {
                _previewRows.Add(new PlayerRow
                {
                    UserId = "preview-" + (i + 1),
                    UserName = "PREVIEW PLAYER " + (i + 1),
                    SortIndex = i,
                    IsMe = i == 0,
                    IsParticipant = true,
                    HasScoreData = true,
                    Score = 1250000 - i * 143750,
                    Accuracy = 98.42f - i * 1.37f,
                    HasAccuracy = true,
                    Rank = i + 1
                });
            }

            return _previewRows;
        }

        /// <summary>
        /// Resolves the stage the panel is placed against. The local multiplayer facade is created when the
        /// level spawns players and is left at the arena origin facing the track, which is exactly the frame
        /// the panel offsets are defined in. Until the game reports playerSpawningFinished, the camera is
        /// still at the loading/menu pose, so the panel must not be placed against it.
        /// </summary>
        private bool TryResolveStage(float now, out Vector3 anchor, out Quaternion stage, out string source)
        {
            anchor = Vector3.zero;
            stage = Quaternion.identity;
            source = "multiplayer arena origin";

            ResolvePlayersManager();

            if (_players != null)
            {
                bool finished;
                try
                {
                    finished = _players.playerSpawningFinished;
                }
                catch (Exception ex)
                {
                    LeaderboardLog.Debug("Reading the player spawn state failed: " + ex.GetType().Name);
                    finished = false;
                }

                if (finished)
                {
                    try
                    {
                        Transform local = _players.localPlayerTransform;
                        if (local != null)
                        {
                            Vector3 flat = Flatten(local.position);
                            if (flat.magnitude <= 12f)
                            {
                                anchor = flat;
                                stage = FlattenRotation(local.rotation);
                                source = "local player's multiplayer spawn ('" + local.name + "')";
                                return true;
                            }

                            if (now >= _nextStageWarning)
                            {
                                _nextStageWarning = now + 2f;
                                LeaderboardLog.Warn("The local player's spawn is " + flat.magnitude.ToString("0.0", CultureInfo.InvariantCulture) +
                                                    " m from the arena origin; using the camera position instead.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LeaderboardLog.Debug("Local player placement could not be read: " + ex.GetType().Name);
                    }
                }
                else if (now - _songEntryTime < 12f)
                {
                    // The local facade does not exist yet; the camera is still somewhere in the transition.
                    return false;
                }
            }

            // Only use the camera after the level has had time to settle, so a transition pose cannot anchor
            // the panel behind the play area.
            if (_camera != null && now - _songEntryTime >= 8f)
            {
                anchor = Flatten(_camera.transform.position);
                stage = FlattenRotation(_camera.transform.rotation);
                source = "camera position and facing after the level settled";
                return true;
            }

            return false;
        }

        private void ResolvePlayersManager()
        {
            if (_players != null)
            {
                return;
            }

            try
            {
                MultiplayerPlayersManager[] managers = Resources.FindObjectsOfTypeAll<MultiplayerPlayersManager>();
                if (managers == null)
                {
                    return;
                }

                for (int i = 0; i < managers.Length; i++)
                {
                    MultiplayerPlayersManager candidate = managers[i];
                    if (candidate == null || candidate.gameObject == null)
                    {
                        continue;
                    }

                    Scene scene = candidate.gameObject.scene;
                    if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
                    {
                        continue;
                    }

                    _players = candidate;
                    SubscribeToPlayers(candidate);
                    return;
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Multiplayer player placement lookup failed: " + ex.GetType().Name);
            }
        }

        private void SubscribeToPlayers(MultiplayerPlayersManager players)
        {
            if (players == null || ReferenceEquals(players, _playersSubscribed))
            {
                return;
            }

            UnsubscribeFromPlayers();

            try
            {
                players.playerDidFinishEvent += HandleLocalPlayerFinished;
                players.playerNetworkDidFailedEvent += HandleLocalPlayerFinished;
                _playersSubscribed = players;
                PerformanceDiagnostics.Lifecycle("events-subscribed", "local level-end events (one subscription)");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Local player result subscription failed: " + ex.GetType().Name);
            }
        }

        private void UnsubscribeFromPlayers()
        {
            if (_playersSubscribed == null)
            {
                return;
            }

            try
            {
                _playersSubscribed.playerDidFinishEvent -= HandleLocalPlayerFinished;
                _playersSubscribed.playerNetworkDidFailedEvent -= HandleLocalPlayerFinished;
            }
            catch
            {
                // The gameplay scene is already being torn down.
            }

            _playersSubscribed = null;
            PerformanceDiagnostics.Lifecycle("events-unsubscribed", "local level-end events");
        }

        private void HandleLocalPlayerFinished(MultiplayerLevelCompletionResults results)
        {
            if (results == null)
            {
                return;
            }

            bool failed = results.playerLevelEndReason ==
                          MultiplayerLevelCompletionResults.MultiplayerPlayerLevelEndReason.Failed;
            if (failed)
            {
                _localFailureConfirmed = true;
                LeaderboardLog.Info("Local player failure confirmed by the game's level-end event; the row will show FAILED.");
            }
        }

        private void RefreshRows(float now)
        {
            string problem;

            // A remote player's IsFailed() is derived from the same state that is briefly absent while
            // the level spawns. Give the first state packet time to arrive before calling an empty row a
            // failure; the local player uses the level-end event instead and is never gated here.
            bool allowRemoteFailure = now - _songEntryTime >= 2f;

            long readStarted = PerformanceDiagnostics.BeginTiming();
            bool read = ScoreReader.TryRead(_scoreSync, _scoreProvider, _localScore, _localFailureConfirmed,
                                            allowRemoteFailure, _mistakes, _rowBuffer, _rowCache, out problem);
            PerformanceDiagnostics.EndRead(readStarted, _rowBuffer.Count);

            if (read && _mistakes != null)
            {
                _mistakes.Prune(_rowBuffer);
            }

            if (read)
            {
                PruneRowCache(_rowBuffer);
            }

            if (!read)
            {
                if (!string.Equals(problem, _lastRowProblem, StringComparison.Ordinal))
                {
                    _lastRowProblem = problem;
                    LeaderboardLog.Warn("Player data unavailable: " + problem);
                }

                _rowBuffer.Clear();
            }
            else if (_lastRowProblem != null)
            {
                _lastRowProblem = null;
            }

            if (_panel != null)
            {
                string spectatorFooter = UpdateSpectatorView(now);
                long signature = ComputeRenderSignature(_rowBuffer, spectatorFooter);
                bool rankFlashActive = _panel.HasActiveRankFlash(now);
                bool rowsReady = _panel.HasAllRows(_rowBuffer.Count);
                bool changed = _forceRender || signature != _lastRenderSignature || rankFlashActive ||
                               _rankFlashWasActive || !rowsReady;

                // Only the frames that actually apply new text cost a TMP/canvas rebuild. Reading the
                // scores every 250 ms is cheap; making the panel redraw them is not, so the visible
                // refresh is throttled independently. Forced updates (panel creation, spectator
                // re-attach) and rows that still need to be created bypass the gate.
                bool refreshDue = now >= _nextUiRefresh || _forceRender || !rowsReady;
                if (changed && refreshDue)
                {
                    long renderStarted = PerformanceDiagnostics.BeginTiming();
                    _panel.Render(_rowBuffer, spectatorFooter);
                    PerformanceDiagnostics.EndRender(renderStarted);
                    _lastRenderSignature = signature;
                    _forceRender = false;
                    _rankFlashWasActive = rankFlashActive;
                    _nextUiRefresh = now + (_spectatorAnchor != null
                        ? SpectatorUiRefreshInterval
                        : UiRefreshInterval);
                }

                LogFirstPanelView();
            }

            _lastRowCount = _rowBuffer.Count;
            LogFirstPlayerValues(_rowBuffer);
            LogOrder(_rowBuffer, now);
        }

        /// <summary>
        /// Cheap change signature for the visible row data and footer. Render still queues only the
        /// labels that actually differ; this stops the whole panel being walked four times a second
        /// when the score sync table has not changed. Rank flashes force a render until they expire.
        /// The hash is not persisted and collisions only delay one visual update until the next tick.
        /// </summary>
        private static long ComputeRenderSignature(List<PlayerRow> rows, string footer)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)rows.Count) * 1099511628211UL;
                hash = (hash ^ (footer != null ? (uint)footer.GetHashCode() : 0u)) * 1099511628211UL;
                for (int i = 0; i < rows.Count; i++)
                {
                    PlayerRow row = rows[i];
                    hash = (hash ^ (uint)row.Rank) * 1099511628211UL;
                    hash = (hash ^ (uint)row.Score) * 1099511628211UL;
                    hash = (hash ^ (uint)row.Combo) * 1099511628211UL;
                    hash = (hash ^ (uint)row.Mistakes) * 1099511628211UL;
                    hash = (hash ^ (uint)row.Accuracy.GetHashCode()) * 1099511628211UL;
                    hash = (hash ^ (row.IsMe ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.IsParticipant ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.IsFailed ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.HasScoreData ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.HasAccuracy ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.HasCombo ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.HasMistakes ? 1u : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.DisplayName != null ? (uint)row.DisplayName.GetHashCode() : 0u)) * 1099511628211UL;
                    hash = (hash ^ (row.UserId != null ? (uint)row.UserId.GetHashCode() : 0u)) * 1099511628211UL;
                }

                return (long)hash;
            }
        }

        private void EnsureSpectatorReferences()
        {
            if (_spectatorPicker != null && !IsUnityAlive(_spectatorPicker))
            {
                _spectatorPicker = null;
                _spectatorController = null;
                _spectatorMenu = null;
                _spectatorBar = null;
                _spectatorSpot = null;
            }

            if (_spectatorController != null && !IsUnityAlive(_spectatorController))
            {
                _spectatorController = null;
                _spectatorSpot = null;
            }

            if (_spectatorMenu != null && !IsUnityAlive(_spectatorMenu))
            {
                _spectatorMenu = null;
                _spectatorBar = null;
            }

            if (_spectatorBar != null && !IsUnityAlive(_spectatorBar))
            {
                _spectatorBar = null;
            }

            if (_spectatorPicker != null && _spectatorController != null && _spectatorMenu != null)
            {
                return;
            }

            // The inactive-player facade owns all three controllers. Once it exists, a global
            // FindObjectOfType fallback cannot find a better object and is pure recurring cost in a
            // large gameplay scene. Bound retries as well: if this game version doesn't provide one
            // of the prefabs, the leaderboard must stop searching instead of scanning forever.
            if (_spectatorReferenceScans >= SpectatorReferenceScanLimit)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextSpectatorScan)
            {
                return;
            }

            _nextSpectatorScan = now + 1f;
            _spectatorReferenceScans++;
            try
            {
                // When the player is spectating, the three controllers live under the game's
                // inactive-player facade. Search that hierarchy first instead of three scene-wide
                // FindObjectOfType scans.
                MultiplayerLocalInactivePlayerFacade facade =
                    _players != null ? _players.inactivePlayerFacade : null;
                if (facade != null)
                {
                    if (_spectatorPicker == null)
                    {
                        _spectatorPicker = facade.GetComponentInChildren<MultiplayerSpectatingSpotPickerViewController>(true);
                    }

                    if (_spectatorController == null)
                    {
                        _spectatorController = facade.GetComponentInChildren<MultiplayerSpectatorController>(true);
                    }

                    if (_spectatorMenu == null)
                    {
                        _spectatorMenu = facade.GetComponentInChildren<MultiplayerLocalInactivePlayerInGameMenuViewController>(true);
                    }

                    return;
                }

                if (_spectatorPicker == null)
                {
                    _spectatorPicker = FindObjectOfType<MultiplayerSpectatingSpotPickerViewController>();
                }

                if (_spectatorController == null)
                {
                    _spectatorController = FindObjectOfType<MultiplayerSpectatorController>();
                }

                // The inactive-player menu owns the native spectator bar. It is the placement
                // reference for our panel, so resolve it together with the picker.
                if (_spectatorMenu == null)
                {
                    _spectatorMenu = FindObjectOfType<MultiplayerLocalInactivePlayerInGameMenuViewController>();
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Spectator UI lookup failed: " + ex.GetType().Name);
            }
        }

        private RectTransform ResolveSpectatorBar()
        {
            if (_spectatorBar != null && IsUnityAlive(_spectatorBar))
            {
                return _spectatorBar;
            }

            _spectatorBar = null;

            // The native menu's _mainBar is the complete bottom bar (disconnect, spot picker and the
            // two buttons). It has no public accessor, so read the serialized reference once.
            if (_spectatorMenu != null)
            {
                if (_spectatorMainBarField == null)
                {
                    _spectatorMainBarField = typeof(MultiplayerLocalInactivePlayerInGameMenuViewController)
                        .GetField("_mainBar", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (_spectatorMainBarField != null)
                {
                    object value = _spectatorMainBarField.GetValue(_spectatorMenu);
                    GameObject mainBar = value as GameObject;
                    if (mainBar != null)
                    {
                        _spectatorBar = mainBar.transform as RectTransform;
                    }

                    Component component = value as Component;
                    if (_spectatorBar == null && component != null)
                    {
                        _spectatorBar = component.transform as RectTransform;
                    }
                }

            }

            if (_spectatorBar == null && _spectatorPicker != null)
            {
                // The picker's parent is the spot-picker row, which is in turn inside the native bar.
                Transform parent = _spectatorPicker.transform.parent;
                _spectatorBar = parent as RectTransform;
                if (_spectatorBar == null)
                {
                    _spectatorBar = _spectatorPicker.transform as RectTransform;
                }
            }

            if (_spectatorBar == null && _spectatorMenu != null)
            {
                _spectatorBar = _spectatorMenu.transform as RectTransform;
            }

            return _spectatorBar;
        }

        private bool HasWorldSpaceSpectatorBar()
        {
            RectTransform bar = ResolveSpectatorBar();
            if (bar == null)
            {
                return false;
            }

            Canvas canvas = bar.GetComponentInParent<Canvas>();
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            return rootCanvas != null && rootCanvas.renderMode == RenderMode.WorldSpace;
        }

        private bool IsLocalPlayerActive()
        {
            if (_players == null)
            {
                return false;
            }

            try
            {
                // SwitchLocalPlayerToInactiveCoroutine disables the active facade but deliberately
                // leaves the manager's activeLocalPlayerFacade reference in place. The inactive
                // facade is therefore the authoritative sign that this player is now spectating.
                return _players.activeLocalPlayerFacade != null &&
                       _players.inactivePlayerFacade == null;
            }
            catch
            {
                return false;
            }
        }

        private void ResetNativeSpectatorInfoScan()
        {
            _nativeSpectatorInfoScans = 0;
            _nextNativeSpectatorInfoScan = 0f;
        }

        private string UpdateSpectatorView(float now)
        {
            // In normal multiplayer play the local player is active and there can be no spectator
            // menu. Do not run three scene-wide lookups every second for a UI that cannot exist.
            if (IsLocalPlayerActive())
            {
                StopSpectating("the local player is active");
                return null;
            }

            EnsureSpectatorReferences();
            if (_spectatorPicker == null || _spectatorController == null)
            {
                StopSpectating("the spectator UI is not present");
                return null;
            }

            IMultiplayerSpectatingSpot spot = null;
            try
            {
                spot = _spectatorController.currentSpot;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the selected spectating spot failed: " + ex.GetType().Name);
            }

            if (!(spot is MultiplayerConnectedPlayerSpectatingSpot) ||
                (spot as UnityEngine.Object) == null)
            {
                StopSpectating("the player is not spectating another player");
                return null;
            }

            string spotName = string.Empty;
            try
            {
                spotName = spot.spotName;
            }
            catch
            {
                // The name is useful only for diagnostics and the footer; keep the view working without it.
            }

            Transform anchor = null;
            try
            {
                anchor = spot.transform;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Reading the spectator platform transform failed: " + ex.GetType().Name);
            }

            if (anchor == null)
            {
                StopSpectating("the selected player has no platform transform");
                return null;
            }

            bool anchorChanged = !ReferenceEquals(anchor, _spectatorAnchor);
            if (anchorChanged)
            {
                if (_spectatorAnchor == null)
                {
                    // Capture the normal panel pose so spectator mode can hand the same panel back
                    // if the local player ever becomes active again in this scene.
                    _spectatorHomePosition = _panel.WorldPosition;
                    _spectatorHomeRotation = _panel.WorldRotation;
                }

                ResetNativeSpectatorInfoScan();
                _panel.AttachTo(anchor);
                _spectatorAnchor = anchor;
                _spectatorSpot = spot;
                _spectatorPositionedFromNativeBar = false;
                _nextSpectatorBarCheck = 0f;
                _spectatorBarChecks = 0;
                PositionSpectatorPanel(anchor, true);

                LeaderboardLog.Info("Spectator leaderboard attached to the selected player's platform" +
                                    (string.IsNullOrEmpty(spotName) ? "" : " ('" + spotName + "')") +
                                    " with " + _rowBuffer.Count.ToString(CultureInfo.InvariantCulture) +
                                    " player rows; the lobby panel is reused, not duplicated.");
            }
            else if (!_spectatorPositionedFromNativeBar && _spectatorBarChecks < SpectatorBarCheckLimit &&
                     now >= _nextSpectatorBarCheck)
            {
                // The inactive-player bar can be built a frame after the spectator controller. Retry
                // once per second, not four times, until the real bar exists instead of leaving the
                // temporary camera fallback in place.
                _spectatorBarChecks++;
                _nextSpectatorBarCheck = now + 1f;
                if (HasWorldSpaceSpectatorBar())
                {
                    PositionSpectatorPanel(anchor, true);
                }
            }

            _panel.SetVisible(true);
            HideNativeSpectatorInfo(anchor);

            if (string.IsNullOrEmpty(spotName))
            {
                _spectatorFooterName = null;
                _spectatorFooter = null;
                return null;
            }

            if (!string.Equals(spotName, _spectatorFooterName, StringComparison.Ordinal))
            {
                _spectatorFooterName = spotName;
                _spectatorFooter = "SPECTATING: " + spotName;
            }

            return _spectatorFooter;
        }

        private void PositionSpectatorPanel(Transform anchor, bool logPlacement)
        {
            if (_panel == null || anchor == null)
            {
                return;
            }

            RectTransform nativeBar = ResolveSpectatorBar();
            Canvas nativeCanvas = nativeBar != null ? nativeBar.GetComponentInParent<Canvas>() : null;
            Canvas nativeRootCanvas = nativeCanvas != null ? nativeCanvas.rootCanvas : null;
            bool usableNativeBar = nativeBar != null && nativeRootCanvas != null &&
                                   nativeRootCanvas.renderMode == RenderMode.WorldSpace;

            if (usableNativeBar)
            {
                Vector3[] corners = new Vector3[4];
                nativeBar.GetWorldCorners(corners);

                Vector3 nativeBottom = (corners[0] + corners[3]) * 0.5f;
                Vector3 nativeTop = (corners[1] + corners[2]) * 0.5f;
                Vector3 nativeUp = nativeTop - nativeBottom;
                if (nativeUp.sqrMagnitude < 0.0001f)
                {
                    nativeUp = nativeBar.up;
                }

                nativeUp.Normalize();

                // UI front faces the spectator, so its negative forward axis is the direction
                // straight back from the players. Using the bar's plane avoids shifting the panel
                // sideways when the spectator camera is not exactly centred on the bar.
                Vector3 awayFromPlayers = -nativeBar.forward;
                awayFromPlayers.y = 0f;
                if (awayFromPlayers.sqrMagnitude < 0.0001f &&
                    _camera != null && _camera.isActiveAndEnabled)
                {
                    awayFromPlayers = _camera.transform.position - nativeTop;
                    awayFromPlayers.y = 0f;
                }

                if (awayFromPlayers.sqrMagnitude < 0.0001f)
                {
                    awayFromPlayers = Vector3.back;
                }

                awayFromPlayers.Normalize();

                // The shared panel's pivot is bottom-centre. Put that bottom edge just above the
                // native bar, then step slightly back towards the spectator and away from the players.
                Vector3 nativeBottomCenter = nativeTop +
                                             nativeUp * SpectatorPanelGapAboveNativeBar +
                                             awayFromPlayers * SpectatorPanelBackOffset;
                Quaternion nativeRotation = nativeBar.rotation;

                _spectatorBasePosition = nativeBottomCenter;
                _spectatorBaseRotation = nativeRotation;
                ApplySpectatorOffset();
                _panel.SetCamera(_camera);
                _spectatorPositionedFromNativeBar = true;

                if (logPlacement)
                {
                    LeaderboardLog.Info("Spectator leaderboard positioned from the native bar '" +
                                        HierarchyPath(nativeBar) + "' (canvas " + nativeRootCanvas.renderMode +
                                        "): bar_world=" + Format(nativeBar.position) +
                                        ", bar_euler=" + Format(nativeBar.rotation.eulerAngles) +
                                        ", bar_scale=" + Format(nativeBar.lossyScale) +
                                        ", bar_top=" + Format(nativeTop) +
                                        ", panel_bottom=" + Format(nativeBottomCenter) +
                                        ", gap=" + SpectatorPanelGapAboveNativeBar.ToString("0.00", CultureInfo.InvariantCulture) +
                                        "m, back=" + SpectatorPanelBackOffset.ToString("0.00", CultureInfo.InvariantCulture) + "m.");
                }
                return;
            }

            Vector3 eye;
            Vector3 forward;
            if (_camera != null && _camera.isActiveAndEnabled)
            {
                eye = _camera.transform.position;
                forward = _camera.transform.forward;
            }
            else
            {
                eye = anchor.position;
                forward = anchor.forward;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = anchor.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            // Fallback only: keep the panel low and closer than the old 2.10 m placement when the
            // native bar cannot be read (for example, before the inactive-player menu exists).
            Vector3 center = eye + forward * SpectatorPanelFallbackDistance +
                             Vector3.up * SpectatorPanelFallbackCenterHeight;
            float height = Mathf.Max(0.1f, _panel.WorldHeight);
            Vector3 bottomCenter = center - Vector3.up * (height * 0.5f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            _spectatorBasePosition = bottomCenter;
            _spectatorBaseRotation = rotation;
            ApplySpectatorOffset();
            _panel.SetCamera(_camera);
            _spectatorPositionedFromNativeBar = false;

            if (logPlacement)
            {
                LeaderboardLog.Info("Spectator leaderboard used the camera fallback placement; the native " +
                                    "spectator bar was not available. panel_bottom=" + Format(bottomCenter) + ".");
            }
        }

        private void ApplySpectatorOffset()
        {
            if (_panel == null || _spectatorAnchor == null)
            {
                return;
            }

            Vector3 offset = _uiSettings != null && _uiSettings.HasSpectatorOffset
                ? _uiSettings.SpectatorOffset
                : Vector3.zero;
            _panel.SetWorldPose(_spectatorBasePosition + _spectatorBaseRotation * offset,
                                _spectatorBaseRotation);
        }

        private void HideNativeSpectatorInfo(Transform anchor)
        {
            // One bounded search per runtime. GetComponentsInChildren<Graphic>(true) walks the whole
            // inactive-player rig (avatar, camera, menu) and allocated on every spectate switch, even
            // though this game version has no native white information panel to hide.
            if (_nativeSpectatorInfo != null || _nativeSpectatorInfoScanned ||
                _nativeSpectatorInfoScans >= NativeSpectatorInfoScanLimit)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextNativeSpectatorInfoScan)
            {
                return;
            }

            // This used to scan the complete inactive-player hierarchy and write a debug line on
            // every 4 Hz refresh because the native panel does not exist in this game version. Two
            // attempts, one second apart, are enough for the prefab to finish spawning without
            // turning the lookup into permanent per-tick allocation and file I/O.
            _nativeSpectatorInfoScans++;
            _nextNativeSpectatorInfoScan = now + NativeSpectatorInfoRetryInterval;

            Transform searchRoot = null;
            try
            {
                MultiplayerLocalInactivePlayerFacade facade =
                    _players != null ? _players.inactivePlayerFacade : null;
                if (facade == null && _spectatorController != null)
                {
                    facade = _spectatorController.GetComponentInParent<MultiplayerLocalInactivePlayerFacade>();
                }

                if (facade == null)
                {
                    facade = FindObjectOfType<MultiplayerLocalInactivePlayerFacade>();
                }

                if (facade != null)
                {
                    searchRoot = facade.transform;
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Finding the inactive spectating platform failed: " + ex.GetType().Name);
            }

            if (searchRoot == null)
            {
                searchRoot = anchor != null ? anchor.parent : null;
            }

            if (searchRoot == null)
            {
                LogMissingNativeSpectatorInfo();
                return;
            }

            Graphic best = null;
            Graphic namedFrame = null;
            float bestArea = 0f;
            try
            {
                // Collect the white information panel and the prefab's named "Frame" in the same
                // hierarchy walk. The previous code walked it twice when the white-panel test failed.
                Graphic[] graphics = searchRoot.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null || (!(graphic is Image) && !(graphic is RawImage)))
                    {
                        continue;
                    }

                    if (namedFrame == null &&
                        string.Equals(graphic.gameObject.name, "Frame", StringComparison.Ordinal))
                    {
                        namedFrame = graphic;
                    }

                    Color color = graphic.color;
                    if (color.a < 0.25f || color.r < 0.65f || color.g < 0.65f || color.b < 0.65f)
                    {
                        continue;
                    }

                    RectTransform rect = graphic.rectTransform;
                    if (rect == null)
                    {
                        continue;
                    }

                    Vector3 scale = rect.lossyScale;
                    float area = Mathf.Abs(rect.rect.width * scale.x * rect.rect.height * scale.y);
                    if (area >= 0.20f && area > bestArea)
                    {
                        best = graphic;
                        bestArea = area;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Scanning the native spectator information UI failed: " + ex.GetType().Name);
            }

            if (best == null && namedFrame != null)
            {
                // The inactive-player prefab names its large spectator frame "Frame" even when the
                // image's baked color is not bright enough for the general white-panel scan.
                best = namedFrame;
                RectTransform frameRect = namedFrame.rectTransform;
                if (frameRect != null)
                {
                    bestArea = Mathf.Abs(frameRect.rect.width * frameRect.lossyScale.x *
                                         frameRect.rect.height * frameRect.lossyScale.y);
                }
            }

            if (best == null)
            {
                LogMissingNativeSpectatorInfo();
                return;
            }

            _nativeSpectatorInfo = best.gameObject;
            _nativeSpectatorInfoScanned = true;
            _nativeSpectatorInfo.SetActive(false);
            LeaderboardLog.Info("Disabled the native white spectator information panel '" +
                                HierarchyPath(_nativeSpectatorInfo.transform) + "' with area " +
                                bestArea.ToString("0.00", CultureInfo.InvariantCulture) + " m^2.");
        }

        private void LogMissingNativeSpectatorInfo()
        {
            // Once per runtime, after the bounded scan has given up. The lookup repeats for every
            // spectated player, and the same missing-panel line had been written hundreds of times.
            if (_loggedMissingNativeSpectatorInfo || _nativeSpectatorInfoScans < NativeSpectatorInfoScanLimit)
            {
                return;
            }

            _loggedMissingNativeSpectatorInfo = true;
            _nativeSpectatorInfoScanned = true;
            LeaderboardLog.Debug("No native white spectator information panel was found after " +
                                 NativeSpectatorInfoScanLimit + " scans; leaving the game's spectator UI untouched.");
        }

        private static string HierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static string SelectedSpectatingUserId(IMultiplayerSpectatingSpot spot)
        {
            if (spot == null)
            {
                return string.Empty;
            }

            try
            {
                if (_spectatingSpotPlayerField == null)
                {
                    _spectatingSpotPlayerField = typeof(MultiplayerConnectedPlayerSpectatingSpot).GetField(
                        "_connectedPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                IConnectedPlayer player = _spectatingSpotPlayerField != null
                    ? _spectatingSpotPlayerField.GetValue(spot) as IConnectedPlayer
                    : null;
                return player != null ? SafeId(player) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private PlayerRow FindPlayerRow(string userId, string displayName)
        {
            PlayerRow byName = null;
            for (int i = 0; i < _rowBuffer.Count; i++)
            {
                PlayerRow row = _rowBuffer[i];
                if (row == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(userId) &&
                    string.Equals(row.UserId, userId, StringComparison.Ordinal))
                {
                    return row;
                }

                if (byName == null && !string.IsNullOrEmpty(displayName) &&
                    string.Equals(row.DisplayName, displayName, StringComparison.Ordinal))
                {
                    byName = row;
                }
            }

            return byName;
        }

        private void StopSpectating(string reason)
        {
            if (_spectatorAnchor == null)
            {
                ResetSpectatorState();
                return;
            }

            if (_panel != null && _panel.IsAlive)
            {
                _panel.DetachFromAnchor();
                _panel.SetWorldPose(_spectatorHomePosition, _spectatorHomeRotation);
            }

            ResetSpectatorState();
            _forceRender = true;
            LeaderboardLog.Info("Spectator leaderboard hidden: " + reason + ".");
        }

        private void ResetSpectatorState()
        {
            _spectatorAnchor = null;
            _spectatorSpot = null;
            _spectatorFooterName = null;
            _spectatorFooter = null;
            _spectatorPositionedFromNativeBar = false;
            _spectatorBarChecks = 0;
            _nextSpectatorBarCheck = 0f;
            ResetNativeSpectatorInfoScan();
        }

        private void PruneRowCache(List<PlayerRow> rows)
        {
            if (_rowCache.Count <= rows.Count)
            {
                return;
            }

            _rowCacheSeen.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].CacheKey))
                {
                    _rowCacheSeen.Add(rows[i].CacheKey);
                }
            }

            if (_rowCacheSeen.Count == 0)
            {
                return;
            }

            _rowCachePrune.Clear();
            foreach (KeyValuePair<string, PlayerRow> pair in _rowCache)
            {
                if (!_rowCacheSeen.Contains(pair.Key))
                {
                    _rowCachePrune.Add(pair.Key);
                }
            }

            for (int i = 0; i < _rowCachePrune.Count; i++)
            {
                _rowCache.Remove(_rowCachePrune[i]);
            }
        }

        private void LogFirstPlayerValues(List<PlayerRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                PlayerRow row = rows[i];

                // Do not mark a player as logged while the row is still the initial 0 / 0.00%
                // packet. The first useful line should be a real score or a confirmed failure,
                // otherwise a remote row that starts at zero would never log its first update.
                bool hasRealValue = row.HasScoreData &&
                                    (row.Score > 0 || (row.HasAccuracy && row.Accuracy > 0f) || row.IsFailed);
                if (!hasRealValue)
                {
                    continue;
                }

                string key = string.IsNullOrEmpty(row.UserId) ? row.DisplayName : row.UserId;
                if (!_loggedPlayerData.Add(key))
                {
                    continue;
                }

                LeaderboardLog.Info("Player data retrieved for \"" + row.DisplayName + "\": score " + row.ScoreText +
                                    ", accuracy " + row.AccuracyText +
                                    (row.Rank > 0 ? ", position #" + row.Rank : ", spectating") + ".");
            }
        }

        private void LogOrder(List<PlayerRow> rows, float now)
        {
            if (rows.Count == 0)
            {
                return;
            }

            if (now < _nextRankingLog)
            {
                return;
            }

            // Schedule the window before building the summary string. The previous order left the
            // timer unadvanced while the order was unchanged, so Describe() allocated a StringBuilder
            // and a string four times a second for the rest of the song.
            _nextRankingLog = now + RankingLogInterval;
            string order = ScoreReader.Describe(rows);
            if (string.Equals(order, _lastOrder, StringComparison.Ordinal))
            {
                return;
            }

            _lastOrder = order;

            // Player names can contain line breaks, which used to split one ranking line into many
            // file lines. Keep it a single bounded line.
            LeaderboardLog.Debug("Leaderboard order: " + SingleLine(order, 240));
        }

        private void DisposePanel(string reason)
        {
            if (_panel == null)
            {
                return;
            }

            // Detach before destroying the panel so a temporary spectator parent cannot keep a
            // destroyed object alive in the hierarchy for a frame.
            StopSpectating("the main leaderboard panel is being disposed");

            if (_panelPreview)
            {
                PerformanceDiagnostics.EndManual("Test Mode panel disposed");
            }

            try
            {
                _panel.Dispose();
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Disposing the leaderboard panel failed: " + ex.GetType().Name);
            }

            if (!_panelFirstViewLogged)
            {
                LeaderboardLog.Info("PERF panel view: never entered the camera frustum before disposal (scene_to_panel_ms=" +
                                    SinceSceneMs().ToString("0", CultureInfo.InvariantCulture) + ").");
            }

            ResetPanelState();
            LeaderboardLog.Info("Leaderboard removed: " + reason + ".");
            PerformanceDiagnostics.Lifecycle("panel-disposed", reason);
        }

        private void ResetPanelState()
        {
            StopSpectating("the main leaderboard panel was removed");
            _panel = null;
            _panelPreview = false;
            _lastRowCount = 0;
            _lastRowProblem = null;
            _lastOrder = null;
            _loggedPlayerData.Clear();
            _rowBuffer.Clear();
            _rowCache.Clear();
            _lastStatusRows = -1;
            PerformanceDiagnostics.PanelDisposed();
        }

        // ---------------------------------------------------------------- status

        private void ReportSongStatus()
        {
            int players = SafeScorePlayerCount();
            if (_statusSongActive && players == _lastStatusPlayers && _lastRowCount == _lastStatusRows)
            {
                _loggedWaiting = false;
                return;
            }

            _statusSongActive = true;
            _lastStatusPlayers = players;
            _lastStatusRows = _lastRowCount;

            StatusBoard.Suspend();
            StatusBoard.MultiplayerSystem("DETECTED");
            StatusBoard.Session(players > 0
                ? "IN SONG (" + players + " player(s))"
                : "IN SONG (waiting for the score sync table)");
            StatusBoard.Leaderboard("ACTIVE (fixed world-space panel in the multiplayer play space)");
            StatusBoard.PlayerData(_lastRowCount > 0
                ? "AVAILABLE (" + _lastRowCount + " player row(s))"
                : "WAITING (players have not reported scores yet)");
            StatusBoard.Resume();

            _loggedWaiting = false;
        }

        private void ReportLobbyStatus()
        {
            bool connected = false;
            if (_session != null)
            {
                try
                {
                    connected = _session.isConnected;
                }
                catch
                {
                    connected = false;
                }
            }

            bool waiting = _session == null || !connected;

            StatusBoard.Suspend();
            if (waiting)
            {
                StatusBoard.Session(_session == null
                    ? "WAITING (not in a multiplayer lobby)"
                    : "WAITING (not connected to a multiplayer session)");
                StatusBoard.Leaderboard("READY (waiting for a multiplayer session)");
                StatusBoard.PlayerData("WAITING");
            }
            else
            {
                StatusBoard.MultiplayerSystem("DETECTED");
                StatusBoard.Session("CONNECTED (" + SafePlayerCount() + " player(s), no song running)");
                StatusBoard.Leaderboard("READY (waiting for the multiplayer song to start)");
                StatusBoard.PlayerData("WAITING (score data is only available during a song)");
            }
            StatusBoard.Resume();

            if (waiting)
            {
                if (!_loggedWaiting)
                {
                    _loggedWaiting = true;
                    LeaderboardLog.Info("Waiting for a multiplayer session. This is not an error; the mod stays loaded and idle.");
                }
            }
            else
            {
                _loggedWaiting = false;
            }
        }

        // ---------------------------------------------------------------- settings menu API

        internal bool IsMultiplayerSongNow()
        {
            return _scoreSync != null && _scoreProvider != null && IsConnectedMultiplayerSession() &&
                   (IsGameplayScene() || IsScoreManagerInGameplayScene());
        }

        internal void SetMasterEnabled(bool enabled)
        {
            if (_uiSettings.Enabled == enabled)
            {
                return;
            }

            _uiSettings.Enabled = enabled;
            PersistCurrent();

            if (!enabled)
            {
                DisposePanel("the leaderboard was disabled in the settings menu");
            }

            LeaderboardLog.Info("Leaderboard master switch: " + (enabled ? "enabled" : "disabled") + ".");
        }

        internal void SetPerformanceLogging(bool enabled)
        {
            if (_uiSettings.PerformanceLogging == enabled)
            {
                return;
            }

            _uiSettings.PerformanceLogging = enabled;
            if (_panel != null)
            {
                _panel.SetPerformanceLogging(enabled);
            }

            PersistCurrent();
            PerformanceDiagnostics.SetEnabled(enabled);
            LeaderboardLog.Info("Performance diagnostics setting: " + (enabled ? "on" : "off") +
                                ". The setting is saved with the other leaderboard options.");
        }

        internal void SetTestMode(bool enabled)
        {
            if (_uiSettings.TestMode == enabled)
            {
                if (enabled && _panel == null && !IsMultiplayerSongNow())
                {
                    CreatePanel(true);
                }

                return;
            }

            _uiSettings.TestMode = enabled;
            PersistCurrent();

            if (!enabled)
            {
                if (_panel != null && _panelPreview)
                {
                    DisposePanel("Test Mode was turned off");
                }
                else if (_panel != null)
                {
                    _panel.ExitEditMode();
                }
            }
            else
            {
                if (_panel == null && !IsMultiplayerSongNow())
                {
                    CreatePanel(true);
                }

                if (_panel != null && _panelPreview)
                {
                    _panel.EnterTestEditing();
                }
            }

            LeaderboardLog.Info("Test Mode: " + (enabled ? "on (preview data outside multiplayer)" : "off") +
                                "; saved position and appearance were kept.");
        }

        internal void ApplySettings(UiSettings settings, bool restorePosition)
        {
            if (settings == null)
            {
                return;
            }

            _uiSettings = settings;
            PerformanceDiagnostics.SetEnabled(_uiSettings.PerformanceLogging);
            if (_panel != null)
            {
                _panel.SetPerformanceLogging(_uiSettings.PerformanceLogging);
                _panel.ApplyAppearance(_uiSettings);
                if (restorePosition && _spectatorAnchor == null)
                {
                    _panel.ApplyPosition(_uiSettings);
                }

                if (!_uiSettings.Enabled)
                {
                    DisposePanel("the leaderboard is disabled in settings");
                }
                else if (_spectatorAnchor != null)
                {
                    PositionSpectatorPanel(_spectatorAnchor, false);
                }

                _forceRender = true;
            }
        }

        internal void SetOverallScale(float value)
        {
            _uiSettings.OverallScale = Mathf.Clamp(value, UiSettings.MinOverallScale, UiSettings.MaxOverallScale);
            if (_panel != null)
            {
                _panel.SetOverallScale(_uiSettings.OverallScale);
            }
        }

        internal void SetTextScale(float value)
        {
            _uiSettings.TextScale = Mathf.Clamp(value, UiSettings.MinTextScale, UiSettings.MaxTextScale);
            if (_panel != null)
            {
                _panel.SetTextScale(_uiSettings.TextScale);
            }
        }

        internal void SetNameScale(float value)
        {
            _uiSettings.NameScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            if (_panel != null)
            {
                _panel.SetNameScale(_uiSettings.NameScale);
            }
        }

        internal void SetAccuracyScale(float value)
        {
            _uiSettings.AccuracyScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            if (_panel != null)
            {
                _panel.SetAccuracyScale(_uiSettings.AccuracyScale);
            }
        }

        internal void SetScoreScale(float value)
        {
            _uiSettings.ScoreScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            if (_panel != null)
            {
                _panel.SetScoreScale(_uiSettings.ScoreScale);
            }
        }

        internal void SetRowSpacing(float value)
        {
            _uiSettings.RowSpacing = Mathf.Clamp(value, UiSettings.MinRowSpacing, UiSettings.MaxRowSpacing);
            if (_panel != null)
            {
                _panel.SetRowSpacing(_uiSettings.RowSpacing);
            }
        }

        internal void SetBackgroundWidthScale(float value)
        {
            _uiSettings.BackgroundWidthScale = Mathf.Clamp(value, UiSettings.MinBackgroundScale,
                                                            UiSettings.MaxBackgroundScale);
            if (_panel != null)
            {
                _panel.SetBackgroundWidthScale(_uiSettings.BackgroundWidthScale);
            }
        }

        internal void SetBackgroundHeightScale(float value)
        {
            _uiSettings.BackgroundHeightScale = Mathf.Clamp(value, UiSettings.MinBackgroundScale,
                                                             UiSettings.MaxBackgroundScale);
            if (_panel != null)
            {
                _panel.SetBackgroundHeightScale(_uiSettings.BackgroundHeightScale);
            }
        }

        internal void SetBackgroundOpacity(float value)
        {
            _uiSettings.BackgroundOpacity = Mathf.Clamp(value, UiSettings.MinBackgroundOpacity,
                                                         UiSettings.MaxBackgroundOpacity);
            if (_panel != null)
            {
                _panel.SetBackgroundOpacity(_uiSettings.BackgroundOpacity);
            }
        }

        internal void SetTextBrightness(float value)
        {
            _uiSettings.TextBrightness = Mathf.Clamp(value, UiSettings.MinTextBrightness,
                                                      UiSettings.MaxTextBrightness);
            if (_panel != null)
            {
                _panel.SetTextBrightness(_uiSettings.TextBrightness);
                _forceRender = true;
            }
        }

        internal void SetBloom(bool value)
        {
            if (_uiSettings.Bloom == value)
            {
                return;
            }

            _uiSettings.Bloom = value;
            if (_panel != null)
            {
                _panel.SetBloom(value);
            }

            // This is a simple appearance toggle, so persist it immediately rather than making the
            // player remember to open the separate Save page. The rest of the current settings are
            // written with it, matching the master enable/performance toggles.
            PersistCurrent();
            LeaderboardLog.Info("Text bloom setting: " + (value ? "enabled" : "disabled") +
                                " at intensity " + _uiSettings.BloomIntensity.ToString("0.##") +
                                ". Saved with the other leaderboard options.");
        }

        internal void SetBloomIntensity(float value)
        {
            value = Mathf.Clamp(value, UiSettings.MinBloomIntensity, UiSettings.MaxBloomIntensity);
            if (Mathf.Approximately(_uiSettings.BloomIntensity, value))
            {
                return;
            }

            _uiSettings.BloomIntensity = value;
            if (_panel != null)
            {
                _panel.SetBloomIntensity(value);
            }

            PersistCurrent();
            LeaderboardLog.Info("Text bloom intensity setting: " + value.ToString("0.##") +
                                ". Saved with the other leaderboard options.");
        }

        internal void SetShowScore(bool value)
        {
            _uiSettings.ShowScore = value;
            if (_panel != null)
            {
                _panel.SetShowScore(value);
            }
        }

        internal void SetShowAccuracy(bool value)
        {
            _uiSettings.ShowAccuracy = value;
            if (_panel != null)
            {
                _panel.SetShowAccuracy(value);
            }
        }

        internal void SetShowCombo(bool value)
        {
            _uiSettings.ShowCombo = value;
            if (_panel != null)
            {
                _panel.SetShowCombo(value);
            }
        }

        internal void SetShowMistakes(bool value)
        {
            _uiSettings.ShowMistakes = value;
            if (_panel != null)
            {
                _panel.SetShowMistakes(value);
            }
        }

        internal void SetRotationEuler(Vector3 euler)
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.SetRotationEuler(euler);
            }
        }

        internal void Rotate(float x, float y, float z)
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.RotateLocal(x, y, z);
            }
        }

        internal void SetRotationEditing(bool enabled)
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.SetRotationEditing(enabled);
            }
        }

        internal void ToggleRotationEditing()
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.ToggleRotationEditing();
            }
        }

        internal void ResetRotation()
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            if (_panel != null)
            {
                _panel.ResetRotation();
            }
        }

        internal void Nudge(float right, float up, float forward)
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.Nudge(new Vector3(right, up, forward), 0.05f);
            }
        }

        internal void NudgeSpectator(float right, float up, float forward)
        {
            if (_spectatorAnchor == null || _uiSettings == null)
            {
                return;
            }

            SetSpectatorOffset(_uiSettings.SpectatorOffset +
                               new Vector3(right, up, forward) * SpectatorNudgeStep);
        }

        internal void SetSpectatorOffset(Vector3 value)
        {
            if (_uiSettings == null)
            {
                return;
            }

            value.x = Mathf.Clamp(value.x, UiSettings.MinSpectatorOffset, UiSettings.MaxSpectatorOffset);
            value.y = Mathf.Clamp(value.y, UiSettings.MinSpectatorOffset, UiSettings.MaxSpectatorOffset);
            value.z = Mathf.Clamp(value.z, UiSettings.MinSpectatorOffset, UiSettings.MaxSpectatorOffset);
            if (_uiSettings.HasSpectatorOffset && _uiSettings.SpectatorOffset == value)
            {
                return;
            }

            _uiSettings.HasSpectatorOffset = true;
            _uiSettings.SpectatorOffset = value;
            if (_spectatorAnchor != null)
            {
                // Recompute the native-bar base before applying the offset. The selected player's
                // platform is the parent, so a stale cached base would snap the panel back if the
                // platform has moved since it was attached.
                PositionSpectatorPanel(_spectatorAnchor, false);
            }

            _forceRender = true;
            _spectatorOffsetDirty = true;
            _spectatorOffsetSaveAt = Time.unscaledTime + SpectatorOffsetSaveDelay;
        }

        internal void ResetSpectatorPosition()
        {
            SetSpectatorOffset(Vector3.zero);
            PersistCurrent();
            _spectatorOffsetDirty = false;
        }

        internal void BeginEditing()
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.EnterEditMode();
            }
        }

        internal void ExitEditing()
        {
            if (_panel != null)
            {
                _panel.ExitEditMode();
            }
        }

        internal void ToggleDrag()
        {
            if (_spectatorAnchor != null)
            {
                return;
            }

            EnsurePanelForEditing();
            if (_panel != null)
            {
                _panel.ToggleDrag();
            }
        }

        internal void SaveSettings()
        {
            CaptureIntoUiSettings();
            PersistCurrent();
            LeaderboardLog.Info("Settings menu saved the leaderboard configuration. " + _uiSettings.Describe() + ".");
        }

        internal void SavePosition()
        {
            SaveSettings();
            LeaderboardLog.Info(_spectatorAnchor != null
                ? "Settings menu saved the spectator panel offset; the normal panel position and all rotations were left unchanged."
                : "Settings menu saved the current world-space position without changing rotation or scale.");
        }

        internal void RestoreSavedSettings()
        {
            _uiSettings = UiSettings.Load();
            PerformanceDiagnostics.SetEnabled(_uiSettings.PerformanceLogging);
            if (_panel != null)
            {
                _panel.ApplyAppearance(_uiSettings);
                if (_spectatorAnchor == null)
                {
                    _panel.ApplyPosition(_uiSettings);
                }
                else
                {
                    PositionSpectatorPanel(_spectatorAnchor, false);
                }

                if (!_uiSettings.Enabled)
                {
                    DisposePanel("the restored settings disable the leaderboard");
                }
            }

            LeaderboardLog.Info("Settings menu restored the values saved in " + UiSettings.SettingsPath + ".");
        }

        internal void ResetPosition()
        {
            if (_spectatorAnchor != null)
            {
                ResetSpectatorPosition();
                LeaderboardLog.Info("Settings menu reset the spectator panel offset; rotation stayed locked.");
                return;
            }

            if (_panel != null)
            {
                _panel.ResetPosition();
                CaptureIntoUiSettings();
            }

            _uiSettings.HasPosition = true;
            LeaderboardLog.Info("Settings menu reset the leaderboard position only; rotation was left unchanged.");
        }

        internal void ResetScale()
        {
            _uiSettings.OverallScale = 1f;
            _uiSettings.TextScale = 1f;
            _uiSettings.NameScale = 1f;
            _uiSettings.AccuracyScale = 1f;
            _uiSettings.ScoreScale = 1f;
            _uiSettings.RowSpacing = 1f;
            _uiSettings.BackgroundWidthScale = 1f;
            _uiSettings.BackgroundHeightScale = 1f;
            if (_panel != null)
            {
                _panel.ResetScale();
            }

            LeaderboardLog.Info("Settings menu reset all scale and background sliders to their defaults.");
        }

        internal void ResetSettings()
        {
            string path = UiSettings.SettingsPath;
            _uiSettings = new UiSettings();
            PerformanceDiagnostics.SetEnabled(_uiSettings.PerformanceLogging);

            if (_panel != null)
            {
                _panel.ExitEditMode();
                _panel.ApplyAppearance(_uiSettings);
                if (_spectatorAnchor == null)
                {
                    _panel.ResetRotation();
                    _panel.ResetPosition();
                }
                else
                {
                    PositionSpectatorPanel(_spectatorAnchor, false);
                }
            }

            if (!IsMultiplayerSongNow())
            {
                DisposePanel("settings were reset to defaults");
            }

            _uiSettings.Save(0f, 0f);
            LeaderboardLog.Info("Settings menu reset every leaderboard setting to its default and wrote " + path + ".");
        }

        internal string StatusText()
        {
            if (!_uiSettings.Enabled)
            {
                return "Leaderboard disabled. Enable it above to show the panel.";
            }

            if (IsMultiplayerSongNow())
            {
                return "Live multiplayer song: real player data is being shown.";
            }

            if (_uiSettings.TestMode)
            {
                return _panel != null
                    ? "Test Mode: PREVIEW DATA is visible (not real multiplayer scores). Drag with the laser pointer."
                    : "Test Mode is on; the preview panel is being created.";
            }

            return "Hidden outside multiplayer songs. Enable Test Mode to preview and position it now.";
        }

        private void EnsurePanelForEditing()
        {
            if (!_uiSettings.Enabled)
            {
                SetMasterEnabled(true);
            }

            if (_panel == null && !IsMultiplayerSongNow())
            {
                if (!_uiSettings.TestMode)
                {
                    SetTestMode(true);
                }
                else
                {
                    CreatePanel(true);
                }
            }
        }

        private void CaptureIntoUiSettings()
        {
            if (_panel == null)
            {
                return;
            }

            UiSettings captured = _panel.CaptureSettings();
            captured.Enabled = _uiSettings.Enabled;
            captured.TestMode = _uiSettings.TestMode;
            captured.PerformanceLogging = _uiSettings.PerformanceLogging;
            // While spectating the panel has been deliberately attached to another player's
            // platform. Never turn that temporary pose into the normal lobby position.
            if (_spectatorAnchor != null)
            {
                captured.HasPosition = _uiSettings.HasPosition;
                captured.Position = _uiSettings.Position;
                captured.Rotation = _uiSettings.Rotation;
            }

            captured.HasSpectatorOffset = _uiSettings.HasSpectatorOffset;
            captured.SpectatorOffset = _uiSettings.SpectatorOffset;
            _uiSettings = captured;
        }

        private void PersistCurrent()
        {
            float width = _uiSettings.BackgroundWidth;
            float height = _uiSettings.BackgroundHeight;
            if (_panel != null)
            {
                width = _panel.WorldWidth;
                height = _panel.WorldHeight;
            }

            _uiSettings.Save(width, height);
        }

        // ---------------------------------------------------------------- helpers

        private void ResolveCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled)
            {
                return;
            }

            // Switching into the inactive/spectating facade disables the gameplay camera for a
            // moment. Camera.main, Camera.allCameras and the Resources fallback allocate and walk the
            // scene, so cap the retry rate instead of running them on every 250 ms tick.
            float now = Time.unscaledTime;
            if (now < _nextCameraResolve)
            {
                return;
            }

            _nextCameraResolve = now + 2f;

            Camera resolved = null;
            try
            {
                resolved = Camera.main;
            }
            catch
            {
                resolved = null;
            }

            if (resolved == null)
            {
                try
                {
                    Camera[] activeCameras = Camera.allCameras;
                    if (activeCameras != null)
                    {
                        for (int i = 0; i < activeCameras.Length; i++)
                        {
                            Camera candidate = activeCameras[i];
                            if (candidate == null || !candidate.isActiveAndEnabled)
                            {
                                continue;
                            }

                            if (resolved == null || candidate.depth > resolved.depth)
                            {
                                resolved = candidate;
                            }
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (resolved == null)
            {
                try
                {
                    Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
                    if (cameras != null)
                    {
                        for (int i = 0; i < cameras.Length; i++)
                        {
                            Camera candidate = cameras[i];
                            if (candidate == null || !candidate.isActiveAndEnabled)
                            {
                                continue;
                            }

                            if (resolved == null || candidate.depth > resolved.depth)
                            {
                                resolved = candidate;
                            }
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (resolved == null)
            {
                return;
            }

            _camera = resolved;
            if (_panel != null)
            {
                _panel.SetCamera(_camera);
            }

            LogCameraState("resolved the rendering camera");
        }

        private void LogCameraState(string reason)
        {
            try
            {
                if (_camera == null)
                {
                    if (Time.unscaledTime < _nextCameraWarning)
                    {
                        return;
                    }

                    _nextCameraWarning = Time.unscaledTime + 10f;
                    int count = 0;
                    try
                    {
                        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
                        count = cameras == null ? 0 : cameras.Length;
                    }
                    catch
                    {
                        // ignored
                    }

                    LeaderboardLog.Warn("No active rendering camera found while the " + reason + " (" + count +
                                        " camera object(s) in memory).");
                    return;
                }

                // The camera is re-resolved repeatedly during a song but normally stays the same
                // object, so only the first resolution (Info) and an actual camera change (Debug)
                // are logged. Repeating the same pose on every refresh was a large share of the log.
                bool changed = !ReferenceEquals(_lastLoggedCamera, _camera);
                if (!changed)
                {
                    return;
                }

                _lastLoggedCamera = _camera;
                string line = "Rendering camera " + reason + ": '" + _camera.name + "' at " +
                              Format(_camera.transform.position) + ", forward " +
                              Format(_camera.transform.forward) + ", depth " +
                              _camera.depth.ToString("0.0", CultureInfo.InvariantCulture) + ".";
                if (_cameraLogCount == 0)
                {
                    _cameraLogCount = 1;
                    LeaderboardLog.Info(line);
                }
                else
                {
                    LeaderboardLog.Debug(line);
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Camera diagnostics failed: " + ex.GetType().Name);
            }
        }

        private static string SingleLine(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flattened = text.Replace('\r', ' ').Replace('\n', ' ');
            if (flattened.Length <= maxLength)
            {
                return flattened;
            }

            return flattened.Substring(0, maxLength) + "...";
        }

        private int SafePlayerCount()
        {
            return SafeSessionPlayerCount();
        }

        private static bool IsUnityAlive(object value)
        {
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return unityObject == null ? value != null : unityObject != null;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private static Quaternion FlattenRotation(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
        }

        private static string DescribePlayers(Dictionary<string, string> names, List<string> ids)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('"').Append(names[ids[i]]).Append('"');
            }

            return builder.ToString();
        }

        private static string SafeName(IConnectedPlayer player)
        {
            try
            {
                string name = player.userName;
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }

        private static string SafeId(IConnectedPlayer player)
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

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "?";
            }

            return id.Length <= 6 ? id : id.Substring(0, 6) + "...";
        }

        private static string Format(Vector3 position)
        {
            return "(" + position.x.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                   position.y.ToString("F2", CultureInfo.InvariantCulture) + ", " +
                   position.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        private void OnDestroy()
        {
            if (_perfSongActive)
            {
                _perfSongActive = false;
                PerformanceDiagnostics.EndSong("runtime destroyed");
            }

            if (_spectatorOffsetDirty)
            {
                _spectatorOffsetDirty = false;
                PersistCurrent();
            }

            UnsubscribeFromPlayers();
            StopSpectating("the mod runtime is unloading");
            if (_mistakes != null)
            {
                _mistakes.EndSong();
                _mistakes = null;
            }
            PerformanceDiagnostics.Lifecycle("runtime-destroyed", "update loop and player subscriptions stopped");

            if (Instance == this)
            {
                Instance = null;
            }

            if (_panel != null)
            {
                try
                {
                    _panel.Dispose();
                }
                catch
                {
                    // ignored
                }

                _panel = null;
            }
        }
    }
}
