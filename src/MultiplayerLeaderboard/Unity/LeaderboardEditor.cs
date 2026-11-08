using System;
using System.Collections.Generic;
using System.Globalization;
using MultiplayerLeaderboard.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerLeaderboard.Unity
{
    /// <summary>
    /// The in-game control panel. It is a second world-space canvas beside the leaderboard, and every
    /// button is an ordinary Unity UI Button. Beat Saber's own VRInputModule and VRGraphicRaycaster
    /// deliver the desktop laser-pointer click; this class adds no custom pointer of its own.
    /// </summary>
    internal sealed class LeaderboardEditor
    {
        private const float Width = 1000f;
        private const float Height = 1000f;
        private const float ButtonWidth = 470f;
        private const float ButtonHeight = 76f;
        private const float PixelScale = 0.001f;

        private static readonly Color PanelColor = new Color(0.020f, 0.030f, 0.048f, 1f);
        private static readonly Color ButtonColor = new Color(0.070f, 0.105f, 0.155f, 1f);
        private static readonly Color ButtonColorPrimary = new Color(0.120f, 0.210f, 0.310f, 1f);
        private static readonly Color ButtonColorDanger = new Color(0.220f, 0.100f, 0.100f, 1f);
        private static readonly Color TextColor = new Color(0.90f, 0.95f, 1.00f, 1f);
        private static readonly Color DimColor = new Color(0.64f, 0.73f, 0.86f, 1f);
        private static readonly Color GoldColor = new Color(1.00f, 0.82f, 0.30f, 1f);

        private readonly WorldLeaderboardPanel _panel;
        private GameObject _root;
        private Canvas _canvas;
        private TextMeshProUGUI _backdrop;
        private RectTransform _backdropClip;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _hint;
        private bool _visible;
        private Material _panelMaterial;
        private TMP_FontAsset _panelMaterialFont;
        private readonly Dictionary<GameObject, Action> _buttonActions = new Dictionary<GameObject, Action>();
        private GameObject _lastClickTarget;
        private float _lastClickTime;

        internal bool Visible => _visible;

        internal LeaderboardEditor(WorldLeaderboardPanel panel)
        {
            _panel = panel;
            Build();
        }

        private Vector3 PanelCenter
        {
            get
            {
                return _panel.WorldPosition + _panel.WorldRotation * Vector3.up * (_panel.WorldHeight * 0.5f);
            }
        }

        private void Build()
        {
            _root = new GameObject("MultiplayerLeaderboard.Editor", typeof(RectTransform), typeof(Canvas));
            _root.transform.localScale = Vector3.one * PixelScale;

            RectTransform rootRect = (RectTransform)_root.transform;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(Width, Height);

            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32761;
            _canvas.worldCamera = _panel.Canvas != null ? _panel.Canvas.worldCamera : Camera.main;
            WorldLeaderboardPanel.AddVrRaycaster(_canvas, "editor");

            GameObject clipObject = new GameObject("BackdropClip", typeof(RectTransform), typeof(RectMask2D));
            _backdropClip = (RectTransform)clipObject.transform;
            _backdropClip.SetParent(_root.transform, false);
            Place(_backdropClip, 0f, 0f, Width, Height);

            _backdrop = CreateText(_backdropClip, "Backdrop", 0f, 0f, Width, Height, 52f,
                                   TextAlignmentOptions.TopLeft, PanelColor, FontStyles.Normal);
            _backdrop.raycastTarget = true;
            _backdrop.richText = true;
            _backdrop.enableWordWrapping = false;
            _backdrop.overflowMode = TextOverflowModes.Overflow;

            _title = CreateText(_root.transform, "Title", 24f, 18f, 952f, 48f, 44f, TextAlignmentOptions.Left,
                                GoldColor, FontStyles.Bold);
            _title.text = "LEADERBOARD UI EDITOR";

            _status = CreateText(_root.transform, "Status", 24f, 70f, 952f, 110f, 24f,
                                 TextAlignmentOptions.TopLeft, DimColor, FontStyles.Normal);
            _status.enableWordWrapping = true;

            CreateButton("EDIT POSITION", 24f, 190f, ButtonWidth, ButtonHeight, ButtonColorPrimary,
                         () => _panel.EnterEditMode());
            CreateButton("MOVE UI", 506f, 190f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.ToggleDrag());

            CreateButton("SCALE -", 24f, 280f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustOverallScale(-0.1f));
            CreateButton("SCALE +", 506f, 280f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustOverallScale(0.1f));

            CreateButton("TEXT -", 24f, 370f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustTextScale(-0.1f));
            CreateButton("TEXT +", 506f, 370f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustTextScale(0.1f));

            CreateButton("ROWS -", 24f, 460f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustRowSpacing(-0.1f));
            CreateButton("ROWS +", 506f, 460f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.AdjustRowSpacing(0.1f));

            CreateButton("DEPTH -", 24f, 550f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.MoveDepth(-0.05f));
            CreateButton("DEPTH +", 506f, 550f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.MoveDepth(0.05f));

            CreateButton("SAVE UI SETTINGS", 24f, 640f, ButtonWidth, ButtonHeight, ButtonColorPrimary,
                         () => _panel.SaveSettings());
            CreateButton("RESET POSITION", 506f, 640f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.ResetPosition());

            CreateButton("RESET SCALE", 24f, 730f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.ResetScale());
            CreateButton("EXIT EDIT MODE", 506f, 730f, ButtonWidth, ButtonHeight, ButtonColorDanger,
                         () => _panel.ExitEditMode());

            CreateButton("ROTATE MODE", 24f, 820f, ButtonWidth, ButtonHeight, ButtonColorPrimary,
                         () => _panel.ToggleRotationEditing());
            CreateButton("RESET ROTATION", 506f, 820f, ButtonWidth, ButtonHeight, ButtonColor,
                         () => _panel.ResetRotation());

            _hint = CreateText(_root.transform, "Hint", 24f, 906f, 952f, 86f, 23f,
                               TextAlignmentOptions.TopLeft, DimColor, FontStyles.Normal);
            _hint.enableWordWrapping = true;
            _hint.text = "Laser pointer: drag to move. Turn on ROTATE MODE, or right-click-drag, to rotate freely. " +
                         "Scroll while rotating to add roll. Press F8 to open or close this editor.";

            RefreshBackdrop();
            _root.SetActive(false);
        }

        internal void Show()
        {
            if (_root == null)
            {
                return;
            }

            _visible = true;
            _root.SetActive(true);
            if (_canvas != null && _panel.Canvas != null)
            {
                _canvas.worldCamera = _panel.Canvas.worldCamera;
            }

            RefreshStatus();
            RefreshBackdrop();
            Tick();
            LeaderboardLog.Info("Leaderboard editor shown beside the panel. Use the game's laser pointer on the buttons, " +
                                "or point at the leaderboard and hold the trigger / left mouse button to drag it.");
        }

        internal void Hide()
        {
            _visible = false;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        internal void Tick()
        {
            if (!_visible || _root == null)
            {
                return;
            }

            float panelHalfWidth = _panel.WorldWidth * 0.5f;
            float editorHalfWidth = Width * PixelScale * 0.5f;
            Vector3 center = PanelCenter -
                             _panel.WorldRotation * Vector3.right * (panelHalfWidth + editorHalfWidth + 0.18f);
            _root.transform.SetPositionAndRotation(center, _panel.WorldRotation);
        }

        internal void RefreshStatus()
        {
            if (_status == null)
            {
                return;
            }

            Vector3 euler = _panel.WorldRotation.eulerAngles;
            Vector3 position = _panel.WorldPosition;
            _status.text = "POS (" + F(position.x) + ", " + F(position.y) + ", " + F(position.z) + ")\n" +
                           "ROT (" + F(euler.x) + ", " + F(euler.y) + ", " + F(euler.z) + ")\n" +
                           "SCALE " + F(_panel.OverallScale) + "   TEXT " + F(_panel.TextScale) +
                           "   ROWS " + F(_panel.RowSpacing) + "\n" +
                           "NAME " + F(_panel.NameScale) + "   ACC " + F(_panel.AccuracyScale) +
                           "   SCORE " + F(_panel.ScoreScale) + "\n" +
                           "BG " + F(_panel.WorldWidth) + " x " + F(_panel.WorldHeight) + " m   " +
                           (_panel.DragEnabled ? "DRAG READY" : "DRAG PAUSED") + "\n" +
                           (_panel.RotationEditing ? "ROTATE READY (pointer drag rotates)" : "ROTATE OFF") +
                           "   COLUMNS " + (_panel.ShowScore ? "S" : "-") +
                           (_panel.ShowAccuracy ? "A" : "-") + (_panel.ShowCombo ? "C" : "-") +
                           (_panel.ShowMistakes ? "M" : "-");
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float x, float y, float width,
                                            float height, float fontSize, TextAlignmentOptions alignment,
                                            Color color, FontStyles style)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.richText = false;
            LeaderboardFont.Apply(text);
            ApplyPanelMaterial(text);

            return text;
        }

        private void CreateButton(string label, float x, float y, float width, float height, Color color,
                                  Action action)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(TextMeshProUGUI),
                                                     typeof(Button));
            RectTransform buttonRect = (RectTransform)buttonObject.transform;
            buttonRect.SetParent(_root.transform, false);
            Place(buttonRect, x, y, width, height);

            // The label itself is the raycast target and the Button target. There is deliberately no
            // Image here: runtime-created Image/RawImage components in this build render with a white
            // fallback material, and a TMP <mark> in the same object can cover its own label.
            TextMeshProUGUI text = buttonObject.GetComponent<TextMeshProUGUI>();
            text.raycastTarget = true;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Left;
            text.fontSize = 44f;
            text.color = color == ButtonColorDanger
                ? new Color(1.00f, 0.55f, 0.55f, 1f)
                : (color == ButtonColorPrimary ? GoldColor : TextColor);
            text.fontStyle = FontStyles.Bold;
            text.richText = false;
            text.text = "  " + label;
            LeaderboardFont.Apply(text);
            ApplyPanelMaterial(text);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = text;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => TryClick(buttonObject));
            _buttonActions[buttonObject] = () => InvokeButton(label, action);
        }

        internal bool TryClick(GameObject target)
        {
            if (target == null || !_visible || !_buttonActions.TryGetValue(target, out Action action))
            {
                return false;
            }

            // On a desktop click the game's Button.onClick and this fallback can both run. Ignore
            // the second delivery of the same click so toggle buttons do not toggle twice.
            float now = Time.unscaledTime;
            if (_lastClickTarget == target && now - _lastClickTime < 0.3f)
            {
                return true;
            }

            _lastClickTarget = target;
            _lastClickTime = now;
            action();
            return true;
        }

        private static void InvokeButton(string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("Leaderboard editor button '" + label + "' failed.", ex);
            }
        }

        private void RefreshBackdrop()
        {
            if (_backdrop == null)
            {
                return;
            }

            if (_backdrop.font == null || _backdrop.font.faceInfo.pointSize <= 0f ||
                _backdrop.font.faceInfo.lineHeight <= 0f)
            {
                _backdrop.text = string.Empty;
                return;
            }

            float lineRatio = _backdrop.font.faceInfo.lineHeight / _backdrop.font.faceInfo.pointSize;
            _backdrop.fontSize = Mathf.Max(12f, Height / Mathf.Max(0.1f, lineRatio) * 1.05f);
            _backdrop.color = PanelColor;
            string colorTag = ColorUtility.ToHtmlStringRGBA(PanelColor);

            int count = Mathf.Max(2, Mathf.CeilToInt(Width / Mathf.Max(1f, _backdrop.fontSize * 0.30f)) + 2);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _backdrop.text = "<mark=#" + colorTag + ">" + new string('.', count) + "</mark>";
                _backdrop.ForceMeshUpdate();
                float measured = _backdrop.preferredWidth;
                if (measured >= Width * 0.98f || measured <= 1f)
                {
                    break;
                }

                count = Mathf.CeilToInt(count * (Width / measured)) + 2;
            }
        }

        internal void Dispose()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            if (_panelMaterial != null)
            {
                UnityEngine.Object.Destroy(_panelMaterial);
            }

            _panelMaterial = null;
            _panelMaterialFont = null;
            _root = null;
            _canvas = null;
            _backdrop = null;
            _backdropClip = null;
            _status = null;
            _title = null;
            _hint = null;
            _buttonActions.Clear();
            _visible = false;
        }

        private void ApplyPanelMaterial(TextMeshProUGUI text)
        {
            if (text == null || text.font == null || text.font.material == null)
            {
                return;
            }

            if (_panelMaterial == null || _panelMaterialFont != text.font)
            {
                if (_panelMaterial != null)
                {
                    UnityEngine.Object.Destroy(_panelMaterial);
                }

                _panelMaterial = new Material(text.font.material);
                _panelMaterial.hideFlags = HideFlags.HideAndDontSave;
                _panelMaterialFont = text.font;
            }

            // The fontMaterial getter creates a Material instance per label and defeats batching.
            if (text.fontSharedMaterial != _panelMaterial)
            {
                text.fontSharedMaterial = _panelMaterial;
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

        private static string F(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
