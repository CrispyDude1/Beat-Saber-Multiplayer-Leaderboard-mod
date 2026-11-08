# UI architecture: Beat Saber 1.40.3 and Multiplayer Leaderboard

This note records the comparison behind 1.3.0. The game-side findings come from the
decompiled `Main.dll`, `BGNetCore.dll`, `Networking.dll`, `Unity.TextMeshPro.dll` and
`UnityEngine.UI.dll` shipped with the authorized 1.40.3 test installation. No game assembly
was modified.

## What Beat Saber actually does

### 1. One persistent HUD, event-driven text

`CoreGameHUDController` owns a prefab hierarchy and exposes a `CanvasGroup`. The game fades
that group's `alpha`; it does not rebuild or recreate the HUD for ordinary gameplay. Its
children are enabled or disabled once from `CoreGameHUDController.InitData`.

The individual HUD controllers avoid per-frame text churn:

* `ComboUIController` subscribes to `IComboController.comboDidChangeEvent` and writes the
  combo label only when that event fires. It unsubscribes in `OnDisable`.
* `ScoreUIController` subscribes to `IScoreController.scoreDidChangeEvent` and formats the
  score with a reused `StringBuilder`.
* `ImmediateRankUIPanel` updates from
  `relativeScoreOrImmediateRankDidChangeEvent`, and even then writes the rank only when it
  changed and the percentage only when it moved by at least 0.001.
* `MultiplayerPositionHUDController.Update` reads the ranked list every frame, but writes
  the position label only when the local player's rank changed.
* `SongProgressUIController.Update` runs every frame but only touches its minute/second
  labels, slider and fill anchor when the displayed second changes.
* `ResizableTextMeshPro` listens for `TEXT_CHANGED_EVENT`, resizes once, then disables
  itself.

This is the important pattern: a frequently running method is allowed, but the expensive
`TMP_Text.text` assignment and mesh rebuild are gated by a value comparison or an event.

### 2. Native multiplayer score cadence

`MultiplayerScoreProvider` is the game's single score aggregator. It maintains a cached
`List<RankedPlayer>` and `Dictionary<string, RankedPlayer>`, updates them in one `Update`,
sorts the list, and raises `firstPlayerDidChangeEvent` when the leader changes.

The native score-difference displays are deliberately slower than a frame:

* `MultiplayerLocalPlayerScoreDiffTextManager` and
  `MultiplayerOtherPlayersScoreDiffTextManager` use a **0.5 second** timer.
* They write a score-difference text only on that cadence, and the local manager changes
  leader colour only when the boolean leader state changes.

The leaderboard's four-per-second cadence is therefore not excessive compared with the
game's own multiplayer display. The problem was never the number of reads; it was where the
resulting text change invalidated a Unity canvas.

### 3. World-space text uses the 3D TMP renderer where appropriate

The game uses `TextMeshProUGUI` for the screen/HUD labels, but world-space transient text uses
the mesh-based `TextMeshPro` component:

* `MultiplayerScoreDiffText` (`_onPlatformText`)
* `MultiplayerPositionDisplay` (`_text`)
* `MultiplayerCenterTextAnimator` (`_text`)
* `FlyingTextEffect` and `FlyingScoreEffect`

Those objects have their own mesh renderers and do not participate in a UGUI canvas rebuild.
The flying effects are pooled. Tweens are killed in `OnDestroy`; event handlers are removed
in `OnDisable`/`OnDestroy`.

### 4. No layout groups in the gameplay HUD

The decompiled gameplay HUD controllers use serialized prefab `RectTransform`s and direct
text assignments. `LayoutGroup` and `ContentSizeFitter` appear in menu code
(`TabBarViewController`), not in the per-note gameplay HUD. The HUD is not recalculating a
layout tree while the player is cutting notes.

### 5. A platform quirk: runtime Image/RawImage quads are white

A Test Mode experiment in this exact build replaced the panel backdrop with a custom
four-vertex `MaskableGraphic`. It rendered as a white rectangle, matching the existing note in
`LeaderboardEditor` that runtime `Image`/`RawImage` components get a white fallback material.
The final design keeps the TMP `<mark>` backdrop, because TMP's material path is the one that
renders correctly here. This is a deliberate platform-specific exception, not a preference for
text-mesh backgrounds.

## Comparison with the leaderboard

| Concern | Beat Saber | 1.3.0 leaderboard |
|---|---|---|
| Update driver | Events; some HUD `Update` methods gate writes | One runtime `Update`; four-per-second tick |
| Score source | `MultiplayerScoreProvider` and `ScoreController` | Same sources |
| String allocation | Reused `StringBuilder` / cached values | Cached `PlayerRow` strings |
| Layout | Prefab RectTransforms, no gameplay layout groups | Manual anchored `RectTransform` placement |
| Text components | TMP labels reused for the level | TMP labels reused for the song |
| Canvas structure | HUD prefab/CanvasGroup | Static main canvas + one canvas per live row |
| World-space effects | TMP mesh renderers | UGUI world-space panel, required for `VRGraphicRaycaster` editing |
| Visibility | `CanvasGroup.alpha`, `SetActive` | Panel created once per song; rows enabled only when present |
| Object pooling | Gameplay effects pooled | Rows and score buffers reused |
| Cleanup | Unsubscribe in `OnDisable`/`OnDestroy` | Panel/material destroy; level-end unsubscribe; buffers cleared |

### Why one canvas per row

The 1.2.6 log showed the panel's own update, read and render calls at roughly 0.02-0.04 ms,
but the game still produced a ~24 ms frame about every 6.04 seconds while score/accuracy text
was live. A UGUI canvas batches its child graphics; changing a score label dirties the canvas
that owns it. In 1.2.6 that was the main panel canvas, so the backdrop, header, footer and all
rows were part of the same rebuild unit.

1.2.7 gave each live row its own child world-space canvas. The main canvas now holds only the
static backdrop, title and column headers; a score change invalidates that row's canvas, not
the whole panel. The trade-off is one additional draw call per visible row, which is bounded
by the vanilla five-player lobby (the mod caps at ten rows).

### 1.3.0 corrections

Two additional implementation problems were found while checking the TMP API:

1. **Per-label material instances.** The code compared `text.fontMaterial` and then assigned
   `text.fontMaterial`. In TMP, the `fontMaterial` getter calls `CreateMaterialInstance` when
   its cached instance does not match. That gives each label a private material, defeats
   batching, and retains those instances. Both the panel and editor now use
   `fontSharedMaterial` and one owned material.
2. **Unused prebuilt rows.** `WorldLeaderboardPanel.Build` created ten hidden row objects,
   each with a nested canvas and four TMP labels, even for a two-player lobby. Rows are now
   created on first use and reused. A six-row Test Mode preview now reports seven canvases
   (one main plus six rows) and 32 TMP labels; the old design prebuilt ten rows regardless.

The diagnostic summary now includes `ui_rows_changed`, `ui_canvases`, `ui_row_canvases` and
`ui_labels`, so a future log can show both the update granularity and the live UI topology.

## Evidence from the authorized 1.40.3 instance

The 1.2.7 in-song log (14:42-14:43) contains four steady ten-second windows with a single
score-sync row:

```
avg_frame_ms=7.095  max=17.258  frames_over_50ms=0
avg_frame_ms=7.080  max=17.903  frames_over_50ms=0
avg_frame_ms=7.085  max=17.983  frames_over_50ms=0
avg_frame_ms=7.094  max=18.143  frames_over_50ms=0
```

`avg_update_ms` was 0.025-0.031, `avg_read_ms` 0.005-0.006, `avg_render_ms` 0.009-0.015 and
there were no GC collections. The earlier 1.2.6 run in the same installation had a **24 ms
hitch at roughly 6.04 second intervals** with `rows=1..2` and a 10-second average around
11.13 ms. The two early 1.2.7 hitches at 2.52 s and 8.85 s were song-start work; the 269 ms
event at 53.05 s was the scene transition at song end, not a steady-state UI update.

This is evidence from one real song, not proof for every lobby. A multiplayer run with two or
more players, and a comparison run with the panel disabled, is still required.

## Remaining limitations

* No controllable multiplayer lobby or second player was available in the authorized
  installation, so a two-player 1.3.0 run has not been measured.
* VR trigger dragging has not been exercised with a physical headset in this run. Desktop
  `-fpfc` dragging and edit-mode setup were exercised.
* Runtime UGUI `Image`/`RawImage` quads are unusable in this build, so the backdrop remains a
  TMP `<mark>` glyph mesh. It is static outside edit/layout changes.
* The main panel remains UGUI because the game's `VRGraphicRaycaster` and `VRInputModule`
  provide the existing laser-pointer editing path. Replacing it with mesh TMP would require a
  separate collider/raycast implementation and would be a larger compatibility risk than the
  measured row-canvas fix.
