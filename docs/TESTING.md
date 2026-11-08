# Test record

Current build: **MultiplayerLeaderboard 1.4.0**, SHA256
`c15db6ba243a389efb5f51aec85c8bc52822331168e797e7b89328043e614a68`, 203,264 bytes.

Target used for all checks: `<Beat Saber install>`
(Beat Saber 1.40.3_4614, BSIPA, BSML 1.12.5). No other Beat Saber installation was
launched or modified.

## A. Build

```
./build.sh "<Beat Saber install>"
```

* Release build: **0 warnings, 0 errors**.
* Target framework `net472`, IL-only managed DLL, assembly `MultiplayerLeaderboard 1.4.0.0`.
* Manifest embedded with BSIPA and BeatSaberMarkupLanguage dependencies; no loose manifest
  is produced or copied.
* Package checks: no Kick Mod/Ping Mod/SiraUtil/BS_Utils name and no absolute Windows user
  path in the DLL.

## B. Static verification of the compiled DLL

```
dotnet run --project tools/Verify -c Release -- \
  --dll MultiplayerLeaderboard/dist/MultiplayerLeaderboard.dll \
  --game "<Beat Saber install>"
```

Result: **PASS - 77 checks, 0 failures.**

Covered: assembly identity, IL-only, .NET Framework 4.x target; every assembly reference
resolves from the game, BSIPA or Plugins; embedded manifest 1.4.0 with only BSIPA and BSML;
the BSIPA plugin contract; every game member used, including
`MultiplayerPlayersManager.playerDidFinishEvent` and
`playerNetworkDidFailedEvent`; BSML referenced explicitly; no reference to Kick Mod, Ping
Mod, SiraUtil, BS_Utils or Harmony; no absolute developer path in emitted strings.

## C. Normal-settings restart regression (issue 1)

### Cause found in the game code

Beat Saber's normal Settings flow does not merely dismiss its view when **OK** or **Apply**
is pressed. `SettingsFlowCoordinator` saves the settings and invokes
`MainFlowCoordinator.HandleSettingsFlowCoordinatorDidFinish`; for `Ok`/`Apply` that method
calls:

```
_menuTransitionsHelper.RestartGame();
```

`MenuTransitionsHelper.RestartGame()` clears and reopens the app-init scenes. The rebuilt
main menu constructs a new BSML Zenject container, `MenuButtons` and `GameplaySetup`. The
plugin's DontDestroyOnLoad host survives. In 1.2.0 the host kept `_registered = true`, so it
never registered its button on the rebuilt `MenuButtons`; the old button object belonged to
the destroyed menu. The same one-shot flag also skipped the Gameplay Setup tab.

### Fix

`LeaderboardMenuIntegration` now re-registers on every main-menu update:

* `MenuButtons.Instance.RegisterButton(_button)` is idempotent: BSML refuses a second button
  with the same text, so repeated calls cannot duplicate it.
* `GameplaySetup.Instance.AddTab(...)` is likewise idempotent by tab name.
* A rebuilt BSML instance is detected and logged as a re-registration; a normal first
  registration logs as before.
* Shutdown unregisters from the instance that was actually used, not from a potentially
  already-destroyed singleton.

### Test performed

The exact method called by normal Settings **OK/Apply** was invoked through a temporary
desktop test key:

```
MenuTransitionsHelper.RestartGame()
```

The test key was removed from the final DLL. Observed sequence after each restart:

```
TEST: restarting the game through MenuTransitionsHelper.RestartGame().
Scene changed: 'PCInit'.
Scene changed: 'MainMenu'.
Mods menu was rebuilt; button re-registered: 'Multiplayer Leaderboard'.
Gameplay Setup was rebuilt; tab re-registered: 'Multiplayer Leaderboard'.
```

The restart was performed **three consecutive times**. Every time the button and tab
returned, and the on-screen Mods pane showed exactly one `MULTIPLAYER LEADERBOARD` entry.

After a restart, the button's own click handler was invoked through a second temporary test
key. The Multiplayer Leaderboard settings page opened normally and rendered its sections.
That key was also removed from the final DLL.

The final installed DLL was then launched cleanly. Its log shows:

```
Multiplayer Leaderboard 1.2.1 - diagnostics
Mods menu button registered: 'Multiplayer Leaderboard'.
Gameplay Setup tab registered: 'Multiplayer Leaderboard'.
```

No duplicate button, missing reference or exception appeared in the final launch log.

### What remains for the user

The actual physical click on normal Settings **OK** was not automated (Beat Saber's FPFC
input ignores synthesized mouse clicks). The game code path taken by that click was tested
directly via `MenuTransitionsHelper.RestartGame()`, which is the only thing that path does
to the menu. If you press OK and still lose the button, send the log; it will show whether
`Mods menu was rebuilt` was reached.

## D. Active-player / zero-score regression (issue 2)

### Cause found in the game code

The mod previously used `IConnectedPlayer.IsFailed()` for every row. In this game build that
method is:

```
public static bool IsFailed(this IConnectedPlayer player)
{
    if (!player.WasActiveAtLevelStart() || player.IsActiveOrFinished())
        return !player.isConnected;
    return true;
}
```

It is a derived `is_active` state test, not a level-failure event. At the start of a
multiplayer level, or while the game's state packet has not reported `is_active` for the
local player, it returns `true` even though the player is on the play field with full energy
and zero score. That is why the screenshot showed `FAILED` beside a live zero-score player.
The previous mod also required `maxMultipliedScore > 0` before setting a percentage, so the
accuracy column fell back to `-` in the same state.

### Fix

* The local player's failure is now taken only from Beat Saber's authoritative local
  level-end event: `MultiplayerPlayersManager.playerDidFinishEvent` and
  `playerNetworkDidFailedEvent`, with
  `MultiplayerLevelCompletionResults.MultiplayerPlayerLevelEndReason.Failed`.
* `IConnectedPlayer.IsFailed()` is still used for remote rows, because that is the only
  state the multiplayer protocol exposes for another player, but only after a two-second
  initial-state grace period so a missing opening packet cannot create a false failure.
* A zero score is never a failure condition.
* When the live score controller reports both `multipliedScore` and
  `immediateMaxPossibleMultipliedScore` as zero at song start, accuracy is shown as
  `0.00%`, matching the game's own HUD. A non-zero numerator with no denominator still
  shows `-` instead of a fabricated percentage.
* The same local `ScoreController` values continue to update score and accuracy every tick;
  remote rows continue to use the synced score table and its shared offset time.
* A local player who completes the song keeps the normal score display; a local player who
  gives up is not turned into a `FAILED` row.

### Test status

This fix could not be exercised in a live multiplayer song because no multiplayer lobby,
second player or real multiplayer session was available in this test run. The code path is
built and statically verified, and the failure source is the game's own level-end event
rather than a zero-score heuristic. **The active zero-score row must be confirmed in your
next real multiplayer song**; the log will contain either the normal
`Player data retrieved ... score 0, accuracy 0.00%` line or the explicit
`Local player failure confirmed by the game's level-end event` line if the game really
failed the player.

I am not claiming a live-song pass for this half of the change.

## E. Live launch checks retained from earlier tests

These settings/UI behaviours are unchanged in 1.2.6 and were previously verified:

| Check | Result |
|---|---|
| Mod initializes | PASS |
| Mods button registers | PASS |
| Gameplay Setup tab registers | PASS |
| Settings page opens and renders all sections | PASS |
| Test Mode preview created outside multiplayer | PASS |
| Preview data labelled `PREVIEW DATA` / `PREVIEW PLAYER n` | PASS |
| Default preview fully visible on screen | PASS at bottom-centre `(-0.06, 1.72, 0.70)`, 2.00 m out, 0° up |
| Saved position and appearance restored after restart | PASS |
| Turning Test Mode off hides preview and keeps saved values | PASS |
| Multiplayer-only idle state restored with Test Mode off | PASS |
| Drag changes position only, rotation unchanged | PASS in desktop pointer test |
| No exceptions/errors in the launch log | PASS |

Saved restore test (temporary INI, removed afterwards):

```
[LeaderboardPosition]
X=0.0000  Y=1.4000  Z=0.7000

[LeaderboardRotation]
X=0.0000  Y=0.0000  Z=0.0000
QX=0.0000 QY=0.0000 QZ=0.0000 QW=1.0000

[LeaderboardScale]
Overall=1.0000 Text=1.1500 Name=1.1000
Accuracy=1.0500 Score=1.1000 RowSpacing=1.0500

[Background]
Width=1.7600 Height=0.3800 WidthScale=1.1000 HeightScale=1.1000

[Visibility]
Enabled=true TestMode=false
```

The same values produced a **2.18 x 0.43 m** panel at exactly `(0.00, 1.40, 0.70)`,
with all six preview rows and the score column inside the background.

## F. In-song performance and diagnostics (issue 4)

### Code-inspection findings

The live log from the user's 1.40.3 instance showed a healthy panel with no runtime exception,
but the in-song tick was doing avoidable work four times per second. A later 1.2.3 run measured
the result: `avg_update_ms=8.213` (max 36.294) while `avg_read_ms=0.064` and
`avg_render_ms=0.054`. The missing 8 ms was the local-score lookup: because the local player was
in an inactive/spectating state, no `ScoreController` was found, so `ResolveLocalScore` called
`Resources.FindObjectsOfTypeAll<ScoreController>()` on every 250 ms tick. That global scan also
allocated an array each time and grew managed memory between samples. This is the measured
root cause of the remaining stutter, not an assumption.

* `RefreshRows` allocated a fresh `List` for score states and a fresh `PlayerRow` for every
  player on every tick.
* Every row's `DisplayName`, `RankText`, `AccuracyText` and `ScoreText` property built a new
  string on every tick, even when the value had not changed.
* `EnsureFonts` re-applied font/material work to every label on every tick.
* `LogOrder` built the complete ranking string on every tick even though it only logged every
  five seconds.
* `TrackSessionPlayers` allocated a list, a dictionary and a summary string every tick, and
  copied its roster dictionary even when nothing had changed.
* `StatusBoard.Suspend/Resume` rebuilt its signature every tick even when no status value had
  changed.
* During the spawn transition, `TryResolveStage` logged the same fallback warning four times a
  second.
* Each panel/editor text received its own `new Material`; those materials were not destroyed
  when the panel was removed. This was a bounded leak per panel creation, separate from the
  per-tick allocation path.
* When no local score source was found, `ResolveLocalScore` ran a global
  `Resources.FindObjectsOfTypeAll<ScoreController>()` scan on every tick. The 1.2.3 PERF data
  shows this was the surviving 8 ms cost and the source of the repeated managed allocations.

These findings are code-level evidence. The PERF summaries distinguish CPU from GC from
rendering in the user's actual scene, and the 1.2.3 measurement identified the local-score scan
as the remaining measured problem.

### Changes

* Reused row objects are keyed by player id (or a safe name/index fallback), and the score-state
  buffer is reused and cleared after each read.
* `PlayerRow` caches its formatted name, rank, accuracy and score strings against the values
  that produced them.
* `WorldLeaderboardPanel.Render` only writes a text value when it changed; footer text is cached;
  layout is only rebuilt when the visible row count changes.
* Visible text is refreshed at 2 Hz during play and 1 Hz while spectating instead of on every
  4 Hz data tick. The spectator PERF window showed the tick itself was cheap (`update_max_ms=2.438`,
  `read_max_ms=0.047`, `render_max_ms=0.051`) but each applied frame paid a TMP/canvas rebuild:
  108 apply frames produced 83 frames over 20 ms in 30 seconds. Score reads stay at 4 Hz, so rank
  order, footer counts and spectator tracking are unchanged.
* Attaching or detaching the spectator panel re-applies only the root scale instead of the full
  label layout; the native spectator information search runs once per song; and camera
  re-resolution is limited to one attempt every two seconds while the gameplay camera is disabled
  during a spectator switch.
* Font setup stops after every label has a usable TMP font. The original implementation read
  `TMP_Text.fontMaterial`, whose getter creates a private material instance per label; 1.3.0
  replaces that with one shared `fontSharedMaterial`. Both the panel and editor destroy their
  owned material when disposed.
* Session roster comparison uses reusable buffers, runs at most twice a second, and only copies
  the roster when it actually changes.
* `StatusBoard` only writes a single `STATUS:` line, and only when a value actually changed.
* Ranking text is built only when its thirty-second log interval is due, and is flattened to one
  bounded line so a player name with line breaks cannot split it across file lines.
* Spawn fallback warnings are throttled to once every two seconds during the transition.
* `ResolveLocalScore` now looks for a `ScoreController` directly on the local multiplayer player
  or active local player facade. It retries that cheap lookup at most every two seconds and
  limits the global `Resources` fallback to two scans per song instead of running it every tick.
* Added `Performance Diagnostics` (default on): a PERF summary with frame time, update/read/render
  time, UI text/layout changes and GC counts; lifecycle lines for runtime, panel and event
  subscription creation/removal; duplicate-runtime protection. The summary covers the current
  window rather than the whole song.
* 1.2.5 adds up to 24 bounded `PERF hitch` lines per song for frames at or above 20 ms, with the
  time since the last update/read/render and the last UI text/layout change. It also skips TMP
  color assignments when the color is unchanged.
* 1.2.6 makes the normal in-song panel display-only: no `VRGraphicRaycaster`, no raycast target
  and no hover-driven TMP backdrop rebuild. Interaction is enabled only in edit/Test Mode editing
  and removed when editing ends. This removes the measured 26 ms panel hitch pattern.
* 1.2.7 puts each live row on its own small world-space canvas. A score or accuracy change now
  marks only that row's canvas dirty instead of rebuilding the main panel canvas with the
  backdrop, header and every other row.
* 1.3.0 fixes two TMP/material defects found by comparing against the game's HUD. The old code
  read `TMP_Text.fontMaterial`, whose getter creates a private material instance per label;
  this defeated batching and retained materials. Panel and editor labels now use one
  `fontSharedMaterial`. Row objects and their nested canvases are created on first use instead
  of prebuilding ten hidden rows. The PERF summary now reports `ui_rows_changed`,
  `ui_canvases`, `ui_row_canvases` and `ui_labels`.
* 1.4.0 bounds log volume for long sessions: the PERF summary is one compact line every thirty
  seconds (down from a ~730-character line every ten), at most ten `PERF hitch` lines per window
  and at least half a second apart, the status block is a single `STATUS:` line, the spectator
  "no native panel" line is written once per runtime, camera lines only on the first resolve or
  an actual camera change, and the startup banner/detection output is collapsed.

### Test status

Build: **0 warnings, 0 errors**. Static verifier: **PASS - 77 checks, 0 failures** for SHA256
`b1afa4c277a36c25f6268642e95e9b034a38d4db429430ab83bafc562ae12122`, 121,856 bytes. The final DLL
was installed only in the authorized instance. A Test Mode launch built the preview panel,
attached the VR raycaster only for edit interaction, and rendered all six preview rows through
the per-row canvases with no layout or rendering errors. The log reported 7 canvases (one main
plus six rows), 6 row canvases and 32 TMP labels, proving the lazy-row path and shared material.

The user's 1.2.4 in-song run is measured evidence for the local-score fix: **0.037 ms average /
0.107 ms maximum update**, **0.011 ms read**, **0.009 ms render**, no GC. The 1.2.5 run is the
measured evidence for the panel cause. With the panel on: **883 frames / 10.2 s**, **11.600 ms
average**, **145.090 ms maximum**, and **13 logged hitches of about 26 ms** at roughly **355 ms
intervals**. With the panel disabled in the next song: **900 frames / 10.2 s**, **11.358 ms
average**, **78.289 ms maximum**, and no repeating 26 ms pattern. The 1.2.6 panel-off pointer path
is **not yet measured**; the next user run is required.

## G. Remote player score and accuracy (issue 3)

### Cause

On a duel, the mod read every remote row from the score-state object found by scanning the
scene for `ScoreSyncStateManager`. The game's own opponent score display uses its injected
`MultiplayerScoreProvider` and that provider's own per-player `MultiplayerSyncState` instead.
The remote player existed in the scanned table, but its values stayed at zero while the local
live `ScoreController` advanced. That produced `2 DOGGY.PPK 0.00% 0` for the whole song.

### Fix

* The runtime now resolves the game's `MultiplayerScoreProvider` first and uses its exact
  injected score-state manager. The old scene scan remains only as a fallback for older or
  patched scenes.
* Remote score comes from `MultiplayerScoreProvider.TryGetScore(userId).score`, the same value
  the game's own multiplayer leaderboard and duel score display use.
* Remote accuracy comes from the latest `MultipliedScore` and
  `ImmediateMaxPossibleMultipliedScore` in that provider's own `RankedPlayer` sync state,
  read through its private `_multiplayerSyncState` field. The latest packet is used because
  this is a leaderboard update, not a frame-smoothed HUD animation.
* If a remote score is available but its denominator has not arrived, the accuracy cell stays
  `-`; it is never shown as a fabricated `0.00%`.
* A one-time warning is logged if the provider has a remote score but no accuracy state.

### Test status

Static verification now checks `MultiplayerScoreProvider.TryGetScore`, `RankedPlayer.score`,
`RankedPlayer.userId` and `RankedPlayer._multiplayerSyncState`. The build passes **77 checks,
0 failures**.

A live two-player song was not available in this run. The previous live log from the user's
1.40.3 instance showed the exact failure (`DOGGY.PPK 0.00% 0` while the local row advanced);
the corrected remote-provider path has not yet been exercised against a real second player.
**This remains to be confirmed in your next live multiplayer song.**

## H. Still unverified (needs the user's live run)

* A real multiplayer song/lobby: active zero-score display, real player names and ranking,
  live updates, failed/completed transitions, join/leave, timer-HUD anchoring and cleanup.
  No multiplayer session or second player was available in this run.
* Clicking the normal game Settings **OK** button with a physical controller/pointer. The
  exact `RestartGame()` code path was exercised three times instead.
* Opening the Gameplay Setup tab from the pause menu. Registration and re-registration are
  verified; the pause-menu navigation itself was not.
* Clicking every settings control with a real pointer. Controls render and their bound
  runtime methods were exercised directly; synthetically injected mouse clicks are ignored
  by this game's FPFC input.
* Dragging while a song is paused in a real song.
* VR controller trigger dragging (desktop pointer dragging was verified).

## I. Known limits

* Only the top 10 rows fit. Vanilla multiplayer is 5 players.
* Accuracy is Beat Saber's score percentage: multiplied score / maximum multiplied score
  for the notes played. The multiplayer protocol has no separate cut-accuracy field.
* The local row uses the live `ScoreController`; remote rows use the provider's latest
  received packet, so remote rows can trail the local row by the network offset.
* If the timer HUD, then the combo HUD, cannot be found, the fixed fallback is used and
  logged.
* F8 is a keyboard shortcut, so opening the in-world editor inside a headset needs a
  keyboard or the desktop `-fpfc` window. The Mods page is the primary configuration
  surface.
* The same settings page is registered as a Gameplay Setup tab for the pause menu's Mods
  panel, in addition to the main-menu Mods button.
