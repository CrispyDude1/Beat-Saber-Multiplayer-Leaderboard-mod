using System;
using System.Collections.Generic;
using System.Globalization;
using MultiplayerLeaderboard.Diagnostics;
using MultiplayerLeaderboard.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRUIControls;

namespace MultiplayerLeaderboard.Unity
{
    /// <summary>
    /// The leaderboard's world-space panel.
    ///
    /// The dark field is a real full-rect Graphic, and every column is laid out from the same scale
    /// values as the background. Text scale therefore grows the panel instead of letting numbers escape
    /// it, and row spacing grows the height without moving the bottom-centre pivot.
    /// </summary>
    internal sealed class WorldLeaderboardPanel
    {
        internal const int MaxRows = 10;

        private const float BaseWidth = 2000f;
        private const float BaseHeaderHeight = 150f;
        private const float BaseRowHeight = 118f;
        private const float BaseFooterHeight = 56f;
        private const float BasePadding = 28f;
        private const float PixelScale = 0.001f;

        private static readonly Color BackgroundColor = new Color(0.012f, 0.016f, 0.026f, 1f);
        private static readonly Color EditBackgroundColor = new Color(0.035f, 0.060f, 0.090f, 1f);
        private static readonly Color HeaderColor = new Color(0.98f, 0.99f, 1.00f, 1f);
        private static readonly Color ColumnColor = new Color(0.62f, 0.70f, 0.84f, 1f);
        private static readonly Color NameColor = new Color(0.95f, 0.97f, 1.00f, 1f);
        private static readonly Color AccuracyColor = new Color(0.72f, 0.88f, 1.00f, 1f);
        private static readonly Color ScoreColor = new Color(0.95f, 0.97f, 1.00f, 1f);
        private static readonly Color ComboColor = new Color(1.00f, 0.86f, 0.55f, 1f);
        private static readonly Color MistakeColor = new Color(1.00f, 0.62f, 0.55f, 1f);
        private static readonly Color LocalColor = new Color(1.00f, 0.84f, 0.35f, 1f);
        private static readonly Color FailedColor = new Color(1.00f, 0.45f, 0.45f, 1f);
        private static readonly Color RankLossColor = new Color(1.00f, 0.22f, 0.22f, 1f);
        private static readonly Color GoldColor = new Color(1.00f, 0.82f, 0.30f, 1f);
        private static readonly Color SilverColor = new Color(0.82f, 0.86f, 0.95f, 1f);
        private static readonly Color BronzeColor = new Color(0.85f, 0.58f, 0.35f, 1f);
        private static readonly Color OtherRankColor = new Color(0.66f, 0.73f, 0.86f, 1f);
        private static readonly Color PlaceholderColor = new Color(0.70f, 0.76f, 0.86f, 1f);
        private static readonly Color EditHintColor = new Color(1.00f, 0.82f, 0.30f, 1f);

        private sealed class Row
        {
            public GameObject Root;
            public Canvas Canvas;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Accuracy;
            public TextMeshProUGUI Score;
            public TextMeshProUGUI Combo;
            public TextMeshProUGUI Mistakes;
        }

        private struct PendingText
        {
            public TextMeshProUGUI Label;
            public string Value;

            public PendingText(TextMeshProUGUI label, string value)
            {
                Label = label;
                Value = value;
            }
        }

        private struct PendingColor
        {
            public TextMeshProUGUI Label;
            public Color Value;

            public PendingColor(TextMeshProUGUI label, Color value)
            {
                Label = label;
                Value = value;
            }
        }

        private GameObject _root;
        private bool _platformAttached;
        private RectTransform _rect;
        private Canvas _canvas;
        private Image _backdrop;
        private RectTransform _backdropClip;
        private Texture2D _backdropTexture;
        private Sprite _backdropSprite;
        private static Material _backdropMaterial;
        private static bool _backdropMaterialResolved;
        private Row[] _rows;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _columnPlayer;
        private TextMeshProUGUI _columnAccuracy;
        private TextMeshProUGUI _columnScore;
        private TextMeshProUGUI _columnCombo;
        private TextMeshProUGUI _columnMistakes;
        private TextMeshProUGUI _placeholder;
        private TextMeshProUGUI _footer;
        private TextMeshProUGUI _editHint;
        private LeaderboardEditor _editor;
        private readonly List<TextMeshProUGUI> _texts = new List<TextMeshProUGUI>();
        private readonly List<PendingText> _pendingText = new List<PendingText>(64);
        private readonly List<PendingColor> _pendingColor = new List<PendingColor>(64);
        private int _createdRows;
        private int _rowCanvasCount;
        private bool _topologyLogged;
        private float _width;
        private float _height;
        private int _visibleCount = 1;
        private float _overallScale = 1f;
        private float _textScale = 1f;
        private float _nameScale = 1f;
        private float _accuracyScale = 1f;
        private float _scoreScale = 1f;
        private float _rowSpacing = 1f;
        private float _backgroundWidthScale = 1f;
        private float _backgroundHeightScale = 1f;
        private float _backgroundOpacity = 1f;
        private float _textBrightness = 1f;
        private bool _bloom;
        private float _bloomIntensity = UiSettings.DefaultBloomIntensity;
        private float _bloomMaterialIntensity = -1f;
        private static Shader _bloomShader;
        private static bool _bloomShaderResolved;
        private Material _bloomMaterial;
        private string _bloomMode = "off";
        private bool _showScore = true;
        private bool _showAccuracy = true;
        private bool _showCombo = true;
        private bool _showMistakes = true;
        private bool _enabled = true;
        private bool _testMode;
        private bool _performanceLogging;
        private bool _preview;
        private bool _editMode;
        private bool _dragEnabled;
        private bool _editorRequested;
        private bool _rotationEditing;
        private bool _fontsReady;
        private VRGraphicRaycaster _raycaster;
        private LeaderboardPanelInput _input;
        private bool _interactionEnabled;
        private string _creationPhases = string.Empty;
        private string _footerCache;
        private int _footerCount = -1;
        private int _footerSpectators = -1;
        private int _footerShown = -1;
        private Vector3 _defaultPosition;
        private Quaternion _defaultRotation = Quaternion.identity;
        private const float RankFlashSeconds = 1.5f;
        private readonly Dictionary<string, int> _lastRanks = new Dictionary<string, int>(12);
        private readonly Dictionary<string, float> _rankFlashUntil = new Dictionary<string, float>(12);
        private readonly HashSet<string> _seenRankKeys = new HashSet<string>();
        private readonly List<string> _rankKeys = new List<string>(12);

        internal bool IsAlive => _root != null;
        internal bool EditMode => _editMode;
        internal bool DragEnabled => _dragEnabled;
        internal bool Preview => _preview;
        internal float OverallScale => _overallScale;
        internal float TextScale => _textScale;
        internal float NameScale => _nameScale;
        internal float AccuracyScale => _accuracyScale;
        internal float ScoreScale => _scoreScale;
        internal float RowSpacing => _rowSpacing;
        internal float BackgroundWidthScale => _backgroundWidthScale;
        internal float BackgroundHeightScale => _backgroundHeightScale;
        internal float BackgroundOpacity => _backgroundOpacity;
        internal float TextBrightness => _textBrightness;
        internal bool Bloom => _bloom;
        internal float BloomIntensity => _bloomIntensity;
        private bool BloomActive => _bloom && _bloomIntensity > 0.001f;
        internal bool ShowScore => _showScore;
        internal bool ShowAccuracy => _showAccuracy;
        internal bool ShowCombo => _showCombo;
        internal bool ShowMistakes => _showMistakes;
        internal bool RotationEditing => _rotationEditing;
        internal Vector3 WorldPosition => _root != null ? _root.transform.position : Vector3.zero;
        internal Quaternion WorldRotation => _root != null ? _root.transform.rotation : Quaternion.identity;
        internal Vector3 RotationEuler => NormalizeEuler(WorldRotation.eulerAngles);
        internal float WorldWidth => _rect != null && _root != null ? _rect.rect.width * _root.transform.lossyScale.x : 0f;
        internal float WorldHeight => _rect != null && _root != null ? _rect.rect.height * _root.transform.lossyScale.y : 0f;
        internal Canvas Canvas => _canvas;
        internal int CanvasCount => _canvas == null ? 0 : 1 + _rowCanvasCount;
        internal int RowCanvasCount => _rowCanvasCount;
        internal int LabelCount => _texts.Count;
        internal string CreationPhases => _creationPhases;

        internal void RefreshStaticLabels()
        {
            ForceStaticTextUpdate();
        }

        internal bool IsPointerTarget(GameObject target)
        {
            return target != null && _backdrop != null && target == _backdrop.gameObject;
        }

        internal bool TryClickEditor(GameObject target)
        {
            return _editor != null && _editor.TryClick(target);
        }

        internal static WorldLeaderboardPanel Create(Vector3 position, Quaternion rotation, UiSettings settings, bool preview)
        {
            long totalStarted = PerformanceDiagnostics.Timestamp();
            WorldLeaderboardPanel panel = new WorldLeaderboardPanel();
            panel._defaultPosition = position;
            panel._defaultRotation = rotation;
            panel._preview = preview;

            if (settings != null)
            {
                panel._overallScale = Mathf.Clamp(settings.OverallScale, UiSettings.MinOverallScale,
                                                  UiSettings.MaxOverallScale);
                panel._textScale = Mathf.Clamp(settings.TextScale, UiSettings.MinTextScale, UiSettings.MaxTextScale);
                panel._nameScale = Mathf.Clamp(settings.NameScale, UiSettings.MinColumnScale,
                                                UiSettings.MaxColumnScale);
                panel._accuracyScale = Mathf.Clamp(settings.AccuracyScale, UiSettings.MinColumnScale,
                                                   UiSettings.MaxColumnScale);
                panel._scoreScale = Mathf.Clamp(settings.ScoreScale, UiSettings.MinColumnScale,
                                                UiSettings.MaxColumnScale);
                panel._rowSpacing = Mathf.Clamp(settings.RowSpacing, UiSettings.MinRowSpacing,
                                                UiSettings.MaxRowSpacing);
                panel._backgroundWidthScale = Mathf.Clamp(settings.BackgroundWidthScale,
                                                           UiSettings.MinBackgroundScale,
                                                           UiSettings.MaxBackgroundScale);
                panel._backgroundHeightScale = Mathf.Clamp(settings.BackgroundHeightScale,
                                                            UiSettings.MinBackgroundScale,
                                                            UiSettings.MaxBackgroundScale);
                panel._backgroundOpacity = Mathf.Clamp(settings.BackgroundOpacity,
                                                        UiSettings.MinBackgroundOpacity,
                                                        UiSettings.MaxBackgroundOpacity);
                panel._textBrightness = Mathf.Clamp(settings.TextBrightness,
                                                    UiSettings.MinTextBrightness,
                                                    UiSettings.MaxTextBrightness);
                panel._bloom = settings.Bloom;
                panel._bloomIntensity = Mathf.Clamp(settings.BloomIntensity,
                                                     UiSettings.MinBloomIntensity,
                                                     UiSettings.MaxBloomIntensity);
                panel._showScore = settings.ShowScore;
                panel._showAccuracy = settings.ShowAccuracy;
                panel._showCombo = settings.ShowCombo;
                panel._showMistakes = settings.ShowMistakes;
                panel._enabled = settings.Enabled;
                panel._testMode = settings.TestMode;
                panel._performanceLogging = settings.PerformanceLogging;
            }

            panel.Build();

            Vector3 restorePosition = settings != null && settings.HasPosition ? settings.Position : position;
            Quaternion restoreRotation = settings != null && settings.HasPosition ? settings.Rotation : rotation;
            panel._root.transform.SetPositionAndRotation(restorePosition, restoreRotation);
            panel._root.transform.localScale = Vector3.one * (PixelScale * panel._overallScale);

            LeaderboardLog.Info("PERF panel creation phases: " + panel._creationPhases + ", total=" +
                                PerformanceDiagnostics.ElapsedMs(totalStarted).ToString("0.000", CultureInfo.InvariantCulture) + " ms.");
            LeaderboardLog.Info("Leaderboard panel built: " + panel.WorldWidth.ToString("0.00", CultureInfo.InvariantCulture) +
                                " x " + panel.WorldHeight.ToString("0.00", CultureInfo.InvariantCulture) +
                                " m, canvas " + panel.CurrentPixelWidth.ToString("0", CultureInfo.InvariantCulture) +
                                " x " + panel.CurrentPixelHeight.ToString("0", CultureInfo.InvariantCulture) +
                                " px, overall scale " + F(panel._overallScale) + ", text scale " + F(panel._textScale) +
                                ", row spacing " + F(panel._rowSpacing) + ".");
            return panel;
        }

        private float CurrentPixelWidth => _width > 0f ? _width : ComputeWidth();
        private float CurrentPixelHeight => _height;

        private float ColumnScale
        {
            get
            {
                float scale = Mathf.Max(1f, _nameScale);
                if (_showAccuracy)
                {
                    scale = Mathf.Max(scale, _accuracyScale);
                }

                if (_showScore)
                {
                    scale = Mathf.Max(scale, _scoreScale);
                }

                return scale;
            }
        }

        private float ComputeWidth()
        {
            float s = _textScale;
            float content = 104f * s + 760f * s * _nameScale + 28f * s;
            if (_showAccuracy)
            {
                content += 16f * s + 300f * s * _accuracyScale;
            }

            if (_showScore)
            {
                content += 16f * s + 372f * s * _scoreScale;
            }

            if (_showCombo)
            {
                content += 16f * s + 170f * s;
            }

            if (_showMistakes)
            {
                content += 16f * s + 180f * s;
            }

            return Mathf.Max(BaseWidth * s, content) * _backgroundWidthScale;
        }

        private float ComputeHeight(int rows)
        {
            float rowScale = _textScale * _rowSpacing * ColumnScale;
            return (BaseHeaderHeight * _textScale +
                    Mathf.Clamp(rows, 1, MaxRows) * BaseRowHeight * rowScale +
                    BaseFooterHeight * _textScale) * _backgroundHeightScale;
        }

        private void Build()
        {
            long buildStarted = PerformanceDiagnostics.Timestamp();
            _root = new GameObject("MultiplayerLeaderboard.Panel", typeof(RectTransform), typeof(Canvas));
            _root.layer = 5;
            _root.transform.localScale = Vector3.one * (PixelScale * _overallScale);

            _rect = (RectTransform)_root.transform;
            _rect.pivot = new Vector2(0.5f, 0f);
            _width = ComputeWidth();
            _height = ComputeHeight(_visibleCount);
            _rect.sizeDelta = new Vector2(_width, _height);

            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32760;

            // The in-game panel is display-only. Its VR raycaster and pointer handlers are added only
            // by EnterEditMode/EnterTestEditing; keeping them active while playing made the game's
            // VRInputModule raycast the panel every frame and rebuilt this TMP backdrop on hover.
            GameObject clipObject = new GameObject("BackdropClip", typeof(RectTransform));
            clipObject.layer = 5;
            RectTransform clipRect = (RectTransform)clipObject.transform;
            clipRect.SetParent(_root.transform, false);
            Place(clipRect, 0f, 0f, _width, _height);
            _backdropClip = clipRect;
            _backdrop = CreateBackdrop(clipRect, _width, _height);
            UpdateBackdropColor();

            // Rows are created the first time they are needed and then reused for the whole song.
            // Pre-building ten hidden row canvases put 40 TMP labels and ten CanvasRenderers in the
            // scene even for a two-player lobby, which is unnecessary startup and batching work.
            _rows = new Row[MaxRows];

            _title = CreateText(_root.transform, "Title", 0f, 0f, 1160f, 76f, 72f,
                                TextAlignmentOptions.Left, HeaderColor, FontStyles.Bold);
            _title.text = _preview ? "MULTIPLAYER LEADERBOARD  |  PREVIEW DATA" : "MULTIPLAYER LEADERBOARD";
            _title.color = _preview ? EditHintColor : HeaderColor;

            _editHint = CreateText(_root.transform, "EditHint", 0f, 0f, 592f, 76f, 32f,
                                    TextAlignmentOptions.Right, EditHintColor, FontStyles.Bold);
            _editHint.text = string.Empty;

            _columnPlayer = CreateText(_root.transform, "ColumnPlayer", 0f, 0f, 760f, 42f, 40f,
                                       TextAlignmentOptions.Left, ColumnColor, FontStyles.Bold);
            _columnPlayer.text = "PLAYER";

            _columnAccuracy = CreateText(_root.transform, "ColumnAccuracy", 0f, 0f, 300f, 42f, 40f,
                                         TextAlignmentOptions.Right, ColumnColor, FontStyles.Bold);
            _columnAccuracy.text = "ACCURACY";

            _columnScore = CreateText(_root.transform, "ColumnScore", 0f, 0f, 372f, 42f, 40f,
                                      TextAlignmentOptions.Right, ColumnColor, FontStyles.Bold);
            _columnScore.text = "SCORE";

            _columnCombo = CreateText(_root.transform, "ColumnCombo", 0f, 0f, 170f, 42f, 40f,
                                      TextAlignmentOptions.Right, ColumnColor, FontStyles.Bold);
            _columnCombo.text = "COMBO";

            _columnMistakes = CreateText(_root.transform, "ColumnMistakes", 0f, 0f, 180f, 42f, 40f,
                                        TextAlignmentOptions.Right, ColumnColor, FontStyles.Bold);
            _columnMistakes.text = "MISTAKES";

            _placeholder = CreateText(_root.transform, "Placeholder", 0f, 0f, 0f, 0f, 52f,
                                      TextAlignmentOptions.Center, PlaceholderColor, FontStyles.Normal);
            _placeholder.text = "Waiting for multiplayer score data...";

            _footer = CreateText(_root.transform, "Footer", 0f, 0f, 0f, 0f, 40f,
                                 TextAlignmentOptions.Left, ColumnColor, FontStyles.Normal);

            // These fixed labels must never be truncated by their own box. TMP's Ellipsis mode
            // reports zero visible characters when the rect is shorter than one line at the current
            // font size, which was why the title and column headers were blank. Rows keep Ellipsis so
            // a long player name cannot run into the accuracy column.
            _title.overflowMode = TextOverflowModes.Overflow;
            _editHint.overflowMode = TextOverflowModes.Overflow;
            _columnPlayer.overflowMode = TextOverflowModes.Overflow;
            _columnAccuracy.overflowMode = TextOverflowModes.Overflow;
            _columnScore.overflowMode = TextOverflowModes.Overflow;
            _columnCombo.overflowMode = TextOverflowModes.Overflow;
            _columnMistakes.overflowMode = TextOverflowModes.Overflow;
            _placeholder.overflowMode = TextOverflowModes.Overflow;
            _footer.overflowMode = TextOverflowModes.Overflow;

            // Beat Saber's VRInputModule dispatches pointer-down to the graphic that was hit rather than
            // walking up to its parent, so the input component lives on the backdrop itself.
            _input = _backdrop.gameObject.AddComponent<LeaderboardPanelInput>();
            _input.Initialize(this);
            _input.enabled = false;
            EnsureFonts();

            long layoutStarted = PerformanceDiagnostics.Timestamp();
            ApplyLayout();
            double layoutMs = PerformanceDiagnostics.ElapsedMs(layoutStarted);

            long staticStarted = PerformanceDiagnostics.Timestamp();

            // The title, column headers and placeholder receive their text before the first layout,
            // while their temporary rect is shorter than one TMP line. With Ellipsis overflow TMP
            // caches an empty visible-character range at that point and a later rect resize does not
            // bring the glyphs back. Regenerate the static labels once now that the final rect and
            // font size are in place (the rows already set their text after layout, which is why they
            // rendered correctly).
            ForceStaticTextUpdate();
            double staticMs = PerformanceDiagnostics.ElapsedMs(staticStarted);
            _creationPhases = "objects=" + PerformanceDiagnostics.ElapsedMs(buildStarted).ToString("0.000", CultureInfo.InvariantCulture) +
                              ", layout=" + layoutMs.ToString("0.000", CultureInfo.InvariantCulture) +
                              ", static=" + staticMs.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void ForceStaticTextUpdate()
        {
            RefreshStaticLabel(_title, _preview ? "MULTIPLAYER LEADERBOARD  |  PREVIEW DATA" : "MULTIPLAYER LEADERBOARD");
            RefreshStaticLabel(_columnPlayer, "PLAYER");
            RefreshStaticLabel(_columnAccuracy, "ACCURACY");
            RefreshStaticLabel(_columnScore, "SCORE");
            RefreshStaticLabel(_columnCombo, "COMBO");
            RefreshStaticLabel(_columnMistakes, "MISTAKES");
            // The placeholder is set before its rect exists; force it once after layout so it is not
            // left with an empty cached character range when the first song has no score data yet.
            RefreshStaticLabel(_placeholder, "Waiting for multiplayer score data...");

            LeaderboardLog.Debug("Static labels laid out: title " + CharacterCount(_title) + " char(s) ('" +
                                 TextOf(_title) + "', font " + FontName(_title) + ", rect " +
                                 SizeOf(_title) + ", line " + LineRatio(_title) + "), columns " +
                                 CharacterCount(_columnPlayer) + "/" + CharacterCount(_columnAccuracy) + "/" +
                                 CharacterCount(_columnScore) + ", placeholder " + CharacterCount(_placeholder) + ".");
        }

        /// <summary>
        /// Re-assigns a static string and forces a full TMP reparse. Assigning the same string is a
        /// no-op in TMP, which preserves the empty visible-character range produced before the first
        /// layout, so the value is cleared and set again.
        /// </summary>
        private static void RefreshStaticLabel(TextMeshProUGUI text, string value)
        {
            if (text == null)
            {
                return;
            }

            if (!string.Equals(text.text, value, StringComparison.Ordinal))
            {
                text.text = value;
            }
            else
            {
                text.text = string.Empty;
                text.text = value;
            }

            text.ForceMeshUpdate(true, true);
        }

        private static void ForceMeshUpdate(TextMeshProUGUI text)
        {
            if (text != null)
            {
                text.ForceMeshUpdate(true, true);
            }
        }

        private static string TextOf(TextMeshProUGUI text)
        {
            return text != null ? text.text : "null";
        }

        private static string FontName(TextMeshProUGUI text)
        {
            return text != null && text.font != null ? text.font.name : "null";
        }

        private static string LineRatio(TextMeshProUGUI text)
        {
            if (text == null || text.font == null || text.font.faceInfo.pointSize <= 0f)
            {
                return "?";
            }

            return (text.font.faceInfo.lineHeight / text.font.faceInfo.pointSize)
                .ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string SizeOf(TextMeshProUGUI text)
        {
            if (text == null)
            {
                return "null";
            }

            Rect rect = text.rectTransform.rect;
            return rect.width.ToString("0", CultureInfo.InvariantCulture) + "x" +
                   rect.height.ToString("0", CultureInfo.InvariantCulture);
        }

        private static int CharacterCount(TextMeshProUGUI text)
        {
            return text != null && text.textInfo != null ? text.textInfo.characterCount : 0;
        }

        private Row CreateRow(int index)
        {
            Row row = new Row();

            // Each row owns a small canvas, so a live score or accuracy update rebuilds only that
            // row's six labels instead of every label on the panel. The row transform, scale and
            // text placement are unchanged; only the canvas boundary is different.
            GameObject rowObject = new GameObject("Row" + index, typeof(RectTransform), typeof(Canvas));
            rowObject.layer = 5;
            RectTransform rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(_root.transform, false);

            Canvas rowCanvas = rowObject.GetComponent<Canvas>();
            rowCanvas.renderMode = RenderMode.WorldSpace;
            rowCanvas.overrideSorting = true;
            rowCanvas.sortingOrder = 32761;
            rowCanvas.worldCamera = _canvas != null ? _canvas.worldCamera : null;
            row.Canvas = rowCanvas;
            _rowCanvasCount++;

            row.Rank = CreateText(rowRect, "Rank", 0f, 0f, 0f, 0f, 64f,
                                  TextAlignmentOptions.Center, OtherRankColor, FontStyles.Bold);
            row.Name = CreateText(rowRect, "Name", 0f, 0f, 0f, 0f, 72f,
                                  TextAlignmentOptions.Left, NameColor, FontStyles.Normal);
            row.Accuracy = CreateText(rowRect, "Accuracy", 0f, 0f, 0f, 0f, 68f,
                                      TextAlignmentOptions.Right, AccuracyColor, FontStyles.Normal);
            row.Score = CreateText(rowRect, "Score", 0f, 0f, 0f, 0f, 72f,
                                   TextAlignmentOptions.Right, ScoreColor, FontStyles.Normal);
            row.Combo = CreateText(rowRect, "Combo", 0f, 0f, 0f, 0f, 68f,
                                   TextAlignmentOptions.Right, ComboColor, FontStyles.Normal);
            row.Mistakes = CreateText(rowRect, "Mistakes", 0f, 0f, 0f, 0f, 68f,
                                     TextAlignmentOptions.Right, ColumnColor, FontStyles.Normal);

            // Only the player name needs ellipsis. TMP's ellipsis path measures and truncates text
            // on every change; ranks and scores are bounded by their columns, so Overflow avoids
            // that extra pass without changing what is displayed.
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            row.Rank.overflowMode = TextOverflowModes.Overflow;
            row.Accuracy.overflowMode = TextOverflowModes.Overflow;
            row.Score.overflowMode = TextOverflowModes.Overflow;
            row.Combo.overflowMode = TextOverflowModes.Overflow;
            row.Mistakes.overflowMode = TextOverflowModes.Overflow;

            row.Root = rowObject;
            return row;
        }

        internal void SetCamera(Camera camera)
        {
            if (_canvas == null || camera == null)
            {
                return;
            }

            if (_canvas.worldCamera != camera)
            {
                _canvas.worldCamera = camera;
            }

            if (_rows == null)
            {
                return;
            }

            for (int i = 0; i < _rows.Length; i++)
            {
                Row row = _rows[i];
                if (row != null && row.Canvas != null && row.Canvas.worldCamera != camera)
                {
                    row.Canvas.worldCamera = camera;
                }
            }
        }

        /// <summary>
        /// Re-parents this panel to a moving world-space anchor while keeping its current world
        /// transform. The spectator view uses the selected player's platform as that anchor; the
        /// normal in-lobby panel never calls this method.
        /// </summary>
        internal void AttachTo(Transform anchor)
        {
            if (_root == null || anchor == null)
            {
                return;
            }

            _root.transform.SetParent(anchor, true);
            _platformAttached = true;

            // Only the scale compensation depends on the new parent. Re-running the full layout here
            // touched every label's font size and rect, which forced a complete canvas rebuild of the
            // whole panel on every spectator switch.
            ApplyRootScale();
        }

        /// <summary>
        /// Returns the panel to world space after spectator mode ends. Its rotation is not changed;
        /// the caller restores the exact world pose captured before it was attached.
        /// </summary>
        internal void DetachFromAnchor()
        {
            if (_root == null)
            {
                return;
            }

            _root.transform.SetParent(null, true);
            _platformAttached = false;
            ApplyRootScale();
        }

        /// <summary>
        /// True while a rank-change flash still needs a color update. This lets the runtime skip
        /// repeated Render calls when neither row data nor the footer changed, without freezing the
        /// short rank-flash animation.
        /// </summary>
        internal bool HasActiveRankFlash(float now)
        {
            foreach (float until in _rankFlashUntil.Values)
            {
                if (now < until)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Render creates at most one row per call to keep a large lobby from producing one huge
        /// frame. The runtime must keep calling Render while this is false even when the data hash
        /// has not changed, otherwise the remaining rows would never be created.
        /// </summary>
        internal bool HasAllRows(int count)
        {
            if (_rows == null || count <= 0)
            {
                return true;
            }

            int visible = Mathf.Min(count, MaxRows);
            for (int i = 0; i < visible; i++)
            {
                if (_rows[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        internal void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            if (_root == null)
            {
                return;
            }

            _root.transform.SetPositionAndRotation(position, rotation);
        }

        internal void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
            {
                _root.SetActive(visible);
            }
        }

        internal void EnsureFonts()
        {
            // Font resolution is only needed until every label has a usable TMP font. After that the
            // labels are stable; re-checking all 46 labels on every four-per-second row refresh was
            // unnecessary native work.
            if (_fontsReady)
            {
                return;
            }

            bool allReady = true;
            for (int i = 0; i < _texts.Count; i++)
            {
                TextMeshProUGUI text = _texts[i];
                if (!LeaderboardFont.Apply(text))
                {
                    allReady = false;
                }
            }

            _fontsReady = allReady;
            if (allReady && _bloom)
            {
                ApplyTextMaterials();
            }
        }

        internal void Render(IReadOnlyList<PlayerRow> rows, string footerOverride = null)
        {
            EnsureFonts();

            int count = rows == null ? 0 : Math.Min(rows.Count, MaxRows);
            float now = Time.unscaledTime;
            TrackRankChanges(rows, count, now);
            bool rowsCreated = EnsureRows(count);
            bool layoutChanged = SetVisibleCount(count);
            if (rowsCreated && !layoutChanged)
            {
                ApplyLayout();
            }

            int textChanges = 0;
            int changedRows = 0;

            if (_placeholder.gameObject.activeSelf != (count == 0))
            {
                _placeholder.gameObject.SetActive(count == 0);
            }

            for (int i = 0; i < MaxRows; i++)
            {
                Row row = _rows[i];
                bool visible = i < count;
                if (row == null)
                {
                    // Rows are created one per refresh by EnsureRows. The remaining visible rows
                    // fill in on the next few ticks instead of constructing every TMP label in one
                    // frame when a large lobby first appears.
                    continue;
                }

                if (row.Root.activeSelf != visible)
                {
                    row.Root.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                PlayerRow data = rows[i];
                bool rowChanged = false;
                if (Set(row.Rank, data.RankText))
                {
                    textChanges++;
                    rowChanged = true;
                }

                if (Set(row.Name, data.DisplayName))
                {
                    textChanges++;
                    rowChanged = true;
                }

                if (Set(row.Accuracy, data.AccuracyText))
                {
                    textChanges++;
                    rowChanged = true;
                }

                if (Set(row.Score, data.ScoreText))
                {
                    textChanges++;
                    rowChanged = true;
                }

                if (Set(row.Combo, data.ComboText))
                {
                    textChanges++;
                    rowChanged = true;
                }

                if (Set(row.Mistakes, data.MistakesText))
                {
                    textChanges++;
                    rowChanged = true;
                }

                rowChanged |= SetColor(row.Rank, Tint(RankColor(data.Rank)));
                rowChanged |= SetColor(row.Name, NameColorFor(data, now));
                rowChanged |= SetColor(row.Accuracy, Tint(data.IsFailed ? FailedColor : AccuracyColor));
                rowChanged |= SetColor(row.Score, Tint(data.IsFailed
                    ? FailedColor
                    : (data.IsMe ? LocalColor : ScoreColor)));
                rowChanged |= SetColor(row.Combo, Tint(data.IsFailed ? FailedColor : ComboColor));
                rowChanged |= SetColor(row.Mistakes, Tint(data.Mistakes > 0 ? MistakeColor : ColumnColor));
                if (rowChanged)
                {
                    changedRows++;
                }
            }

            if (Set(_footer, footerOverride ?? (_preview
                ? "PREVIEW DATA - not a live multiplayer session"
                : BuildFooter(rows, count))))
            {
                textChanges++;
            }

            LogTopologyOnce();
            PerformanceDiagnostics.RecordUiChanges(textChanges, layoutChanged, changedRows);
        }

        internal void SetPerformanceLogging(bool enabled)
        {
            _performanceLogging = enabled;
        }

        internal void SetPreview(bool preview)
        {
            _preview = preview;
            if (_title != null)
            {
                _title.text = preview
                    ? "MULTIPLAYER LEADERBOARD  |  PREVIEW DATA"
                    : "MULTIPLAYER LEADERBOARD";
                _title.color = Tint(preview ? EditHintColor : HeaderColor);
            }
        }

        private bool SetVisibleCount(int count)
        {
            int effective = Mathf.Clamp(count, 1, MaxRows);
            if (effective == _visibleCount)
            {
                return false;
            }

            _visibleCount = effective;
            _height = ComputeHeight(effective);
            ApplyLayout();
            return true;
        }

        /// <summary>
        /// Applies only the root scale, including the compensation for a spectator platform's 0.02
        /// transform scale. Keeping this separate from ApplyLayout means attaching and detaching the
        /// panel does not touch any label's font size or rect, and therefore does not dirty every
        /// canvas on the panel.
        /// </summary>
        private void ApplyRootScale()
        {
            float desiredScale = PixelScale * _overallScale;
            if (_platformAttached && _root.transform.parent != null)
            {
                Vector3 parentScale = _root.transform.parent.lossyScale;
                _root.transform.localScale = new Vector3(
                    Mathf.Abs(parentScale.x) > 0.0001f ? desiredScale / parentScale.x : desiredScale,
                    Mathf.Abs(parentScale.y) > 0.0001f ? desiredScale / parentScale.y : desiredScale,
                    Mathf.Abs(parentScale.z) > 0.0001f ? desiredScale / parentScale.z : desiredScale);
            }
            else
            {
                _root.transform.localScale = Vector3.one * desiredScale;
            }
        }

        private void ApplyLayout()
        {
            float s = _textScale;
            float rowScale = s * _rowSpacing * ColumnScale;
            float width = ComputeWidth();
            _width = width;
            _height = ComputeHeight(_visibleCount);

            float nameX = 104f * s;
            float nameWidth = 760f * s * _nameScale;
            float cursor = nameX + nameWidth;
            float accuracyX = 0f;
            float accuracyWidth = 300f * s * _accuracyScale;
            if (_showAccuracy)
            {
                cursor += 16f * s;
                accuracyX = cursor;
                cursor += accuracyWidth;
            }

            float scoreX = 0f;
            float scoreWidth = 372f * s * _scoreScale;
            if (_showScore)
            {
                cursor += 16f * s;
                scoreX = cursor;
                cursor += scoreWidth;
            }

            float comboX = 0f;
            float comboWidth = 170f * s;
            if (_showCombo)
            {
                cursor += 16f * s;
                comboX = cursor;
                cursor += comboWidth;
            }

            float mistakesX = 0f;
            float mistakesWidth = 180f * s;
            if (_showMistakes)
            {
                cursor += 16f * s;
                mistakesX = cursor;
            }

            _rect.sizeDelta = new Vector2(width, _height);

            ApplyRootScale();

            if (_backdropClip != null)
            {
                Place(_backdropClip, 0f, 0f, width, _height);
            }

            if (_backdrop != null)
            {
                Place((RectTransform)_backdrop.transform, 0f, 0f, width, _height);
            }

            // Teko's line height is 1.433x its point size, so the header sizes below are chosen to
            // fit one full line inside the 150 px header without touching the first row.
            SetFont(_title, 64f * s);
            Place(_title.rectTransform, BasePadding * s, 12f * s, 900f * s, 80f * s);

            SetFont(_editHint, 30f * s);
            Place(_editHint.rectTransform, width - 620f * s, 12f * s, 592f * s, 80f * s);
            _editHint.gameObject.SetActive(_editMode);

            SetFont(_columnPlayer, 34f * s);
            Place(_columnPlayer.rectTransform, nameX, 104f * s, nameWidth, 42f * s);
            SetColumnVisible(_columnPlayer, true);

            SetFont(_columnAccuracy, 34f * s);
            Place(_columnAccuracy.rectTransform, accuracyX, 104f * s, accuracyWidth, 42f * s);
            SetColumnVisible(_columnAccuracy, _showAccuracy);

            SetFont(_columnScore, 34f * s);
            Place(_columnScore.rectTransform, scoreX, 104f * s, scoreWidth, 42f * s);
            SetColumnVisible(_columnScore, _showScore);

            SetFont(_columnCombo, 34f * s);
            Place(_columnCombo.rectTransform, comboX, 104f * s, comboWidth, 42f * s);
            SetColumnVisible(_columnCombo, _showCombo);

            SetFont(_columnMistakes, 34f * s);
            Place(_columnMistakes.rectTransform, mistakesX, 104f * s, mistakesWidth, 42f * s);
            SetColumnVisible(_columnMistakes, _showMistakes);

            for (int i = 0; i < MaxRows; i++)
            {
                Row row = _rows[i];
                if (row == null)
                {
                    continue;
                }

                float rowY = BaseHeaderHeight * s + i * BaseRowHeight * rowScale;
                Place((RectTransform)row.Root.transform, 0f, rowY, width, BaseRowHeight * rowScale);

                float inset = 4f * rowScale;
                float textHeight = BaseRowHeight * rowScale - inset * 2f;

                SetFont(row.Rank, 64f * s);
                Place(row.Rank.rectTransform, 20f * s, inset, 80f * s, textHeight);

                SetFont(row.Name, 72f * s * _nameScale);
                Place(row.Name.rectTransform, nameX, inset, nameWidth, textHeight);
                SetColumnVisible(row.Name, true);

                SetFont(row.Accuracy, 68f * s * _accuracyScale);
                Place(row.Accuracy.rectTransform, accuracyX, inset, accuracyWidth, textHeight);
                SetColumnVisible(row.Accuracy, _showAccuracy);

                SetFont(row.Score, 72f * s * _scoreScale);
                Place(row.Score.rectTransform, scoreX, inset, scoreWidth, textHeight);
                SetColumnVisible(row.Score, _showScore);

                SetFont(row.Combo, 68f * s);
                Place(row.Combo.rectTransform, comboX, inset, comboWidth, textHeight);
                SetColumnVisible(row.Combo, _showCombo);

                SetFont(row.Mistakes, 68f * s);
                Place(row.Mistakes.rectTransform, mistakesX, inset, mistakesWidth, textHeight);
                SetColumnVisible(row.Mistakes, _showMistakes);
            }

            SetFont(_placeholder, 52f * s);
            Place(_placeholder.rectTransform, BasePadding * s, BaseHeaderHeight * s,
                  width - BasePadding * 2f * s, BaseRowHeight * rowScale);

            float footerY = BaseHeaderHeight * s + _visibleCount * BaseRowHeight * rowScale + 4f * s;
            SetFont(_footer, 40f * s);
            Place(_footer.rectTransform, BasePadding * s, footerY,
                  width - BasePadding * 2f * s, BaseFooterHeight * s - 8f * s);
            ApplyStaticColors();
            UpdateBackdropColor();
        }

        private void SetColumnVisible(TextMeshProUGUI label, bool visible)
        {
            if (label == null)
            {
                return;
            }

            if (label.gameObject.activeSelf == visible)
            {
                return;
            }

            label.gameObject.SetActive(visible);
            if (visible)
            {
                label.ForceMeshUpdate(true, true);
            }
        }

        private void ApplyStaticColors()
        {
            if (_title != null)
            {
                _title.color = Tint(_preview ? EditHintColor : HeaderColor);
            }

            if (_editHint != null)
            {
                _editHint.color = Tint(EditHintColor);
            }

            if (_columnPlayer != null) _columnPlayer.color = Tint(ColumnColor);
            if (_columnAccuracy != null) _columnAccuracy.color = Tint(ColumnColor);
            if (_columnScore != null) _columnScore.color = Tint(ColumnColor);
            if (_columnCombo != null) _columnCombo.color = Tint(ColumnColor);
            if (_columnMistakes != null) _columnMistakes.color = Tint(ColumnColor);
            if (_placeholder != null) _placeholder.color = Tint(PlaceholderColor);
            if (_footer != null) _footer.color = Tint(ColumnColor);
        }

        private Color Tint(Color color)
        {
            return new Color(Mathf.Clamp01(color.r * _textBrightness),
                             Mathf.Clamp01(color.g * _textBrightness),
                             Mathf.Clamp01(color.b * _textBrightness),
                             color.a);
        }

        private static void SetFont(TextMeshProUGUI text, float size)
        {
            if (text != null)
            {
                text.fontSize = size;
            }
        }

        /// <summary>
        /// Creates the panel field as one white-sprite Image. The previous implementation filled a
        /// full-rect TMP text with &lt;mark&gt; and a string of dots, then called ForceMeshUpdate up to
        /// three times per layout pass. That generated a large text mesh and required a custom
        /// outline material (a second shader variant) for every panel rebuild. A single quad renders
        /// in one batch and needs no mesh regeneration.
        /// </summary>
        private Image CreateBackdrop(RectTransform parent, float width, float height)
        {
            GameObject backdropObject = new GameObject("Backdrop", typeof(RectTransform));
            backdropObject.layer = 5;
            RectTransform rect = (RectTransform)backdropObject.transform;
            rect.SetParent(parent, false);
            Place(rect, 0f, 0f, width, height);

            _backdropTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _backdropTexture.hideFlags = HideFlags.HideAndDontSave;
            _backdropTexture.SetPixel(0, 0, Color.white);
            _backdropTexture.Apply(false, false);

            _backdropSprite = Sprite.Create(_backdropTexture, new Rect(0f, 0f, 1f, 1f),
                                            new Vector2(0.5f, 0.5f), 100f);
            _backdropSprite.hideFlags = HideFlags.HideAndDontSave;

            Image image = backdropObject.AddComponent<Image>();
            // Assign the sprite and material before enabling the graphic; adding an Image and then
            // enabling it first is what produced the white fallback quad in this build.
            image.enabled = false;
            image.raycastTarget = false;
            image.type = Image.Type.Simple;
            image.color = BackgroundColor;
            image.sprite = _backdropSprite;

            Material material = ResolveBackdropMaterial();
            if (material != null)
            {
                image.material = material;
            }

            image.enabled = true;
            return image;
        }

        private static Material ResolveBackdropMaterial()
        {
            if (_backdropMaterialResolved)
            {
                return _backdropMaterial;
            }

            _backdropMaterialResolved = true;
            try
            {
                // Same material the installed HUD mods use for runtime-created graphics. It is a
                // shared game asset, so it is never destroyed or modified here.
                Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
                if (materials != null)
                {
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material candidate = materials[i];
                        if (candidate != null && string.Equals(candidate.name, "UINoGlow", StringComparison.Ordinal))
                        {
                            _backdropMaterial = candidate;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Backdrop material lookup failed: " + ex.GetType().Name);
            }

            return _backdropMaterial;
        }

        private void UpdateBackdropColor()
        {
            if (_backdrop != null)
            {
                Color color = _editMode ? EditBackgroundColor : BackgroundColor;
                // Keep a faint field visible while editing even when the player chose a fully
                // transparent background; normal play can make it completely invisible.
                color.a = _editMode ? Mathf.Max(_backgroundOpacity, 0.18f) : _backgroundOpacity;
                _backdrop.color = color;
            }
        }

        private string BuildFooter(IReadOnlyList<PlayerRow> rows, int shown)
        {
            if (rows == null || rows.Count == 0)
            {
                return "Waiting for the game to report player scores...";
            }

            int spectators = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsParticipant)
                {
                    spectators++;
                }
            }

            if (rows.Count == _footerCount && spectators == _footerSpectators && shown == _footerShown &&
                _footerCache != null)
            {
                return _footerCache;
            }

            string text = rows.Count + (rows.Count == 1 ? " player" : " players") + " in this session";

            if (spectators > 0)
            {
                text += " (" + spectators + " spectating)";
            }

            if (rows.Count > shown)
            {
                text += " - showing the top " + shown;
            }

            _footerCount = rows.Count;
            _footerSpectators = spectators;
            _footerShown = shown;
            _footerCache = text;
            return text;
        }

        private static Color RankColor(int rank)
        {
            switch (rank)
            {
                case 1: return GoldColor;
                case 2: return SilverColor;
                case 3: return BronzeColor;
                default: return OtherRankColor;
            }
        }

        private bool Set(TextMeshProUGUI label, string value)
        {
            if (label == null)
            {
                return false;
            }

            if (string.Equals(label.text, value, StringComparison.Ordinal))
            {
                // If a newer value was queued and the data reverted before the flush, discard the
                // stale queued value instead of applying it later.
                for (int i = 0; i < _pendingText.Count; i++)
                {
                    if (ReferenceEquals(_pendingText[i].Label, label))
                    {
                        _pendingText.RemoveAt(i);
                        break;
                    }
                }

                return false;
            }

            for (int i = 0; i < _pendingText.Count; i++)
            {
                if (ReferenceEquals(_pendingText[i].Label, label))
                {
                    if (string.Equals(_pendingText[i].Value, value, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    _pendingText[i] = new PendingText(label, value);
                    return true;
                }
            }

            _pendingText.Add(new PendingText(label, value));
            return true;
        }

        // do only one canvas per frame, instead of slamming everything at once. huge studder improvement.
        internal int FlushUi()
        {
            if (_root == null)
            {
                return 0;
            }

            Canvas canvas = NextPendingCanvas();
            if (canvas == null)
            {
                return 0;
            }

            int applied = 0;
            for (int i = _pendingText.Count - 1; i >= 0; i--)
            {
                PendingText change = _pendingText[i];
                if (change.Label == null)
                {
                    _pendingText.RemoveAt(i);
                    continue;
                }

                if (change.Label.canvas != canvas)
                {
                    continue;
                }

                _pendingText.RemoveAt(i);
                if (!string.Equals(change.Label.text, change.Value, StringComparison.Ordinal))
                {
                    change.Label.text = change.Value;
                    applied++;
                }
            }

            for (int i = _pendingColor.Count - 1; i >= 0; i--)
            {
                PendingColor change = _pendingColor[i];
                if (change.Label == null)
                {
                    _pendingColor.RemoveAt(i);
                    continue;
                }

                if (change.Label.canvas != canvas)
                {
                    continue;
                }

                _pendingColor.RemoveAt(i);
                if (change.Label.color != change.Value)
                {
                    change.Label.color = change.Value;
                    applied++;
                }
            }

            return applied;
        }

        private Canvas NextPendingCanvas()
        {
            while (_pendingText.Count > 0)
            {
                int index = _pendingText.Count - 1;
                TextMeshProUGUI label = _pendingText[index].Label;
                if (label == null || label.canvas == null)
                {
                    _pendingText.RemoveAt(index);
                    continue;
                }

                return label.canvas;
            }

            while (_pendingColor.Count > 0)
            {
                int index = _pendingColor.Count - 1;
                TextMeshProUGUI label = _pendingColor[index].Label;
                if (label == null || label.canvas == null)
                {
                    _pendingColor.RemoveAt(index);
                    continue;
                }

                return label.canvas;
            }

            return null;
        }

        private bool SetColor(TextMeshProUGUI label, Color value)
        {
            if (label == null)
            {
                return false;
            }

            for (int i = 0; i < _pendingColor.Count; i++)
            {
                if (!ReferenceEquals(_pendingColor[i].Label, label))
                {
                    continue;
                }

                if (_pendingColor[i].Value == value)
                {
                    return false;
                }

                if (label.color == value)
                {
                    _pendingColor.RemoveAt(i);
                    return false;
                }

                _pendingColor[i] = new PendingColor(label, value);
                return true;
            }

            if (label.color == value)
            {
                return false;
            }

            _pendingColor.Add(new PendingColor(label, value));
            return true;
        }

        private bool EnsureRows(int count)
        {
            int limit = Mathf.Clamp(count, 0, MaxRows);
            for (int i = 0; i < limit; i++)
            {
                if (_rows[i] == null)
                {
                    EnsureRow(i);
                    return true;
                }
            }

            return false;
        }

        private Row EnsureRow(int index)
        {
            if (_rows == null || index < 0 || index >= _rows.Length)
            {
                return null;
            }

            Row row = _rows[index];
            if (row != null)
            {
                return row;
            }

            row = CreateRow(index);
            row.Root.SetActive(false);
            _rows[index] = row;
            _createdRows++;
            return row;
        }

        private void LogTopologyOnce()
        {
            if (_createdRows == 0)
            {
                return;
            }

            // Keep the diagnostics' canvas count current as rows are created without logging the
            // topology again for every row.
            PerformanceDiagnostics.SetUiTopology(CanvasCount, RowCanvasCount, LabelCount);
            if (_topologyLogged)
            {
                return;
            }

            _topologyLogged = true;
            LeaderboardLog.Debug("UI topology: " + CanvasCount + " world-space canvas(es) including " +
                                 RowCanvasCount + " per-row canvas(es), " + LabelCount +
                                 " TMP label(s), one Image backdrop, no custom material.");
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float x, float y, float width,
                                           float height, float fontSize, TextAlignmentOptions alignment,
                                           Color color, FontStyles style)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.layer = 5;
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.richText = false;
            text.text = string.Empty;
            LeaderboardFont.Apply(text);
            if (BloomActive)
            {
                ApplyBloomToLabel(text);
            }
            _texts.Add(text);
            return text;
        }

        private void ApplyTextMaterials()
        {
            if (_texts.Count == 0)
            {
                return;
            }

            if (!BloomActive)
            {
                for (int i = 0; i < _texts.Count; i++)
                {
                    TextMeshProUGUI text = _texts[i];
                    if (text == null || text.font == null)
                    {
                        continue;
                    }

                    if (text.font.material != null && text.fontSharedMaterial != text.font.material)
                    {
                        text.fontSharedMaterial = text.font.material;
                        text.UpdateMeshPadding();
                        text.SetMaterialDirty();
                    }
                }

                return;
            }

            for (int i = 0; i < _texts.Count; i++)
            {
                ApplyBloomToLabel(_texts[i]);
            }
        }

        private void ApplyBloomToLabel(TextMeshProUGUI text)
        {
            if (text == null || text.font == null)
            {
                return;
            }

            Material material = EnsureBloomMaterial(text.font.material);
            if (material == null)
            {
                return;
            }

            if (text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
                text.UpdateMeshPadding();
                text.SetMaterialDirty();
            }
        }

        private Material EnsureBloomMaterial(Material source)
        {
            if (source == null)
            {
                return null;
            }

            // The game's normal TMP material uses the Mobile/Distance Field Zero Alpha Write
            // shader. It exposes outline/underlay properties but does not draw them, which made
            // the old bloom toggle look completely inert. Use the loaded full Distance Field
            // shader for the bloom copy only; normal text keeps the game's fast mobile shader.
            Shader bloomShader = ResolveBloomShader();
            Shader targetShader = bloomShader != null ? bloomShader : source.shader;
            if (_bloomMaterial != null && _bloomMaterial.shader == targetShader &&
                Mathf.Approximately(_bloomMaterialIntensity, _bloomIntensity))
            {
                return _bloomMaterial;
            }

            if (_bloomMaterial != null)
            {
                UnityEngine.Object.Destroy(_bloomMaterial);
            }

            if (bloomShader != null)
            {
                _bloomMaterial = new Material(bloomShader);
                _bloomMaterial.CopyPropertiesFromMaterial(source);
                if (_bloomMaterial.HasProperty("_MainTex") &&
                    _bloomMaterial.GetTexture("_MainTex") == null && source.mainTexture != null)
                {
                    _bloomMaterial.SetTexture("_MainTex", source.mainTexture);
                }
            }
            else
            {
                _bloomMaterial = new Material(source);
            }

            _bloomMaterial.name = "MultiplayerLeaderboardBloom";
            _bloomMaterial.hideFlags = HideFlags.HideAndDontSave;
            _bloomMaterialIntensity = _bloomIntensity;

            // Use every outer-glow feature the game's TMP shader exposes. The outline is the
            // most reliable one and gives a clearly visible halo; glow and underlay add softness.
            // The text face itself stays crisp because these effects are drawn around the glyphs.
            float k = Mathf.Clamp01(_bloomIntensity / UiSettings.MaxBloomIntensity);
            Color glowColor = new Color(0.82f, 0.92f, 1f, 1f);
            string mode = string.Empty;

            if (_bloomMaterial.HasProperty("_OutlineColor"))
            {
                SetMaterialColor(_bloomMaterial, "_OutlineColor",
                                 new Color(glowColor.r, glowColor.g, glowColor.b,
                                           Mathf.Lerp(0.45f, 1f, k)));
                SetMaterialFloat(_bloomMaterial, "_OutlineWidth", Mathf.Lerp(0.12f, 1.05f, k));
                SetMaterialFloat(_bloomMaterial, "_OutlineSoftness", Mathf.Lerp(0.20f, 0.62f, k));
                _bloomMaterial.EnableKeyword("OUTLINE_ON");
                mode = "outline";
            }

            if (_bloomMaterial.HasProperty("_GlowColor"))
            {
                SetMaterialColor(_bloomMaterial, "_GlowColor",
                                 new Color(glowColor.r, glowColor.g, glowColor.b,
                                           Mathf.Lerp(0.35f, 1f, k)));
                SetMaterialFloat(_bloomMaterial, "_GlowOffset", 0f);
                SetMaterialFloat(_bloomMaterial, "_GlowInner", 0.02f);
                SetMaterialFloat(_bloomMaterial, "_GlowOuter", Mathf.Lerp(0.18f, 1.10f, k));
                SetMaterialFloat(_bloomMaterial, "_GlowPower", Mathf.Lerp(0.90f, 0.50f, k));
                _bloomMaterial.EnableKeyword("GLOW_ON");
                mode += mode.Length == 0 ? "glow" : "+glow";
            }

            if (_bloomMaterial.HasProperty("_UnderlayColor"))
            {
                SetMaterialColor(_bloomMaterial, "_UnderlayColor",
                                 new Color(glowColor.r, glowColor.g, glowColor.b,
                                           Mathf.Lerp(0.30f, 0.95f, k)));
                SetMaterialFloat(_bloomMaterial, "_UnderlayOffsetX", 0f);
                SetMaterialFloat(_bloomMaterial, "_UnderlayOffsetY", 0f);
                SetMaterialFloat(_bloomMaterial, "_UnderlayDilate", Mathf.Lerp(0.08f, 0.90f, k));
                SetMaterialFloat(_bloomMaterial, "_UnderlaySoftness", Mathf.Lerp(0.35f, 0.80f, k));
                _bloomMaterial.EnableKeyword("UNDERLAY_ON");
                _bloomMaterial.DisableKeyword("UNDERLAY_INNER");
                mode += mode.Length == 0 ? "underlay" : "+underlay";
            }

            _bloomMode = mode.Length == 0 ? "unsupported shader" : mode;
            if (mode.Length == 0)
            {
                LeaderboardLog.Warn("Text bloom is enabled, but the game's TMP font shader exposes none of the outline, glow or underlay properties.");
            }

            LeaderboardLog.Info("Text bloom material prepared from " + source.name + " using " +
                                _bloomMode + " at intensity " + _bloomIntensity.ToString("0.##") +
                                " (shader " + _bloomMaterial.shader.name + ").");
            return _bloomMaterial;
        }

        private static Shader ResolveBloomShader()
        {
            if (_bloomShaderResolved)
            {
                return _bloomShader;
            }

            _bloomShaderResolved = true;
            try
            {
                Shader[] shaders = Resources.FindObjectsOfTypeAll<Shader>();
                for (int i = 0; i < shaders.Length; i++)
                {
                    Shader shader = shaders[i];
                    if (shader != null && string.Equals(shader.name, "TextMeshPro/Distance Field",
                                                       StringComparison.Ordinal))
                    {
                        _bloomShader = shader;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Text bloom shader lookup failed: " + ex.GetType().Name);
            }

            if (_bloomShader == null)
            {
                LeaderboardLog.Warn("Text bloom could not find the full TMP Distance Field shader; " +
                                    "the game's mobile shader will be used and may not draw the glow.");
            }

            return _bloomShader;
        }

        private static void SetMaterialColor(Material material, string property, Color value)
        {
            if (material != null && material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetMaterialFloat(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static VRGraphicRaycaster AddVrRaycaster(Canvas canvas, string label)
        {
            try
            {
                VRGraphicRaycaster raycaster = canvas.gameObject.AddComponent<VRGraphicRaycaster>();
                System.Reflection.FieldInfo physics = typeof(VRGraphicRaycaster).GetField(
                    "_physicsRaycaster", System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.NonPublic);
                if (physics != null)
                {
                    physics.SetValue(raycaster, new PhysicsRaycasterWithCache());
                }

                System.Reflection.FieldInfo mask = typeof(VRGraphicRaycaster).GetField(
                    "_blockingMask", System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.NonPublic);
                if (mask != null)
                {
                    mask.SetValue(raycaster, (LayerMask)0);
                }

                LeaderboardLog.Debug("VR UI raycaster attached to the " + label + " canvas.");
                return raycaster;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Warn("Could not attach the game's VR UI raycaster to the " + label +
                                    " canvas; the laser pointer will not select it: " + ex.GetType().Name);
                return null;
            }
        }

        // ---------------------------------------------------------------- editing

        internal void SetHover(bool hovered)
        {
            // Hover used to change the backdrop colour by rebuilding the whole TMP <mark> string.
            // The controller ray crosses this large panel often during play, and each rebuild caused
            // a measurable frame stall. The panel now only uses the fixed background colour.
        }

        internal void SetInteractionEnabled(bool enabled)
        {
            if (_root == null || _backdrop == null || _interactionEnabled == enabled)
            {
                return;
            }

            _interactionEnabled = enabled;
            if (enabled)
            {
                if (_raycaster == null)
                {
                    _raycaster = AddVrRaycaster(_canvas, "leaderboard");
                }

                _backdrop.raycastTarget = true;
            }
            else
            {
                _backdrop.raycastTarget = false;
                if (_raycaster != null)
                {
                    UnityEngine.Object.Destroy(_raycaster);
                    _raycaster = null;
                }
            }
        }

        internal void ToggleEditMode()
        {
            if (_editMode)
            {
                // In Test Mode the panel is already draggable without the full editor. F8 then opens
                // the editor controls; a second F8 closes edit mode again.
                if (_editor == null || !_editor.Visible)
                {
                    _editorRequested = true;
                    EnsureEditor();
                    if (_editor != null)
                    {
                        _editor.Show();
                    }

                    return;
                }

                ExitEditMode();
            }
            else
            {
                EnterEditMode();
            }
        }

        internal void EnterTestEditing()
        {
            if (_root == null)
            {
                return;
            }

            _editMode = true;
            _dragEnabled = true;
            _rotationEditing = false;
            _editorRequested = false;
            if (_input != null)
            {
                _input.enabled = true;
            }
            SetInteractionEnabled(true);
            _editHint.text = "TEST MODE: DRAG TO MOVE  |  F8 FOR CONTROLS";
            _editHint.gameObject.SetActive(true);
            UpdateBackdropColor();
            LeaderboardLog.Info("Test Mode editing enabled: the panel can be dragged without the full editor.");
        }

        internal void EnterEditMode()
        {
            if (_root == null)
            {
                return;
            }

            _editMode = true;
            _dragEnabled = true;
            _editorRequested = true;
            if (_input != null)
            {
                _input.enabled = true;
            }
            SetInteractionEnabled(true);
            UpdateEditHint();
            UpdateBackdropColor();

            if (_editor == null && LeaderboardFont.Resolve() != null)
            {
                _editor = new LeaderboardEditor(this);
            }

            if (_editor != null)
            {
                _editor.Show();
            }
            else
            {
                LeaderboardLog.Debug("Leaderboard editor is waiting for the game's TMP font before it can be built.");
            }
            LeaderboardLog.Info("Leaderboard edit mode enabled. Drag the panel with the game's laser pointer; " +
                                "scroll the mouse wheel while dragging to move it closer or farther. " +
                                "Current " + CaptureSettings().Describe() + ".");
        }

        internal void ExitEditMode()
        {
            _editMode = false;
            _dragEnabled = false;
            _rotationEditing = false;
            _editorRequested = false;
            if (_input != null)
            {
                _input.enabled = false;
            }
            SetInteractionEnabled(false);
            _editHint.text = string.Empty;
            _editHint.gameObject.SetActive(false);
            UpdateBackdropColor();

            if (_editor != null)
            {
                _editor.Hide();
            }

            LeaderboardLog.Info("Leaderboard edit mode disabled; normal world-space behaviour restored. " +
                                CaptureSettings().Describe() + ".");
        }

        internal void ToggleDrag()
        {
            if (!_editMode)
            {
                EnterEditMode();
            }

            _dragEnabled = !_dragEnabled;
            UpdateEditHint();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Leaderboard dragging " + (_dragEnabled ? "enabled" : "paused") + ".");
        }

        internal void EnsureEditor()
        {
            if (!_editMode || !_editorRequested || _editor != null || LeaderboardFont.Resolve() == null)
            {
                return;
            }

            try
            {
                _editor = new LeaderboardEditor(this);
                _editor.Show();
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("Could not build the leaderboard editor; it will retry.", ex);
            }
        }

        internal void SetOverallScale(float value)
        {
            _overallScale = Mathf.Clamp(value, UiSettings.MinOverallScale, UiSettings.MaxOverallScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetTextScale(float value)
        {
            _textScale = Mathf.Clamp(value, UiSettings.MinTextScale, UiSettings.MaxTextScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetNameScale(float value)
        {
            _nameScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetAccuracyScale(float value)
        {
            _accuracyScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetScoreScale(float value)
        {
            _scoreScale = Mathf.Clamp(value, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetRowSpacing(float value)
        {
            _rowSpacing = Mathf.Clamp(value, UiSettings.MinRowSpacing, UiSettings.MaxRowSpacing);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetBackgroundWidthScale(float value)
        {
            _backgroundWidthScale = Mathf.Clamp(value, UiSettings.MinBackgroundScale,
                                                UiSettings.MaxBackgroundScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetBackgroundHeightScale(float value)
        {
            _backgroundHeightScale = Mathf.Clamp(value, UiSettings.MinBackgroundScale,
                                                 UiSettings.MaxBackgroundScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetBackgroundOpacity(float value)
        {
            _backgroundOpacity = Mathf.Clamp(value, UiSettings.MinBackgroundOpacity,
                                             UiSettings.MaxBackgroundOpacity);
            UpdateBackdropColor();
        }

        internal void SetTextBrightness(float value)
        {
            _textBrightness = Mathf.Clamp(value, UiSettings.MinTextBrightness,
                                          UiSettings.MaxTextBrightness);
            ApplyStaticColors();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetBloom(bool value)
        {
            if (_bloom == value)
            {
                return;
            }

            _bloom = value;
            ApplyTextMaterials();
            LeaderboardLog.Info("Text bloom " + (value ? "enabled" : "disabled") +
                                (value ? " (" + _bloomMode + " TMP material effect at intensity " +
                                         _bloomIntensity.ToString("0.##") + ")" : "") + ".");
        }

        internal void SetBloomIntensity(float value)
        {
            value = Mathf.Clamp(value, UiSettings.MinBloomIntensity, UiSettings.MaxBloomIntensity);
            if (Mathf.Approximately(_bloomIntensity, value))
            {
                return;
            }

            _bloomIntensity = value;
            ApplyTextMaterials();
            LeaderboardLog.Info("Text bloom intensity set to " + value.ToString("0.##") +
                                (_bloom ? " (" + _bloomMode + ")" : " (bloom is off)") + ".");
        }

        internal void SetShowScore(bool value)
        {
            if (_showScore == value) return;
            _showScore = value;
            ApplyLayout();
        }

        internal void SetShowAccuracy(bool value)
        {
            if (_showAccuracy == value) return;
            _showAccuracy = value;
            ApplyLayout();
        }

        internal void SetShowCombo(bool value)
        {
            if (_showCombo == value) return;
            _showCombo = value;
            ApplyLayout();
        }

        internal void SetShowMistakes(bool value)
        {
            if (_showMistakes == value) return;
            _showMistakes = value;
            ApplyLayout();
        }

        internal void SetWorldRotation(Quaternion rotation)
        {
            if (_root == null)
            {
                return;
            }

            _root.transform.rotation = Normalize(rotation);
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void ApplyWorldRotation(Quaternion delta)
        {
            if (_root == null || delta == default(Quaternion))
            {
                return;
            }

            _root.transform.rotation = Normalize(delta * _root.transform.rotation);
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetRotationEuler(Vector3 euler)
        {
            SetWorldRotation(Quaternion.Euler(euler));
        }

        internal void RotateLocal(float x, float y, float z)
        {
            ApplyWorldRotation(Quaternion.Euler(x, y, z));
        }

        internal void ResetRotation()
        {
            SetWorldRotation(_defaultRotation);
            LeaderboardLog.Info("Leaderboard rotation reset to its default orientation without changing position.");
        }

        internal void SetRotationEditing(bool enabled)
        {
            if (enabled && !_editMode)
            {
                EnterEditMode();
            }

            _rotationEditing = enabled;
            UpdateEditHint();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Leaderboard pointer rotation " + (enabled ? "enabled" : "disabled") +
                                "; position dragging remains available when it is off.");
        }

        internal void ToggleRotationEditing()
        {
            SetRotationEditing(!_rotationEditing);
        }

        private void UpdateEditHint()
        {
            if (_editHint == null)
            {
                return;
            }

            _editHint.text = !_editMode
                ? string.Empty
                : (_rotationEditing ? "EDIT MODE: DRAG TO ROTATE"
                                    : (_dragEnabled ? "EDIT MODE: DRAG TO MOVE"
                                                    : "EDIT MODE: DRAG PAUSED"));
            _editHint.gameObject.SetActive(_editMode);
        }

        internal void ApplyAppearance(UiSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _overallScale = Mathf.Clamp(settings.OverallScale, UiSettings.MinOverallScale,
                                        UiSettings.MaxOverallScale);
            _textScale = Mathf.Clamp(settings.TextScale, UiSettings.MinTextScale, UiSettings.MaxTextScale);
            _nameScale = Mathf.Clamp(settings.NameScale, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            _accuracyScale = Mathf.Clamp(settings.AccuracyScale, UiSettings.MinColumnScale,
                                         UiSettings.MaxColumnScale);
            _scoreScale = Mathf.Clamp(settings.ScoreScale, UiSettings.MinColumnScale, UiSettings.MaxColumnScale);
            _rowSpacing = Mathf.Clamp(settings.RowSpacing, UiSettings.MinRowSpacing, UiSettings.MaxRowSpacing);
            _backgroundWidthScale = Mathf.Clamp(settings.BackgroundWidthScale, UiSettings.MinBackgroundScale,
                                                UiSettings.MaxBackgroundScale);
            _backgroundHeightScale = Mathf.Clamp(settings.BackgroundHeightScale, UiSettings.MinBackgroundScale,
                                                 UiSettings.MaxBackgroundScale);
            _backgroundOpacity = Mathf.Clamp(settings.BackgroundOpacity, UiSettings.MinBackgroundOpacity,
                                             UiSettings.MaxBackgroundOpacity);
            _textBrightness = Mathf.Clamp(settings.TextBrightness, UiSettings.MinTextBrightness,
                                          UiSettings.MaxTextBrightness);
            _bloom = settings.Bloom;
            _bloomIntensity = Mathf.Clamp(settings.BloomIntensity, UiSettings.MinBloomIntensity,
                                          UiSettings.MaxBloomIntensity);
            _showScore = settings.ShowScore;
            _showAccuracy = settings.ShowAccuracy;
            _showCombo = settings.ShowCombo;
            _showMistakes = settings.ShowMistakes;
            _enabled = settings.Enabled;
            _testMode = settings.TestMode;
            _performanceLogging = settings.PerformanceLogging;
            ApplyLayout();
            ApplyTextMaterials();
        }

        internal void ApplyPosition(UiSettings settings)
        {
            if (settings == null || !settings.HasPosition || _root == null)
            {
                return;
            }

            _root.transform.SetPositionAndRotation(settings.Position, settings.Rotation);
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void Nudge(Vector3 localDirection, float meters)
        {
            if (_root == null || localDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            // Position only, in the panel's own left/right/up plane. Rotation is never written.
            _root.transform.position += _root.transform.rotation * localDirection.normalized * meters;
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void AdjustOverallScale(float delta)
        {
            _overallScale = Mathf.Clamp(_overallScale + delta, UiSettings.MinOverallScale,
                                        UiSettings.MaxOverallScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Overall UI scale: " + F(_overallScale) + "; world size " +
                                WorldWidth.ToString("0.00", CultureInfo.InvariantCulture) + " x " +
                                WorldHeight.ToString("0.00", CultureInfo.InvariantCulture) + " m.");
        }

        internal void AdjustTextScale(float delta)
        {
            _textScale = Mathf.Clamp(_textScale + delta, UiSettings.MinTextScale, UiSettings.MaxTextScale);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Text scale: " + F(_textScale) + "; background " +
                                WorldWidth.ToString("0.00", CultureInfo.InvariantCulture) + " x " +
                                WorldHeight.ToString("0.00", CultureInfo.InvariantCulture) + " m.");
        }

        internal void AdjustRowSpacing(float delta)
        {
            _rowSpacing = Mathf.Clamp(_rowSpacing + delta, UiSettings.MinRowSpacing, UiSettings.MaxRowSpacing);
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Row spacing: " + F(_rowSpacing) + "; background height " +
                                WorldHeight.ToString("0.00", CultureInfo.InvariantCulture) + " m.");
        }

        internal void MoveDepth(float meters)
        {
            if (_root == null || Mathf.Abs(meters) < 0.0001f)
            {
                return;
            }

            _root.transform.position += _root.transform.forward * meters;
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void SetWorldPosition(Vector3 position)
        {
            if (_root == null)
            {
                return;
            }

            // Position only. The panel's world rotation is deliberately never assigned here.
            _root.transform.position = position;
        }

        internal void ResetPosition()
        {
            if (_root == null)
            {
                return;
            }

            // Position and rotation are independent. Resetting the position must not undo an
            // orientation the player set through the rotation page or the pointer.
            _root.transform.position = _defaultPosition;
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Leaderboard position reset to its default placement " + Format(_defaultPosition) +
                                "; rotation was left at " + Format(WorldRotation) + ".");
        }

        internal void ResetScale()
        {
            _overallScale = 1f;
            _textScale = 1f;
            _nameScale = 1f;
            _accuracyScale = 1f;
            _scoreScale = 1f;
            _rowSpacing = 1f;
            _backgroundWidthScale = 1f;
            _backgroundHeightScale = 1f;
            ApplyLayout();
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }

            LeaderboardLog.Info("Leaderboard scale reset to overall 1.00, text 1.00, row spacing 1.00.");
        }

        internal UiSettings CaptureSettings()
        {
            return new UiSettings
            {
                HasPosition = true,
                Position = WorldPosition,
                Rotation = WorldRotation,
                OverallScale = _overallScale,
                TextScale = _textScale,
                NameScale = _nameScale,
                AccuracyScale = _accuracyScale,
                ScoreScale = _scoreScale,
                RowSpacing = _rowSpacing,
                BackgroundWidthScale = _backgroundWidthScale,
                BackgroundHeightScale = _backgroundHeightScale,
                BackgroundOpacity = _backgroundOpacity,
                TextBrightness = _textBrightness,
                Bloom = _bloom,
                BloomIntensity = _bloomIntensity,
                ShowScore = _showScore,
                ShowAccuracy = _showAccuracy,
                ShowCombo = _showCombo,
                ShowMistakes = _showMistakes,
                BackgroundWidth = WorldWidth,
                BackgroundHeight = WorldHeight,
                Enabled = _enabled,
                TestMode = _testMode,
                PerformanceLogging = _performanceLogging
            };
        }

        internal void SaveSettings()
        {
            UiSettings settings = CaptureSettings();
            settings.Save(WorldWidth, WorldHeight);
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void LogDragFinished()
        {
            LeaderboardLog.Info("Leaderboard dragged to " + Format(WorldPosition) + "; rotation unchanged at " +
                                Format(WorldRotation) + ".");
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void LogRotationFinished()
        {
            LeaderboardLog.Info("Leaderboard rotated to " + Format(WorldRotation) + "; position unchanged at " +
                                Format(WorldPosition) + ".");
            if (_editor != null)
            {
                _editor.RefreshStatus();
            }
        }

        internal void Dispose()
        {
            if (_editor != null)
            {
                _editor.Dispose();
                _editor = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            if (_backdropSprite != null)
            {
                UnityEngine.Object.Destroy(_backdropSprite);
            }

            if (_backdropTexture != null)
            {
                UnityEngine.Object.Destroy(_backdropTexture);
            }

            if (_bloomMaterial != null)
            {
                UnityEngine.Object.Destroy(_bloomMaterial);
                _bloomMaterial = null;
            }

            _backdropSprite = null;
            _backdropTexture = null;
            _raycaster = null;
            _input = null;
            _interactionEnabled = false;
            _root = null;
            _rect = null;
            _canvas = null;
            _backdrop = null;
            _backdropClip = null;
            _rows = null;
            _createdRows = 0;
            _rowCanvasCount = 0;
            _topologyLogged = false;
            _title = null;
            _columnPlayer = null;
            _columnAccuracy = null;
            _columnScore = null;
            _placeholder = null;
            _footer = null;
            _editHint = null;
            _texts.Clear();
            _pendingText.Clear();
            _pendingColor.Clear();
            _footerCache = null;
            _footerCount = -1;
            _footerSpectators = -1;
            _footerShown = -1;
            _fontsReady = false;
            _bloomMode = "off";
            _bloomMaterialIntensity = -1f;
            _rotationEditing = false;
            _lastRanks.Clear();
            _rankFlashUntil.Clear();
            _seenRankKeys.Clear();
            _rankKeys.Clear();
        }

        private void TrackRankChanges(IReadOnlyList<PlayerRow> rows, int count, float now)
        {
            _seenRankKeys.Clear();
            for (int i = 0; i < count; i++)
            {
                PlayerRow data = rows[i];
                if (data == null || !data.IsParticipant || data.Rank <= 0)
                {
                    continue;
                }

                string key = RankKey(data);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                int previous;
                // Any real rank movement flashes, up or down. A routine refresh with the same rank
                // does not, and a newly seen player has no previous rank to compare against.
                if (_lastRanks.TryGetValue(key, out previous) && previous > 0 && data.Rank != previous)
                {
                    _rankFlashUntil[key] = now + RankFlashSeconds;
                }

                _lastRanks[key] = data.Rank;
                _seenRankKeys.Add(key);
            }

            _rankKeys.Clear();
            _rankKeys.AddRange(_lastRanks.Keys);
            for (int i = 0; i < _rankKeys.Count; i++)
            {
                string key = _rankKeys[i];
                if (!_seenRankKeys.Contains(key))
                {
                    _lastRanks.Remove(key);
                    _rankFlashUntil.Remove(key);
                }
            }
        }

        private Color NameColorFor(PlayerRow data, float now)
        {
            Color color = data.IsMe ? LocalColor : NameColor;
            if (data.Rank == 1)
            {
                color = Color.Lerp(color, GoldColor, 0.55f);
            }

            float flashUntil;
            if (_rankFlashUntil.TryGetValue(RankKey(data), out flashUntil) && now < flashUntil)
            {
                color = Color.Lerp(color, RankLossColor, 0.78f);
            }

            return Tint(color);
        }

        private static string RankKey(PlayerRow data)
        {
            if (data == null) return string.Empty;
            if (!string.IsNullOrEmpty(data.UserId)) return data.UserId;
            if (!string.IsNullOrEmpty(data.CacheKey)) return data.CacheKey;
            return data.DisplayName ?? string.Empty;
        }

        private static Quaternion Normalize(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y +
                                         rotation.z * rotation.z + rotation.w * rotation.w);
            return magnitude > 0.0001f
                ? new Quaternion(rotation.x / magnitude, rotation.y / magnitude,
                                 rotation.z / magnitude, rotation.w / magnitude)
                : Quaternion.identity;
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

        private static string F(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                   value.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                   value.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        }

        private static string Format(Quaternion value)
        {
            return "(" + value.x.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   value.y.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   value.z.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   value.w.ToString("0.000", CultureInfo.InvariantCulture) + ")";
        }
    }
}
