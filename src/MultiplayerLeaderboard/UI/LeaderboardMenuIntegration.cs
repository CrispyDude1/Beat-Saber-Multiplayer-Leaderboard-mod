using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.MenuButtons;
using HMUI;
using MultiplayerLeaderboard.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerLeaderboard.UI
{
    /// <summary>
    /// Adds the "Multiplayer Leaderboard" entry to the Mods pane to the left of Beat Saber's main menu.
    /// The button is the same BSML MenuButton used by other mods, so its size, hover state, font and
    /// placement are handled by the game's existing Mods menu.
    /// </summary>
    internal sealed class LeaderboardMenuIntegration : MonoBehaviour
    {
        private const string ButtonText = "Multiplayer Leaderboard";

        private MenuButton _button;
        private MenuButtons _menuButtons;
        private LeaderboardSettingsViewController _gameplayHost;
        private GameplaySetup _gameplaySetup;
        private bool _buttonRegistered;
        private bool _tabRegistered;
        private float _nextAttempt;

        private void Update()
        {
            if (Time.unscaledTime < _nextAttempt)
            {
                return;
            }

            _nextAttempt = Time.unscaledTime + 1f;
            // Pressing OK/Apply in Beat Saber's normal settings restarts the game through
            // MenuTransitionsHelper.RestartGame(). That destroys BSML's main-menu container and its
            // MenuButtons/GameplaySetup singletons while this plugin host survives as DontDestroyOnLoad.
            // Register once per BSML container instance, not once per second: a rebuilt menu changes
            // the singleton reference, which is the cue to restore the button and tab.
            TryRegister();
            TryRegisterGameplayTab();
        }

        private void TryRegister()
        {
            try
            {
                // BSML's Zenject container does not exist until the main menu is up. Checking the
                // scene avoids asking BSML for MenuButtons (and logging) during loading.
                if (!string.Equals(SceneManager.GetActiveScene().name, "MainMenu",
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (_button == null)
                {
                    _button = new MenuButton(ButtonText,
                        "Configure the multiplayer leaderboard, Test Mode and world-space UI.",
                        OpenSettings);
                }

                MenuButtons buttons = MenuButtons.Instance;
                if (buttons == null)
                {
                    return;
                }

                bool rebuilt = !ReferenceEquals(_menuButtons, buttons);
                if (_buttonRegistered && !rebuilt)
                {
                    return;
                }

                // Register once per MenuButtons instance. The old code called RegisterButton every
                // second even when BSML had not rebuilt its menu, which still walked its button list.
                bool firstRegistration = !_buttonRegistered;
                buttons.RegisterButton(_button);
                _buttonRegistered = true;
                _menuButtons = buttons;
                LeaderboardLog.Info(firstRegistration
                    ? "Mods menu button registered: '" + ButtonText + "'."
                    : "Mods menu was rebuilt; button re-registered: '" + ButtonText + "'.");
            }
            catch (InvalidOperationException)
            {
                // BSML is still installing its Zenject bindings; retry on a later frame.
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Mods menu button registration is not ready yet: " + ex.GetType().Name);
            }
        }

        private void TryRegisterGameplayTab()
        {
            try
            {
                if (!string.Equals(SceneManager.GetActiveScene().name, "MainMenu",
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                GameplaySetup setup = GameplaySetup.Instance;
                if (setup == null)
                {
                    return;
                }

                // The same BSML page is reused as a Gameplay Setup tab. It is the Mods panel the
                // pause menu opens, so the full settings page remains reachable while a song is
                // paused. Create the host once and keep it alive for the tab's lifetime.
                bool hostCreated = _gameplayHost == null;
                if (hostCreated)
                {
                    _gameplayHost = BeatSaberUI.CreateViewController<LeaderboardSettingsViewController>();
                    // The tab outlives the main-menu scene, so its binding host must outlive it too.
                    DontDestroyOnLoad(_gameplayHost.gameObject);
                }

                bool rebuilt = !ReferenceEquals(_gameplaySetup, setup);
                if (_tabRegistered && !rebuilt && !hostCreated)
                {
                    return;
                }

                // AddTab rebuilds part of the Gameplay Setup page. Do it only when this is a new
                // setup instance (for example after the game restarts its main menu), not every
                // second for the lifetime of the same menu.
                bool firstRegistration = !_tabRegistered;
                setup.AddTab(ButtonText, "MultiplayerLeaderboard.UI.LeaderboardSettings.bsml",
                             _gameplayHost, MenuType.All);
                _tabRegistered = true;
                _gameplaySetup = setup;
                LeaderboardLog.Info(firstRegistration
                    ? "Gameplay Setup tab registered: '" + ButtonText + "'."
                    : "Gameplay Setup was rebuilt; tab re-registered: '" + ButtonText + "'.");
            }
            catch (Exception ex)
            {
                // BSML is still constructing its gameplay setup view; retry after the next scene settles.
                LeaderboardLog.Debug("Gameplay Setup tab registration is not ready yet: " + ex.GetType().Name);
            }
        }

        private void OpenSettings()
        {
            try
            {
                LeaderboardSettingsFlowCoordinator flow =
                    BeatSaberUI.CreateFlowCoordinator<LeaderboardSettingsFlowCoordinator>();
                BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(
                    flow, null, ViewController.AnimationDirection.Horizontal, false, false);
                LeaderboardLog.Info("Opened the Multiplayer Leaderboard settings menu from the Mods pane.");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("Could not open the Multiplayer Leaderboard settings menu.", ex);
            }
        }

        private void OnDestroy()
        {
            if (_menuButtons != null && _button != null)
            {
                try
                {
                    _menuButtons.UnregisterButton(_button);
                }
                catch
                {
                    // The game is shutting down and BSML's container may already be gone.
                }
            }

            if (_gameplaySetup != null)
            {
                try
                {
                    _gameplaySetup.RemoveTab(ButtonText);
                }
                catch
                {
                    // The game is shutting down and BSML's container may already be gone.
                }
            }

            if (_gameplayHost != null)
            {
                Destroy(_gameplayHost.gameObject);
            }

            _menuButtons = null;
            _gameplaySetup = null;
            _buttonRegistered = false;
            _tabRegistered = false;
        }
    }
}
