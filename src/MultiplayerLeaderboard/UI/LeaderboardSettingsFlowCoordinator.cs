using BeatSaberMarkupLanguage;
using HMUI;

namespace MultiplayerLeaderboard.UI
{
    /// <summary>
    /// Presents the leaderboard settings page as a normal Beat Saber menu flow, including the game's
    /// back button, rather than a separate desktop window or floating panel.
    /// </summary>
    internal sealed class LeaderboardSettingsFlowCoordinator : FlowCoordinator
    {
        private LeaderboardSettingsViewController _settings;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy,
                                            bool screenSystemEnabling)
        {
            if (!firstActivation)
            {
                return;
            }

            _settings = BeatSaberUI.CreateViewController<LeaderboardSettingsViewController>();

            // Match BSML's own settings pages: a 110-unit-wide page anchored to the same vertical
            // area as every other mod page. Without this the scroll view is positioned below the
            // visible menu and only its first line can be seen.
            UnityEngine.RectTransform rect = _settings.GetComponent<UnityEngine.RectTransform>();
            rect.sizeDelta = new UnityEngine.Vector2(110f, 0f);
            rect.anchorMin = new UnityEngine.Vector2(0.5f, 0f);
            rect.anchorMax = new UnityEngine.Vector2(0.5f, 1f);

            SetTitle("Multiplayer Leaderboard", ViewController.AnimationType.In);
            showBackButton = true;
            ProvideInitialViewControllers(_settings, null, null, null, null);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(
                this, null, ViewController.AnimationDirection.Horizontal);
        }
    }
}
