# Multiplayer Leaderboard 1.4.0

A standalone Beat Saber mod that draws a live multiplayer leaderboard inside VR during a
multiplayer song, in a fixed world-space position. It shows rank, player name, accuracy and live
score.

Version 1.4.0 keeps every displayed field and changes when the visible text is applied: scores are
still read four times a second, but labels refresh twice a second in normal play and once a second
while spectating. Spectator attach/detach now re-applies only the panel scale instead of the full
label layout, the native-panel hierarchy scan runs once per song, and camera re-resolution is
capped while the gameplay camera is disabled. This targets the measured TMP/canvas rebuild cost
without removing information.

Version 1.3.0 follows the UI patterns used by Beat Saber's own gameplay HUD. It fixes two TMP
implementation defects found during that comparison: the `fontMaterial` getter was creating a
private material for every label (defeating batching), and the panel prebuilt ten row canvases
even for a two-player lobby. Labels now share one panel material, and row canvases are created
on first use and then reused. The ten-second summary also reports UI row changes, canvas count
and TMP label count. See `UI-ARCHITECTURE.md` for the game-vs-mod comparison.

Version 1.2.7 isolates every live row on its own small world-space canvas. A score or accuracy
change now rebuilds only that row's canvas instead of the main leaderboard canvas. This targets the
remaining hitch seen in 1.2.6 when score and accuracy rows were live.

Version 1.2.6 fixed the panel-specific stutter found with the 1.2.5 hitch log. With the panel
visible, the game produced a **26 ms frame every ~355 ms**; with the panel disabled, the same
song averaged **11.358 ms** with no repeating 26 ms stall. The leaderboard update itself measured
about **0.04 ms**, so the cost was the panel's pointer path: its VR raycaster and backdrop were
always active, and the large world-space panel received repeated hover events while the controller
ray crossed it. Every hover rebuilt the TMP `<mark>` backdrop with `ForceMeshUpdate`.

1.2.6 removes that work from normal gameplay. The in-song panel is display-only: it has no VR
raycaster and no raycast target, and hover no longer rebuilds the backdrop. The raycaster and
pointer handlers are created only when you open edit mode or Test Mode editing and are removed
when you leave. Score updates, ranking, accuracy, failure handling, saved settings and the panel's
appearance are unchanged.

Version 1.2.5 added the bounded `PERF hitch` diagnostics used to find this pattern and avoided
redundant TMP color writes. The 1.2.4 run had already measured the four-per-second update loop at
**0.037 ms average / 0.107 ms maximum**, with score reading at **0.011 ms** and rendering at
**0.009 ms**; no GC ran.

Version 1.2.4 fixed the previously measured 8 ms update cost. A 1.2.3 run showed the leaderboard
update taking about **8.2 ms on average** even though score reading took **0.06 ms** and
rendering **0.05 ms**. The time was a global `Resources.FindObjectsOfTypeAll<ScoreController>()`
scan running every 250 ms while no local `ScoreController` existed. 1.2.4 looks for the score
controller directly on the local multiplayer player and limits the global fallback to two scans
per song.

Version 1.2.4 also keeps the 1.2.3 allocation/UI work removal: rows and score buffers are reused,
unchanged text is not re-formatted and font setup stops after it succeeds. It added a **Performance
Diagnostics** toggle in the mod's settings; when on, one memory/GC/update-cost summary is written
to the mod log every ten seconds. The leaderboard itself is unchanged when diagnostics are off.
The material handling in 1.2.4 was not actually shared: reading `TMP_Text.fontMaterial` creates a
private material instance per label. 1.3.0 fixes that with `fontSharedMaterial`.

Version 1.2.2 fixed remote players' scores and accuracy not advancing on the leaderboard.
Remote rows now use the game's own `MultiplayerScoreProvider` and the provider's per-player
score-sync state, which is the same source Beat Saber uses for its duel score display. Version
1.2.1 fixed the Mods button disappearing after the game's normal Settings page is confirmed
with **OK/Apply**, and prevented an active zero-score player from being shown as **FAILED**.
Version 1.2.0 added the normal mod page to Beat Saber's **Mods** category. The page controls
visibility, Test Mode, position, text sizes, row spacing and background size. Test Mode keeps
a clearly labelled **PREVIEW DATA** panel visible outside multiplayer so it can be positioned
whenever you like, including while a song is paused.

```
MULTIPLAYER LEADERBOARD
PLAYER                    ACCURACY        SCORE
1   Player_A                95.87%    1,743,725
2   Player_B                91.94%      688,111
3   Player_C                89.81%      659,999
4   Player_D                85.52%      659,000
```

The board is a world-space canvas placed once. It is **not** parented to the camera, is **not**
billboarded, and is never made to follow your head. Dragging changes position only; rotation is
never changed by a drag or by the position controls.

## Requirements

| Item | Version |
|---|---|
| Beat Saber (PC / Steam or Oculus PC) | 1.40.3 |
| BSIPA (mod loader) | 4.3.0 or newer |
| BeatSaberMarkupLanguage (BSML) | 1.12.5 or newer |
| Other mods | none |

BSML provides the standard Mods-pane button and settings page used by the other mods in the
screenshots. It is an explicit dependency in the embedded manifest. The Kick Mod, Ping Mod,
SiraUtil and Harmony are not used.

## Installation

1. Close Beat Saber.
2. Copy `MultiplayerLeaderboard.dll` into `<Beat Saber>\Plugins\`.
3. Make sure BSML is installed in the same `Plugins` folder (`BSML.dll`). It is already present
   in the test instance and is normally installed by every mod manager.
4. Start Beat Saber.

Do not copy a loose `manifest.json` next to the DLL; the manifest is embedded in the DLL.

Removal: delete `MultiplayerLeaderboard.dll`. See `UNINSTALL.txt`.

## Opening the settings

1. From the main menu, open **Mods** on the left.
2. Select **Multiplayer Leaderboard**.
3. A normal Beat Saber settings page opens with the game's back button.

The page is grouped into **Visibility**, **Position**, **Scaling and Appearance**, and
**Configuration**. The same page is also registered as a Gameplay Setup tab in Beat Saber's
Mods panel, so it can be opened from the pause menu during a song without leaving the level.

Beat Saber restarts its main-menu scene when the normal game **Settings** page is confirmed with
**OK** or **Apply** (`MenuTransitionsHelper.RestartGame()`). That rebuilds BSML's menu container;
this mod detects the rebuilt menu and re-registers its button and Gameplay Setup tab. There is
still exactly one button because BSML rejects duplicate button names. Your saved leaderboard
configuration is untouched by that restart.

### Visibility

* **Leaderboard Enabled** — master switch. Off hides the panel in songs and Test Mode.
* **Test Mode** — keeps the labelled preview panel visible outside multiplayer. Turning Test
  Mode off removes the preview and restores multiplayer-only activation. It does **not** erase
  any saved position, scale or appearance value.

Test Mode always displays sample players named `PREVIEW PLAYER 1`…`6`. The title includes
`PREVIEW DATA`, and the footer says it is not a live multiplayer session. Preview values are
never presented as real players or scores.

### Position

* **Edit Position** — enables the game laser pointer for the panel.
* **Move Left / Move Right / Move Up / Move Down / Move Closer / Move Farther** — small nudges.
* **Pause / Resume Dragging** — toggles pointer dragging without closing the page.
* **Save Position** — writes the exact world position and rotation.
* **Reset Position** — restores the current session's default placement.
* **Restore Saved Position** — re-applies the position stored on disk.
* **Exit Edit Mode** — stops dragging.

Dragging with the laser pointer changes **position only**. Rotation is preserved exactly and no
control rotates the panel.

### Scaling and Appearance

Sliders for Overall UI Scale, Text Size, Player Name Size, Accuracy Size, Score Size, Row
Spacing, Background Width and Background Height. The columns and background grow with the text,
so content stays inside the dark panel. **Reset Scale and Background** returns these to 1.00.

### Configuration

* **Save Settings** — writes position, rotation, all scale and background values, visibility and
  Test Mode state.
* **Reset All Settings** — restores defaults and clears the saved position.

### Positioning while paused

Test Mode keeps the panel alive when a song is paused. Pause the song, use the game's normal
laser pointer to drag the panel, then open the Mods tab in the pause menu (the same settings page)
if you want exact nudges or slider changes. The pause menu itself is not intercepted or replaced.

## Where settings are saved

```
<Beat Saber>\UserData\MultiplayerLeaderboard\UISettings.ini
```

A diagnostic copy is written to:

```
<Beat Saber>\Logs\MultiplayerLeaderboard\UISettings.txt
```

The INI stores:

* `[LeaderboardPosition]` — world X/Y/Z of the panel's bottom-centre pivot.
* `[LeaderboardRotation]` — Euler X/Y/Z and quaternion QX/QY/QZ/QW for exact restore.
* `[LeaderboardScale]` — Overall, Text, Name, Accuracy, Score, RowSpacing.
* `[Background]` — WidthScale, HeightScale and measured metre dimensions.
* `[Visibility]` — Enabled, TestMode.
* `[Meta]` — save timestamp.

Settings load automatically at startup. No position section is written until the first save.
Disabling Test Mode saves the new visibility state while retaining every position and appearance
value.

The main mod log is:

```
<Beat Saber>\Logs\MultiplayerLeaderboard\MultiplayerLeaderboard.log
```

## Where the data comes from

Nothing is estimated or hard-coded.

* **Players and names**: every entry in the game's multiplayer score sync table
  (`IConnectedPlayer` from each `MultiplayerSyncState`), with `userId`, `userName`, `sortIndex`,
  `isMe`, participant/failed state and connection state.
* **Failed state**: the local player is marked failed only when Beat Saber's own
  `MultiplayerPlayersManager.playerDidFinishEvent` reports the `Failed` result. The mod does not
  infer failure from `IConnectedPlayer.IsFailed()` for the local player, because that method is
  a derived `is_active` state test and can be true briefly while a song is starting at zero score.
  Remote rows use the game's state after a short initial-state grace period.
* **Live score**: for the local player, the live `ScoreController` that feeds Beat Saber's own
  HUD. For every other player, the score from the game's own `MultiplayerScoreProvider`, with
  the provider's per-player `StandardScoreSyncState` as a fallback. This is the same provider
  Beat Saber uses for its multiplayer leaderboard and duel opponent score.
* **Accuracy**: the player's own latest synced multiplied score divided by the maximum multiplied
  score reachable for the notes they have played (`MultipliedScore` /
  `ImmediateMaxPossibleMultipliedScore`). This is Beat Saber's score-percentage definition; the
  multiplayer protocol has no separate cut-accuracy field. If a remote score arrives without its
  accuracy denominator, the accuracy cell shows `-` instead of a fabricated percentage.
* **Ordering**: participants by live score; failed players last, like the game's own board;
  equal scores keep lobby order; anyone who joined after the level started is listed separately
  as spectating with `-` instead of a score.

A value that is not available yet is shown as `-`. At the very start of a song, when both the
live multiplied score and the maximum multiplied score are zero, accuracy is shown as `0.00%`,
which is the same real value Beat Saber's own HUD displays. The mod never substitutes a guessed
score or percentage, and a zero score is never treated as a failure.

## Placement and background

During a real multiplayer song the mod waits for the local player to spawn, then anchors the
panel above the game's own song-progress HUD. If that HUD cannot be found it falls back to the
combo HUD and then to a fixed offset. Test Mode uses a one-time world-space placement about two
metres in front of the current camera, low enough to be visible; it stays world-anchored after
that.

The main panel's dark field is a single UI `Image` using a runtime 1x1 white sprite and the shared
`UINoGlow` game material, not TMP's `<mark>` mesh and not one image per row. It is clipped by a
`RectMask2D`, so the field covers the header, every row, the footer and the score column. The F8
editor uses a TMP `<mark>` field of its own. Layout is data-driven: canvas width, height, columns,
header, rows, footer and font sizes all recompute together when a scale or background slider
changes.

## F8 editor

The original in-world F8 editor is still available as a quick alternative in a multiplayer song.
It uses ordinary Unity UI buttons and the same runtime as the Mods page. The Mods page is the
primary configuration surface; F8 is not required for Test Mode or for saving.

## Status diagnostics

The mod writes its own log at the path above. It records the settings path, the detected session
manager, score source, panel creation position, first real score/accuracy per player, ranking
order (debug, throttled), editor actions and exceptions. Outside a multiplayer song it stays
loaded and idle; that is normal.

## Performance diagnostics

The settings page has **Performance Diagnostics** (on by default). During a multiplayer song it
writes one compact `PERF summary` line every thirty seconds to `MultiplayerLeaderboard.log`.
The line contains:

* average and maximum frame time, plus the number of frames over 50 ms;
* maximum leaderboard update, player-read and UI-render time for the window;
* how many text values, rows and layouts actually changed, how many UI applies ran, and the player count;
* managed heap change and GC generation 0/1/2 collection counts for that window.

`PERF hitch` lines record the panel-side context of individual long frames: at most ten per
thirty-second window, at least half a second apart. Everything else in the log is event-driven,
and the repeated multi-line status block is now a single `STATUS:` line.

One `PERF lifecycle` line is written when the runtime starts, the panel is created or removed,
and the level-end event subscription starts or stops. That makes duplicate panels or duplicate
update loops visible without logging every frame. Turn the toggle off for a comparison run; the
leaderboard still updates normally.

## Testing status of this build

Static: release build **0 warnings / 0 errors**; the compiled-DLL verifier checks target
framework, every assembly reference, the embedded manifest, the BSIPA contract, every game member
used, the BSML dependency, no other plugin reference and no developer paths. The 1.4.0 source
builds clean and its static verifier passes **77 checks, 0 failures** (SHA-256
`c15db6ba243a389efb5f51aec85c8bc52822331168e797e7b89328043e614a68`). Live frame-pacing
verification of the 1.4.0 refresh cadence is still pending.

Live in the authorized instance: 1.2.1 loaded and registered its menus; 1.2.2 loaded and
registered the Mods button and Gameplay Setup tab; 1.2.3 loaded and produced the first in-song
PERF summaries; 1.2.4 was launched cleanly through the main menu and was also run in a live
two-player song from the user's 1.40.3 instance; 1.2.5 ran in a live two-player song and produced
the hitch log that identified the panel pointer path. The 1.3.0 DLL passes the same static checks
(77 checks, 0 failures), and a Test Mode launch rendered all six preview rows with seven canvases
(one main plus six rows) instead of prebuilding ten rows. See `TESTING.md` for the exact checks.

The 1.2.4 in-song run confirmed the local-score fix: **0.037 ms average update**, **0.107 ms
maximum**, **0.011 ms reads**, **0.009 ms rendering**, and no GC. The 1.2.5 run then isolated the
remaining stutter to the panel: 1.2.5's panel-on window had **13 logged hitches of about 26 ms**
and **883 frames / 10.2 s**; its panel-disabled window had **900 frames / 10.2 s**, **11.358 ms
average**, and no repeating 26 ms stall. 1.2.6 removes the always-on pointer/hover work. The
next in-song run is required to confirm that the frame-time pattern is gone; this README does
not treat the code change alone as proof.

The 1.2.6 in-song run confirmed that the old **26 ms every ~355 ms** pointer pattern was gone. It
still showed a **24 ms spike about every 6.04 s** while score and accuracy rows were live, with
the mod's update, read and render calls at **0.02–0.04 ms**. 1.2.7 therefore reduces the Unity
canvas work behind those rows: each row is now a separate canvas, so a score update cannot force
a rebuild of the backdrop, header and every other row. The in-song result still needs your log;
this is a targeted fix, not a claim of measured success.

The first in-song log from 1.2.7 is more useful than a code argument. From 14:42:36 to 14:43:16,
four consecutive ten-second windows with a live score and accuracy row reported **7.095, 7.080,
7.085 and 7.094 ms average frame time**, maximums of **17.258-18.143 ms**, and **zero frames over
50 ms**. Updates averaged 0.025-0.031 ms, reads 0.005-0.006 ms and renders 0.009-0.015 ms, with no
GC. The old 6.04-second, 24 ms pattern was no longer present. That run had only one score-sync
player, so it is evidence rather than a multi-player guarantee; 1.3.0 needs the same test with
two or more players.

**Not yet verified here:** a real two-player multiplayer song (remote score/accuracy, active
zero-score display, ranking, failure/completion transitions, cleanup), the VR controller trigger,
and pointer clicks through every settings control. Those need your live run; the desktop FPFC
pointer and the runtime methods were exercised. The 1.2.4 run did show a remote row advancing,
but the local player was inactive, so the active local-score path still needs a real song test.
The remote-provider path has been exercised against a real second player in the user's instance.

## Compatibility

Built and statically verified against Beat Saber **1.40.3_4614** with the BSIPA and BSML in the
test instance. It is not version-locked by addresses. Quest/console builds are not supported.

## Building from source

Requirements and a fresh-clone walkthrough are in `BUILDING.md`. The short version:

```bash
./build.sh "/path/to/Beat Saber"
# or
BEATSABER_DIR="/path/to/Beat Saber" ./build.sh
```

The script writes the shippable DLL and documentation to `dist/`. You can also build directly:

```bash
dotnet build src/MultiplayerLeaderboard/MultiplayerLeaderboard.csproj -c Release \
  -p:BeatSaberDir="/path/to/Beat Saber"
```

Static verification:

```bash
dotnet run --project tools/Verify -c Release -- \
  --dll src/MultiplayerLeaderboard/bin/Release/MultiplayerLeaderboard.dll \
  --game "<Beat Saber folder>"
```

## Layout

```
src/MultiplayerLeaderboard/
  UI/                          Mods button, BSML page and flow coordinator
  Unity/UiSettings.cs          INI persistence
  Unity/WorldLeaderboardPanel.cs
  Unity/LeaderboardEditor.cs
  Unity/LeaderboardPanelInput.cs
  Gameplay/LeaderboardRuntime.cs
  Diagnostics/                 log, status and frame diagnostics
tools/Verify/                  static DLL verifier
/docs                          testing record and game UI comparison
build.sh                       build + package
BUILDING.md                    prerequisites and fresh-clone commands
```

`build.sh` writes `dist/MultiplayerLeaderboard.dll` plus the documentation. Install only the DLL
into `Plugins`; the other files are documentation.
