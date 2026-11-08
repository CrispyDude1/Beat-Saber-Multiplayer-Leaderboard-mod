using System.Globalization;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using MultiplayerLeaderboard.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerLeaderboard.UI
{
    /// <summary>
    /// The page opened by the Multiplayer Leaderboard button in Beat Saber's Mods pane. It is a normal
    /// BSML settings page; every control edits the same live runtime that the in-song panel and F8 editor
    /// use, so the settings menu is not a second, disconnected configuration system.
    ///
    /// The menu is deliberately a small hub rather than one long list. Each category is a separate
    /// vertical page in the same BSML document, shown one at a time with its own Back button. Keeping
    /// one document means the existing Gameplay Setup tab (the pause-menu Mods panel) can host the same
    /// complete interface without needing a second flow coordinator.
    /// </summary>
    [ViewDefinition("MultiplayerLeaderboard.UI.LeaderboardSettings.bsml")]
    internal sealed class LeaderboardSettingsViewController : BSMLAutomaticViewController
    {
        private static LeaderboardRuntime Runtime => LeaderboardRuntime.Instance;

        private static UiSettings Settings
        {
            get { return Runtime != null ? Runtime.Settings : null; }
        }

        [UIComponent("page-main")] private RectTransform _pageMain;
        [UIComponent("page-positioning")] private RectTransform _pagePositioning;
        [UIComponent("page-rotation")] private RectTransform _pageRotation;
        [UIComponent("page-scaling")] private RectTransform _pageScaling;
        [UIComponent("page-appearance")] private RectTransform _pageAppearance;
        [UIComponent("page-text")] private RectTransform _pageText;
        [UIComponent("page-display")] private RectTransform _pageDisplay;
        private RectTransform _activePage;

        [UIValue("status")]
        public string Status
        {
            get { return Runtime != null ? Runtime.StatusText() : "Multiplayer Leaderboard is still starting."; }
        }

        [UIValue("leaderboard-enabled")]
        public bool LeaderboardEnabled
        {
            get { return Settings == null || Settings.Enabled; }
            set
            {
                if (Runtime != null && Settings != null && Settings.Enabled != value)
                {
                    Runtime.SetMasterEnabled(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("test-mode")]
        public bool TestMode
        {
            get { return Settings != null && Settings.TestMode; }
            set
            {
                if (Runtime != null && Settings != null && Settings.TestMode != value)
                {
                    Runtime.SetTestMode(value);
                    RefreshStatus();
                    RefreshValues();
                }
            }
        }

        [UIValue("performance-diagnostics")]
        public bool PerformanceDiagnostics
        {
            get { return Settings != null && Settings.PerformanceLogging; }
            set
            {
                if (Runtime != null && Settings != null && Settings.PerformanceLogging != value)
                {
                    Runtime.SetPerformanceLogging(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("overall-scale")]
        public float OverallScale
        {
            get { return Settings != null ? Settings.OverallScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.OverallScale, value))
                {
                    Runtime.SetOverallScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("text-scale")]
        public float TextScale
        {
            get { return Settings != null ? Settings.TextScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null && !Mathf.Approximately(Settings.TextScale, value))
                {
                    Runtime.SetTextScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("name-scale")]
        public float NameScale
        {
            get { return Settings != null ? Settings.NameScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null && !Mathf.Approximately(Settings.NameScale, value))
                {
                    Runtime.SetNameScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("accuracy-scale")]
        public float AccuracyScale
        {
            get { return Settings != null ? Settings.AccuracyScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.AccuracyScale, value))
                {
                    Runtime.SetAccuracyScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("score-scale")]
        public float ScoreScale
        {
            get { return Settings != null ? Settings.ScoreScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null && !Mathf.Approximately(Settings.ScoreScale, value))
                {
                    Runtime.SetScoreScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("row-spacing")]
        public float RowSpacing
        {
            get { return Settings != null ? Settings.RowSpacing : 1f; }
            set
            {
                if (Runtime != null && Settings != null && !Mathf.Approximately(Settings.RowSpacing, value))
                {
                    Runtime.SetRowSpacing(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("background-width")]
        public float BackgroundWidth
        {
            get { return Settings != null ? Settings.BackgroundWidthScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.BackgroundWidthScale, value))
                {
                    Runtime.SetBackgroundWidthScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("background-height")]
        public float BackgroundHeight
        {
            get { return Settings != null ? Settings.BackgroundHeightScale : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.BackgroundHeightScale, value))
                {
                    Runtime.SetBackgroundHeightScale(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("background-opacity")]
        public float BackgroundOpacity
        {
            get { return Settings != null ? Settings.BackgroundOpacity : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.BackgroundOpacity, value))
                {
                    Runtime.SetBackgroundOpacity(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("text-brightness")]
        public float TextBrightness
        {
            get { return Settings != null ? Settings.TextBrightness : 1f; }
            set
            {
                if (Runtime != null && Settings != null &&
                    !Mathf.Approximately(Settings.TextBrightness, value))
                {
                    Runtime.SetTextBrightness(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("bloom")]
        public bool Bloom
        {
            get { return Settings != null && Settings.Bloom; }
            set
            {
                if (Runtime != null && Settings != null && Settings.Bloom != value)
                {
                    Runtime.SetBloom(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("bloom-intensity")]
        public string BloomIntensity
        {
            get
            {
                float value = Settings != null ? Settings.BloomIntensity : UiSettings.DefaultBloomIntensity;
                return value.ToString("0.##", CultureInfo.InvariantCulture);
            }
            set
            {
                if (Runtime == null || Settings == null)
                {
                    return;
                }

                float parsed;
                bool valid = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                                             out parsed);
                if (!valid)
                {
                    valid = float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture,
                                           out parsed);
                }

                if (!valid)
                {
                    parsed = Settings.BloomIntensity;
                }

                float clamped = Mathf.Clamp(parsed, UiSettings.MinBloomIntensity,
                                            UiSettings.MaxBloomIntensity);
                if (!Mathf.Approximately(Settings.BloomIntensity, clamped))
                {
                    Runtime.SetBloomIntensity(clamped);
                }

                RefreshStatus();
                RefreshValues();
            }
        }

        [UIValue("show-score")]
        public bool ShowScore
        {
            get { return Settings == null || Settings.ShowScore; }
            set
            {
                if (Runtime != null && Settings != null && Settings.ShowScore != value)
                {
                    Runtime.SetShowScore(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("show-accuracy")]
        public bool ShowAccuracy
        {
            get { return Settings == null || Settings.ShowAccuracy; }
            set
            {
                if (Runtime != null && Settings != null && Settings.ShowAccuracy != value)
                {
                    Runtime.SetShowAccuracy(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("show-combo")]
        public bool ShowCombo
        {
            get { return Settings == null || Settings.ShowCombo; }
            set
            {
                if (Runtime != null && Settings != null && Settings.ShowCombo != value)
                {
                    Runtime.SetShowCombo(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("show-mistakes")]
        public bool ShowMistakes
        {
            get { return Settings == null || Settings.ShowMistakes; }
            set
            {
                if (Runtime != null && Settings != null && Settings.ShowMistakes != value)
                {
                    Runtime.SetShowMistakes(value);
                    RefreshStatus();
                }
            }
        }

        [UIValue("spectator-x")]
        public float SpectatorX
        {
            get { return Settings != null ? Settings.SpectatorOffset.x : 0f; }
            set
            {
                if (Runtime != null && Settings != null)
                {
                    Runtime.SetSpectatorOffset(new Vector3(value, Settings.SpectatorOffset.y,
                                                           Settings.SpectatorOffset.z));
                    RefreshStatus();
                }
            }
        }

        [UIValue("spectator-y")]
        public float SpectatorY
        {
            get { return Settings != null ? Settings.SpectatorOffset.y : 0f; }
            set
            {
                if (Runtime != null && Settings != null)
                {
                    Runtime.SetSpectatorOffset(new Vector3(Settings.SpectatorOffset.x, value,
                                                           Settings.SpectatorOffset.z));
                    RefreshStatus();
                }
            }
        }

        [UIValue("spectator-z")]
        public float SpectatorZ
        {
            get { return Settings != null ? Settings.SpectatorOffset.z : 0f; }
            set
            {
                if (Runtime != null && Settings != null)
                {
                    Runtime.SetSpectatorOffset(new Vector3(Settings.SpectatorOffset.x,
                                                           Settings.SpectatorOffset.y, value));
                    RefreshStatus();
                }
            }
        }

        [UIValue("rotation-x")]
        public float RotationX
        {
            get { return RotationValue.x; }
            set { SetRotationAxis(value, RotationValue.y, RotationValue.z); }
        }

        [UIValue("rotation-y")]
        public float RotationY
        {
            get { return RotationValue.y; }
            set { SetRotationAxis(RotationValue.x, value, RotationValue.z); }
        }

        [UIValue("rotation-z")]
        public float RotationZ
        {
            get { return RotationValue.z; }
            set { SetRotationAxis(RotationValue.x, RotationValue.y, value); }
        }

        [UIValue("pointer-rotation")]
        public bool PointerRotation
        {
            get { return Runtime != null && Runtime.Panel != null && Runtime.Panel.RotationEditing; }
            set
            {
                if (Runtime != null)
                {
                    Runtime.SetRotationEditing(value);
                    RefreshRotationValues();
                }
            }
        }

        private static Vector3 RotationValue
        {
            get { return Runtime != null ? Runtime.RotationEuler : Vector3.zero; }
        }

        [UIAction("open-positioning")] private void OpenPositioning() { ShowPage(_pagePositioning); }
        [UIAction("open-rotation")] private void OpenRotation() { ShowPage(_pageRotation); }
        [UIAction("open-scaling")] private void OpenScaling() { ShowPage(_pageScaling); }
        [UIAction("open-appearance")] private void OpenAppearance() { ShowPage(_pageAppearance); }
        [UIAction("open-text")] private void OpenText() { ShowPage(_pageText); }
        [UIAction("open-display")] private void OpenDisplay() { ShowPage(_pageDisplay); }

        [UIAction("go-main")]
        private void GoMain()
        {
            ShowPage(_pageMain);
            RefreshValues();
        }

        [UIAction("edit-position")]
        private void EditPosition()
        {
            Runtime?.BeginEditing();
            RefreshStatus();
        }

        [UIAction("move-left")] private void MoveLeft() { Runtime?.Nudge(-1f, 0f, 0f); RefreshStatus(); }
        [UIAction("move-right")] private void MoveRight() { Runtime?.Nudge(1f, 0f, 0f); RefreshStatus(); }
        [UIAction("move-up")] private void MoveUp() { Runtime?.Nudge(0f, 1f, 0f); RefreshStatus(); }
        [UIAction("move-down")] private void MoveDown() { Runtime?.Nudge(0f, -1f, 0f); RefreshStatus(); }
        [UIAction("move-closer")] private void MoveCloser() { Runtime?.Nudge(0f, 0f, 1f); RefreshStatus(); }
        [UIAction("move-farther")] private void MoveFarther() { Runtime?.Nudge(0f, 0f, -1f); RefreshStatus(); }

        [UIAction("toggle-drag")]
        private void ToggleDrag()
        {
            Runtime?.ToggleDrag();
            RefreshStatus();
        }

        [UIAction("exit-edit")]
        private void ExitEdit()
        {
            Runtime?.ExitEditing();
            RefreshStatus();
            RefreshRotationValues();
        }

        [UIAction("save-position")]
        private void SavePosition()
        {
            Runtime?.SavePosition();
            RefreshStatus();
        }

        [UIAction("reset-position")]
        private void ResetPosition()
        {
            Runtime?.ResetPosition();
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-spectator-position")]
        private void ResetSpectatorPosition()
        {
            Runtime?.ResetSpectatorPosition();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("restore-saved")]
        private void RestoreSaved()
        {
            Runtime?.RestoreSavedSettings();
            RefreshStatus();
            RefreshValues();
            RefreshRotationValues();
        }

        [UIAction("reset-rotation")]
        private void ResetRotation()
        {
            Runtime?.ResetRotation();
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshRotationValues();
        }

        [UIAction("reset-rotation-x")]
        private void ResetRotationX()
        {
            SetRotationAxis(0f, RotationValue.y, RotationValue.z);
            Runtime?.SaveSettings();
        }

        [UIAction("reset-rotation-y")]
        private void ResetRotationY()
        {
            SetRotationAxis(RotationValue.x, 0f, RotationValue.z);
            Runtime?.SaveSettings();
        }

        [UIAction("reset-rotation-z")]
        private void ResetRotationZ()
        {
            SetRotationAxis(RotationValue.x, RotationValue.y, 0f);
            Runtime?.SaveSettings();
        }

        [UIAction("toggle-pointer-rotation")]
        private void TogglePointerRotation()
        {
            Runtime?.ToggleRotationEditing();
            RefreshStatus();
            RefreshRotationValues();
        }

        [UIAction("rotate-x-minus")] private void RotateXMinus() { Rotate(-5f, 0f, 0f); }
        [UIAction("rotate-x-plus")] private void RotateXPlus() { Rotate(5f, 0f, 0f); }
        [UIAction("rotate-y-minus")] private void RotateYMinus() { Rotate(0f, -5f, 0f); }
        [UIAction("rotate-y-plus")] private void RotateYPlus() { Rotate(0f, 5f, 0f); }
        [UIAction("rotate-z-minus")] private void RotateZMinus() { Rotate(0f, 0f, -5f); }
        [UIAction("rotate-z-plus")] private void RotateZPlus() { Rotate(0f, 0f, 5f); }

        [UIAction("reset-scale")]
        private void ResetScale()
        {
            Runtime?.ResetScale();
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-overall-scale")]
        private void ResetOverallScale()
        {
            Runtime?.SetOverallScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-row-spacing")]
        private void ResetRowSpacing()
        {
            Runtime?.SetRowSpacing(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-background-width")]
        private void ResetBackgroundWidth()
        {
            Runtime?.SetBackgroundWidthScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-background-height")]
        private void ResetBackgroundHeight()
        {
            Runtime?.SetBackgroundHeightScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-text-scale")]
        private void ResetTextScale()
        {
            Runtime?.SetTextScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-name-scale")]
        private void ResetNameScale()
        {
            Runtime?.SetNameScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-accuracy-scale")]
        private void ResetAccuracyScale()
        {
            Runtime?.SetAccuracyScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-score-scale")]
        private void ResetScoreScale()
        {
            Runtime?.SetScoreScale(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-background-opacity")]
        private void ResetBackgroundOpacity()
        {
            Runtime?.SetBackgroundOpacity(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-text-brightness")]
        private void ResetTextBrightness()
        {
            Runtime?.SetTextBrightness(1f);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-bloom-intensity")]
        private void ResetBloomIntensity()
        {
            Runtime?.SetBloomIntensity(UiSettings.DefaultBloomIntensity);
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("reset-appearance")]
        private void ResetAppearance()
        {
            Runtime?.SetBackgroundOpacity(1f);
            Runtime?.SetTextBrightness(1f);
            Runtime?.SetBloomIntensity(UiSettings.DefaultBloomIntensity);
            Runtime?.SetBloom(false);
            Runtime?.SaveSettings();
            RefreshStatus();
            RefreshValues();
        }

        [UIAction("save-settings")]
        private void SaveSettings()
        {
            Runtime?.SaveSettings();
            RefreshStatus();
        }

        [UIAction("reset-settings")]
        private void ResetSettings()
        {
            Runtime?.ResetSettings();
            ShowPage(_pageMain);
            RefreshStatus();
            RefreshValues();
            RefreshRotationValues();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy,
                                            bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            ShowPage(_pageMain);
            RefreshStatus();
            RefreshValues();
            RefreshRotationValues();
        }

        private void Rotate(float x, float y, float z)
        {
            Runtime?.Rotate(x, y, z);
            RefreshStatus();
            RefreshRotationValues();
        }

        private void SetRotationAxis(float x, float y, float z)
        {
            if (Runtime != null)
            {
                Runtime.SetRotationEuler(new Vector3(x, y, z));
                RefreshStatus();
                RefreshRotationValues();
            }
        }

        private void ShowPage(RectTransform page)
        {
            if (page == null)
            {
                return;
            }

            // Hide every page explicitly. On the first activation _activePage is null, so hiding
            // only the previous page would leave every category visible at once.
            HidePage(_pageMain);
            HidePage(_pagePositioning);
            HidePage(_pageRotation);
            HidePage(_pageScaling);
            HidePage(_pageAppearance);
            HidePage(_pageText);
            HidePage(_pageDisplay);

            _activePage = page;
            if (!page.gameObject.activeSelf)
            {
                page.gameObject.SetActive(true);
            }

            RectTransform root = transform as RectTransform;
            if (root != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            }
        }

        private static void HidePage(RectTransform page)
        {
            if (page != null && page.gameObject.activeSelf)
            {
                page.gameObject.SetActive(false);
            }
        }

        private void RefreshStatus()
        {
            NotifyPropertyChanged(nameof(Status));
        }

        private void RefreshRotationValues()
        {
            NotifyPropertyChanged(nameof(RotationX));
            NotifyPropertyChanged(nameof(RotationY));
            NotifyPropertyChanged(nameof(RotationZ));
            NotifyPropertyChanged(nameof(PointerRotation));
        }

        private void RefreshValues()
        {
            NotifyPropertyChanged(nameof(LeaderboardEnabled));
            NotifyPropertyChanged(nameof(TestMode));
            NotifyPropertyChanged(nameof(PerformanceDiagnostics));
            NotifyPropertyChanged(nameof(OverallScale));
            NotifyPropertyChanged(nameof(TextScale));
            NotifyPropertyChanged(nameof(NameScale));
            NotifyPropertyChanged(nameof(AccuracyScale));
            NotifyPropertyChanged(nameof(ScoreScale));
            NotifyPropertyChanged(nameof(RowSpacing));
            NotifyPropertyChanged(nameof(BackgroundWidth));
            NotifyPropertyChanged(nameof(BackgroundHeight));
            NotifyPropertyChanged(nameof(BackgroundOpacity));
            NotifyPropertyChanged(nameof(TextBrightness));
            NotifyPropertyChanged(nameof(Bloom));
            NotifyPropertyChanged(nameof(BloomIntensity));
            NotifyPropertyChanged(nameof(ShowScore));
            NotifyPropertyChanged(nameof(ShowAccuracy));
            NotifyPropertyChanged(nameof(ShowCombo));
            NotifyPropertyChanged(nameof(ShowMistakes));
            NotifyPropertyChanged(nameof(SpectatorX));
            NotifyPropertyChanged(nameof(SpectatorY));
            NotifyPropertyChanged(nameof(SpectatorZ));
            RefreshRotationValues();
        }
    }
}
