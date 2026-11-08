using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace MultiplayerLeaderboard.Diagnostics
{
    /// <summary>
    /// Low-frequency performance telemetry for the leaderboard. Frame sampling is two float additions per
    /// frame; all formatting and file output happens once every thirty seconds. This is deliberately separate
    /// from the normal event log so a diagnostic run can be identified and compared with a run where the
    /// leaderboard is disabled.
    /// </summary>
    internal static class PerformanceDiagnostics
    {
        // A thirty-second window keeps the per-song line count small enough that the whole log can be
        // sent for analysis, while still showing whether frame pacing changed during the song.
        private const float SummaryInterval = 30f;
        private const float SlowFrameSeconds = 0.050f;
        private const float HitchThresholdSeconds = 0.020f;
        private const float HitchLogInterval = 0.5f;
        private const int HitchLogLimit = 10;

        private static bool _enabled = true;
        private static bool _active;
        private static float _songStartedAt;
        private static float _windowStartedAt;
        private static float _nextSummaryAt;

        private static int _frames;
        private static double _frameTotalMs;
        private static double _frameMaxMs;
        private static int _slowFrames;
        private static int _hitchFrames;
        private static int _updates;
        private static double _updateTotalMs;
        private static double _updateMaxMs;
        private static int _reads;
        private static double _readTotalMs;
        private static double _readMaxMs;
        private static int _renders;
        private static double _renderTotalMs;
        private static double _renderMaxMs;
        private static long _uiTextChanges;
        private static int _uiLayoutChanges;
        private static long _uiRowsChanged;
        private static int _uiUpdateTicks;
        private static int _uiChangeTicks;
        private static int _uiMaxTextChanges;
        private static int _uiCanvases;
        private static int _uiRowCanvases;
        private static int _uiLabels;
        private static int _maxRows;
        private static int _lastRows;

        private static long _windowGc0;
        private static long _windowGc1;
        private static long _windowGc2;
        private static long _windowManagedBytes;
        private static long _windowUnityBytes;
        private static long _windowMonoBytes;

        private static float _lastHitchAt;
        private static int _hitchLogs;
        private static float _lastUpdateAt;
        private static double _lastUpdateMs;
        private static float _lastReadAt;
        private static double _lastReadMs;
        private static float _lastRenderAt;
        private static double _lastRenderMs;
        private static float _lastUiChangeAt;
        private static int _lastUiTextChanges;
        private static int _lastUiRowsChanged;
        private static bool _lastUiLayoutChanged;

        private static long _windowTickAllocBytes;
        private static int _canvasRebuildFrames;
        private static int _uiApplyTicks;
        private static long _uiAppliedLabels;
        private static float _lastUiApplyAt;
        private static int _lastUiApplied;
        private static bool _uiApplyThisFrame;

        private static int _lastHitchFrame = -1;
        private static int _lastHitchFrameDelta;

        // The game's audio clock is a staircase (it moves in DSP buffer steps) and its controller
        // force-resyncs when it drifts from the wall clock. Reading the song-time delta at a hitch is
        // what shows whether the stall came from that resync/catch-up rather than from this panel.
        private static AudioTimeSyncController _audioSync;
        private static int _audioSyncLookups;
        private static System.Reflection.FieldInfo _forcedSyncField;
        private static bool _forcedSyncFieldLookup;
        private static int _lastCanvasRebuildFrame = -1;
        private static bool _canvasHook;
        private static bool _manual;

        internal static bool Enabled
        {
            get { return _enabled; }
        }

        internal static bool IsActive => _active;
        internal static bool IsManual => _manual;

        internal static void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                return;
            }

            if (!enabled && _active)
            {
                WriteSummary("disabled");
                _active = false;
                _manual = false;
            }

            _enabled = enabled;
            LeaderboardLog.Info("PERF diagnostics " + (enabled ? "enabled" : "disabled") +
                                " from the Multiplayer Leaderboard settings.");
        }

        internal static void BeginSong(int players, bool leaderboardEnabled)
        {
            if (!_enabled)
            {
                return;
            }

            _manual = false;
            BeginWindow(players, leaderboardEnabled, "multiplayer song");
        }

        internal static void BeginManual(string reason)
        {
            if (!_enabled || _active)
            {
                return;
            }

            _manual = true;
            BeginWindow(0, true, reason);
        }

        private static void BeginWindow(int players, bool leaderboardEnabled, string reason)
        {
            if (!_enabled)
            {
                return;
            }

            if (_active)
            {
                WriteSummary("monitor-replaced");
                _active = false;
            }

            _active = true;
            _songStartedAt = Time.unscaledTime;
            _windowStartedAt = _songStartedAt;
            _nextSummaryAt = _songStartedAt + SummaryInterval;
            _frames = 0;
            _frameTotalMs = 0d;
            _frameMaxMs = 0d;
            _slowFrames = 0;
            _hitchFrames = 0;
            _updates = 0;
            _updateTotalMs = 0d;
            _updateMaxMs = 0d;
            _reads = 0;
            _readTotalMs = 0d;
            _readMaxMs = 0d;
            _renders = 0;
            _renderTotalMs = 0d;
            _renderMaxMs = 0d;
            _uiTextChanges = 0;
            _uiLayoutChanges = 0;
            _uiRowsChanged = 0;
            _uiUpdateTicks = 0;
            _uiChangeTicks = 0;
            _uiMaxTextChanges = 0;
            _uiCanvases = 0;
            _uiRowCanvases = 0;
            _uiLabels = 0;
            _maxRows = 0;
            _lastRows = 0;

            _lastHitchAt = 0f;
            _hitchLogs = 0;
            _lastUpdateAt = 0f;
            _lastUpdateMs = 0d;
            _lastReadAt = 0f;
            _lastReadMs = 0d;
            _lastRenderAt = 0f;
            _lastRenderMs = 0d;
            _lastUiChangeAt = 0f;
            _lastUiTextChanges = 0;
            _lastUiRowsChanged = 0;
            _lastUiLayoutChanged = false;
            _windowTickAllocBytes = 0L;
            _canvasRebuildFrames = 0;
            _uiApplyTicks = 0;
            _uiAppliedLabels = 0L;
            _lastUiApplyAt = 0f;
            _lastUiApplied = 0;
            _uiApplyThisFrame = false;
            _lastCanvasRebuildFrame = -1;
            HookCanvas();

            _windowGc0 = GC.CollectionCount(0);
            _windowGc1 = GC.CollectionCount(1);
            _windowGc2 = GC.CollectionCount(2);
            _windowManagedBytes = ManagedBytes();
            _windowUnityBytes = UnityAllocatedBytes();
            _windowMonoBytes = UnityMonoBytes();

            _audioSync = null;
            _audioSyncLookups = 0;
            _nextAudioWarm = 0f;
            _allowAudioScan = false;

            LeaderboardLog.Info("PERF monitoring started (" + reason + "): " + players + " score-sync player(s), leaderboard " +
                                (leaderboardEnabled ? "enabled" : "disabled (baseline: no panel is built)") +
                                ", summary every " + SummaryInterval.ToString("0", CultureInfo.InvariantCulture) +
                                " s. Disable it in the mod's settings if a run without diagnostics is needed.");
        }

        internal static void EndSong(string reason)
        {
            if (!_active)
            {
                return;
            }

            WriteSummary("song-end: " + reason);
            _active = false;
            _manual = false;
        }

        internal static void EndManual(string reason)
        {
            if (!_active || !_manual)
            {
                return;
            }

            WriteSummary("monitor-end: " + reason);
            _active = false;
            _manual = false;
        }

        internal static void Frame()
        {
            if (!_active)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            if (delta < 0f)
            {
                return;
            }

            double milliseconds = delta * 1000d;
            _frames++;
            _frameTotalMs += milliseconds;
            if (milliseconds > _frameMaxMs)
            {
                _frameMaxMs = milliseconds;
            }

            if (delta >= SlowFrameSeconds)
            {
                _slowFrames++;
            }

            // Counted separately from the logged hitches: the log line cap must not hide how many
            // 20 ms+ frames actually occurred in the window.
            if (delta >= HitchThresholdSeconds)
            {
                _hitchFrames++;
            }

            // Record only a bounded number of real hitches. The timing context is what separates a
            // deferred TMP/canvas rebuild from a frame drop elsewhere in the game or headset runtime.
            float now = Time.unscaledTime;
            if (delta >= HitchThresholdSeconds &&
                now - _songStartedAt >= 2f &&
                now - _lastHitchAt >= HitchLogInterval &&
                _hitchLogs < HitchLogLimit)
            {
                float previousHitchAt = _lastHitchAt;
                _lastHitchAt = now;
                _hitchLogs++;
                _lastHitchFrameDelta = _lastHitchFrame < 0 ? -1 : Time.frameCount - _lastHitchFrame;
                _lastHitchFrame = Time.frameCount;
                WriteHitch(now, milliseconds, previousHitchAt,
                           _lastCanvasRebuildFrame == Time.frameCount - 1);
            }
        }

        internal static void Tick()
        {
            if (!_active || Time.unscaledTime < _nextSummaryAt)
            {
                return;
            }

            WriteSummary("periodic");
            _nextSummaryAt = Time.unscaledTime + SummaryInterval;
        }

        internal static long BeginTiming()
        {
            return _active ? Stopwatch.GetTimestamp() : 0L;
        }

        /// <summary>
        /// Timestamp for one-off lifecycle measurements (such as panel construction) that must be
        /// measured even when no song window is active, for example in Test Mode.
        /// </summary>
        internal static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static void EndUpdate(long start)
        {
            if (!_active || start == 0L)
            {
                return;
            }

            double milliseconds = ElapsedMs(start);
            _updates++;
            _updateTotalMs += milliseconds;
            if (milliseconds > _updateMaxMs)
            {
                _updateMaxMs = milliseconds;
            }

            _lastUpdateAt = Time.unscaledTime;
            _lastUpdateMs = milliseconds;
        }

        internal static void EndRead(long start, int rows)
        {
            if (!_active || start == 0L)
            {
                return;
            }

            double milliseconds = ElapsedMs(start);
            _reads++;
            _readTotalMs += milliseconds;
            if (milliseconds > _readMaxMs)
            {
                _readMaxMs = milliseconds;
            }

            _lastReadAt = Time.unscaledTime;
            _lastReadMs = milliseconds;

            _lastRows = rows;
            if (rows > _maxRows)
            {
                _maxRows = rows;
            }
        }

        internal static void EndRender(long start)
        {
            if (!_active || start == 0L)
            {
                return;
            }

            double milliseconds = ElapsedMs(start);
            _renders++;
            _renderTotalMs += milliseconds;
            if (milliseconds > _renderMaxMs)
            {
                _renderMaxMs = milliseconds;
            }

            _lastRenderAt = Time.unscaledTime;
            _lastRenderMs = milliseconds;
        }

        internal static void SetUiTopology(int canvases, int rowCanvases, int labels)
        {
            _uiCanvases = canvases;
            _uiRowCanvases = rowCanvases;
            _uiLabels = labels;
        }

        internal static void PanelDisposed()
        {
            _uiCanvases = 0;
            _uiRowCanvases = 0;
            _uiLabels = 0;
        }

        internal static void RecordUiApply(int applied)
        {
            if (!_active || applied <= 0)
            {
                return;
            }

            _uiApplyTicks++;
            _uiAppliedLabels += applied;
            _lastUiApplyAt = Time.unscaledTime;
            _lastUiApplied = applied;
            _uiApplyThisFrame = true;
        }

        internal static void RecordUiChanges(int textChanges, bool layoutChanged, int rowsChanged)
        {
            if (!_active)
            {
                return;
            }

            _uiUpdateTicks++;
            _uiTextChanges += textChanges;
            _uiRowsChanged += rowsChanged;
            if (layoutChanged)
            {
                _uiLayoutChanges++;
            }

            if (textChanges > 0 || layoutChanged)
            {
                _uiChangeTicks++;
                if (textChanges > _uiMaxTextChanges)
                {
                    _uiMaxTextChanges = textChanges;
                }

                _lastUiChangeAt = Time.unscaledTime;
                _lastUiTextChanges = textChanges;
                _lastUiRowsChanged = rowsChanged;
                _lastUiLayoutChanged = layoutChanged;
            }
        }

        internal static void RecordTickAllocation(long bytes)
        {
            if (_active && bytes > 0L)
            {
                _windowTickAllocBytes += bytes;
            }
        }

        private static void WriteHitch(float now, double frameMilliseconds, float previousHitchAt,
                                       bool canvasRebuildPreviousFrame)
        {
            // One compact line per hitch. The panel-side timing context is kept (update/render/ui apply),
            // while duplicated read timing and always-present fields were dropped; the window summary still
            // reports the read maximum, so a slow read cannot hide here.
            StringBuilder builder = new StringBuilder(320);
            builder.Append("PERF hitch: frame_ms=")
                   .Append(frameMilliseconds.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", at_song_s=").Append((now - _songStartedAt).ToString("0.00", CultureInfo.InvariantCulture))
                   .Append(", since_last_hitch_ms=").Append(AgeMs(now, previousHitchAt).ToString("0.0", CultureInfo.InvariantCulture))
                   .Append(", since_update_ms=").Append(AgeMs(now, _lastUpdateAt).ToString("0.0", CultureInfo.InvariantCulture))
                   .Append(", update_ms=").Append(_lastUpdateMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", render_ms=").Append(_lastRenderMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", since_ui_change_ms=").Append(AgeMs(now, _lastUiChangeAt).ToString("0.0", CultureInfo.InvariantCulture))
                   .Append(", ui_text_changes=").Append(_lastUiTextChanges)
                   .Append(", ui_rows_changed=").Append(_lastUiRowsChanged)
                   .Append(", since_ui_apply_ms=").Append(AgeMs(now, _lastUiApplyAt).ToString("0.0", CultureInfo.InvariantCulture))
                   .Append(", ui_applied=").Append(_lastUiApplied)
                   .Append(", ui_apply_prev=").Append(canvasRebuildPreviousFrame ? 1 : 0)
                   .Append(", frames_since_hitch=").Append(_lastHitchFrameDelta)
                   .Append(", panel=").Append(_uiCanvases > 0 ? 1 : 0)
                   .Append(", song_delta_ms=").Append(SongDeltaMs().ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", forced_sync_s=").Append(ForcedSyncSeconds().ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(".");

            LeaderboardLog.Info(builder.ToString());
        }        private static double SongTime()
        {
            EnsureAudioSync();
            try
            {
                return _audioSync != null ? _audioSync.songTime : -1d;
            }
            catch (Exception)
            {
                return -1d;
            }
        }

        private static double SongDeltaMs()
        {
            EnsureAudioSync();
            try
            {
                return _audioSync != null ? _audioSync.lastFrameDeltaSongTime * 1000d : -1d;
            }
            catch (Exception)
            {
                return -1d;
            }
        }

        private static double ForcedSyncSeconds()
        {
            EnsureAudioSync();
            try
            {
                if (!_forcedSyncFieldLookup && _audioSync != null)
                {
                    _forcedSyncFieldLookup = true;
                    _forcedSyncField = typeof(AudioTimeSyncController).GetField(
                        "_forcedSyncDeltaTime", System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.NonPublic);
                }

                if (_forcedSyncField != null && _audioSync != null)
                {
                    object value = _forcedSyncField.GetValue(_audioSync);
                    return value is float ? (float)value : -1d;
                }
            }
            catch (Exception)
            {
                return -1d;
            }

            return -1d;
        }

        private static float _nextAudioWarm;
        private static bool _allowAudioScan;

        private static void EnsureAudioSync()
        {
            if (_audioSync != null && _audioSync.isActiveAndEnabled)
            {
                return;
            }

            _audioSync = null;
            if (!_allowAudioScan || _audioSyncLookups >= 8)
            {
                return;
            }

            _audioSyncLookups++;
            try
            {
                AudioTimeSyncController[] controllers =
                    UnityEngine.Object.FindObjectsByType<AudioTimeSyncController>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (controllers != null)
                {
                    for (int i = 0; i < controllers.Length; i++)
                    {
                        AudioTimeSyncController candidate = controllers[i];
                        if (candidate != null && candidate.isActiveAndEnabled)
                        {
                            _audioSync = candidate;
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                _audioSync = null;
            }
        }

        // use a tick system now. if it scans every audio controller, and flushes logs on the same frame... we probably get an studder.
        internal static void WarmAudioSync(float now)
        {
            if (now < _nextAudioWarm)
            {
                return;
            }

            _nextAudioWarm = now + 2f;
            _allowAudioScan = true;
            EnsureAudioSync();
            _allowAudioScan = false;
        }

        private static double AgeMs(float now, float then)        {
            return then <= 0f ? -1d : Mathf.Max(0f, now - then) * 1000d;
        }

        internal static void Lifecycle(string eventName, string detail)
        {
            if (!_enabled)
            {
                return;
            }

            LeaderboardLog.Info("PERF lifecycle: " + eventName + (string.IsNullOrEmpty(detail) ? string.Empty : " - " + detail));
        }

        private static void WriteSummary(string reason)
        {
            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0.001f, now - _windowStartedAt);
            double seconds = elapsed;
            double averageFrame = _frames > 0 ? _frameTotalMs / _frames : 0d;
            double averageUpdate = _updates > 0 ? _updateTotalMs / _updates : 0d;

            long managed = ManagedBytes();
            long unity = UnityAllocatedBytes();
            long mono = UnityMonoBytes();
            long gc0 = GC.CollectionCount(0);
            long gc1 = GC.CollectionCount(1);
            long gc2 = GC.CollectionCount(2);

            // Compact by design: the same evidence that identifies a panel-caused hitch (frame pacing,
            // per-stage maxima, UI changes and canvas applies, memory/GC, hitch count) in one short line
            // instead of a ~730 character block four times a minute.
            StringBuilder builder = new StringBuilder(384);
            builder.Append("PERF summary (").Append(reason).Append("): elapsed=")
                   .Append(seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("s")
                   .Append(", frames=").Append(_frames)
                   .Append(", avg_frame_ms=").Append(averageFrame.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", max_frame_ms=").Append(_frameMaxMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", over_20ms=").Append(_hitchFrames)
                   .Append(", over_50ms=").Append(_slowFrames)
                   .Append("; update_avg_ms=").Append(averageUpdate.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", update_max_ms=").Append(_updateMaxMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", read_max_ms=").Append(_readMaxMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append(", render_max_ms=").Append(_renderMaxMs.ToString("0.000", CultureInfo.InvariantCulture))
                   .Append("; ui_text_changes=").Append(_uiTextChanges)
                   .Append(", ui_max_changes=").Append(_uiMaxTextChanges)
                   .Append(", ui_apply_ticks=").Append(_uiApplyTicks)
                   .Append(", ui_applied_labels=").Append(_uiAppliedLabels)
                   .Append(", ui_canvas_rebuilds=").Append(_canvasRebuildFrames)
                   .Append(", rows=").Append(_lastRows)
                   .Append(", max_rows=").Append(_maxRows)
                   .Append("; managed_delta_KB=").Append(Kilobytes(managed - _windowManagedBytes).ToString("0.0", CultureInfo.InvariantCulture))
                   .Append(", gc=").Append(gc0 - _windowGc0).Append('/').Append(gc1 - _windowGc1).Append('/').Append(gc2 - _windowGc2)
                   .Append(", hitch_logs=").Append(_hitchLogs)
                   .Append(".");

            LeaderboardLog.Info(builder.ToString());
            LeaderboardLog.Flush();

            _frames = 0;
            _frameTotalMs = 0d;
            _frameMaxMs = 0d;
            _slowFrames = 0;
            _hitchFrames = 0;
            _updates = 0;
            _updateTotalMs = 0d;
            _updateMaxMs = 0d;
            _reads = 0;
            _readTotalMs = 0d;
            _readMaxMs = 0d;
            _renders = 0;
            _renderTotalMs = 0d;
            _renderMaxMs = 0d;
            _uiTextChanges = 0;
            _uiLayoutChanges = 0;
            _uiRowsChanged = 0;
            _uiUpdateTicks = 0;
            _uiChangeTicks = 0;
            _uiMaxTextChanges = 0;
            _maxRows = 0;
            _windowTickAllocBytes = 0L;
            _canvasRebuildFrames = 0;
            _uiApplyTicks = 0;
            _uiAppliedLabels = 0L;
            _windowStartedAt = now;

            _windowGc0 = gc0;
            _windowGc1 = gc1;
            _windowGc2 = gc2;
            _windowManagedBytes = managed;
            _windowUnityBytes = unity;
            _windowMonoBytes = mono;
        }

        private static void HookCanvas()
        {
            if (_canvasHook)
            {
                return;
            }

            _canvasHook = true;
            Canvas.willRenderCanvases += HandleWillRenderCanvases;
        }

        private static void HandleWillRenderCanvases()
        {
            if (!_active || !_uiApplyThisFrame)
            {
                return;
            }

            _uiApplyThisFrame = false;
            _canvasRebuildFrames++;
            _lastCanvasRebuildFrame = Time.frameCount;
        }

        internal static double ElapsedMs(long start)
        {
            if (start == 0L)
            {
                return 0d;
            }

            long now = Stopwatch.GetTimestamp();
            if (now <= start)
            {
                return 0d;
            }

            return (now - start) * 1000d / Stopwatch.Frequency;
        }

        private static long ManagedBytes()
        {
            try
            {
                return GC.GetTotalMemory(false);
            }
            catch
            {
                return 0L;
            }
        }

        private static long UnityAllocatedBytes()
        {
            try
            {
                return Profiler.GetTotalAllocatedMemoryLong();
            }
            catch
            {
                return 0L;
            }
        }

        private static long UnityMonoBytes()
        {
            try
            {
                return Profiler.GetMonoUsedSizeLong();
            }
            catch
            {
                return 0L;
            }
        }

        private static double Megabytes(long bytes)
        {
            return bytes <= 0L ? 0d : bytes / (1024d * 1024d);
        }

        private static double Kilobytes(long bytes)
        {
            return bytes / 1024d;
        }
    }
}
