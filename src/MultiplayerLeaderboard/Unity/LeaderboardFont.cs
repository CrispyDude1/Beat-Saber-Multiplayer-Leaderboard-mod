using System;
using MultiplayerLeaderboard.Diagnostics;
using TMPro;
using UnityEngine;

namespace MultiplayerLeaderboard.Unity
{
    /// <summary>
    /// Resolves the game's own TMP font for the panel. Self-contained on purpose: the font asset can only
    /// be bound after TMP_Settings exists (well after a mod starts), so lookups are retried at most once a
    /// second. A font without a signed-distance material/atlas is refused, because assigning one makes TMP
    /// fall back to UI/Default and paint solid white quads.
    /// </summary>
    internal static class LeaderboardFont
    {
        private static TMP_FontAsset _resolvedFont;
        private static float _nextAttempt;
        private static int _assetScans;
        private static int _liveScans;
        private static bool _logged;

        public static bool Apply(TextMeshProUGUI label)
        {
            if (label == null)
            {
                return false;
            }

            if (IsUsable(label.font))
            {
                return true;
            }

            TMP_FontAsset font = Resolve();
            if (font == null)
            {
                return false;
            }

            try
            {
                label.font = font;
                if (font.material != null)
                {
                    label.fontSharedMaterial = font.material;
                }

                if (!_logged)
                {
                    _logged = true;
                    LeaderboardLog.Info("Panel font resolved: " + font.name + ".");
                }

                return true;
            }
            catch (Exception ex)
            {
                LeaderboardLog.Warn("Applying the panel font '" + font.name + "' failed: " + ex.GetType().Name);
                return false;
            }
        }

        public static TMP_FontAsset Resolve()
        {
            if (_resolvedFont != null && IsUsable(_resolvedFont))
            {
                return _resolvedFont;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextAttempt)
            {
                return null;
            }

            _nextAttempt = now + 1f;

            try
            {
                if (IsUsable(TMP_Settings.defaultFontAsset))
                {
                    _resolvedFont = TMP_Settings.defaultFontAsset;
                    return _resolvedFont;
                }
            }
            catch
            {
                // TMP settings may not be loaded yet.
            }

            // live labels already use the game font. a few tries is enough, then stop scanning
            if (_liveScans < 8)
            {
                _liveScans++;
                if (TryFontFromLiveText())
                {
                    return _resolvedFont;
                }
            }

            if (_assetScans >= 3)
            {
                return null;
            }

            _assetScans++;

            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null)
                {
                    for (int i = 0; i < fonts.Length; i++)
                    {
                        if (IsUsable(fonts[i]))
                        {
                            _resolvedFont = fonts[i];
                            return _resolvedFont;
                        }
                    }
                }

                LeaderboardLog.Debug("Panel font not available yet (" + (fonts == null ? 0 : fonts.Length) +
                                     " font asset(s)); retrying.");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Warn("Scanning font assets failed: " + ex.GetType().Name);
            }

            return null;
        }

        private static bool TryFontFromLiveText()
        {
            try
            {
                TextMeshProUGUI[] texts = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (texts == null)
                {
                    return false;
                }

                for (int i = 0; i < texts.Length; i++)
                {
                    TextMeshProUGUI text = texts[i];
                    if (text == null)
                    {
                        continue;
                    }

                    TMP_FontAsset font = text.font;
                    if (IsUsable(font))
                    {
                        _resolvedFont = font;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LeaderboardLog.Debug("Live font lookup failed: " + ex.GetType().Name);
            }

            return false;
        }

        private static bool IsUsable(TMP_FontAsset font)
        {
            if (font == null || font.material == null || font.material.mainTexture == null || font.atlasTexture == null)
            {
                return false;
            }

            // A font whose material is UI/Default (or any non-TMP shader) makes TMP draw solid white quads
            // instead of glyphs. Reject it and keep looking for the game's signed-distance font material.
            Shader shader = font.material.shader;
            if (shader == null)
            {
                return false;
            }

            string name = shader.name ?? string.Empty;
            return name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("SDF", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
