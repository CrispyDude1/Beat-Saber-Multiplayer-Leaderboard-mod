using System;
using System.Globalization;
using System.IO;
using System.Text;
using MultiplayerLeaderboard.Diagnostics;
using UnityEngine;

namespace MultiplayerLeaderboard.Unity
{
    /// <summary>
    /// The exact world-space transform and layout values selected with the in-game editor. The position
    /// is the panel's bottom-centre pivot, so restoring it puts the same point back in the same place.
    /// The quaternion is stored in addition to the Euler angles so restoring cannot change orientation.
    /// </summary>
    internal sealed class UiSettings
    {
        internal const float MinOverallScale = 0.5f;
        internal const float MaxOverallScale = 2.0f;
        internal const float MinTextScale = 0.6f;
        internal const float MaxTextScale = 1.6f;
        internal const float MinColumnScale = 0.7f;
        internal const float MaxColumnScale = 1.5f;
        internal const float MinRowSpacing = 0.75f;
        internal const float MaxRowSpacing = 1.6f;
        internal const float MinBackgroundScale = 1.0f;
        internal const float MaxBackgroundScale = 1.5f;
        internal const float MinBackgroundOpacity = 0f;
        internal const float MaxBackgroundOpacity = 1f;
        internal const float MinTextBrightness = 0.5f;
        internal const float MaxTextBrightness = 1.3f;
        internal const float MinBloomIntensity = 0f;
        internal const float MaxBloomIntensity = 10f;
        internal const float DefaultBloomIntensity = 2f;
        internal const float MinSpectatorOffset = -5f;
        internal const float MaxSpectatorOffset = 5f;

        internal bool HasPosition;
        internal Vector3 Position;

        // The spectator panel is attached to the selected player's platform and keeps its rotation
        // locked. Only this position offset is user-controlled; it is expressed in the panel's local
        // right/up/forward axes so it follows the platform without rotating the panel.
        internal bool HasSpectatorOffset;
        internal Vector3 SpectatorOffset;
        internal Quaternion Rotation = Quaternion.identity;
        internal float OverallScale = 1f;
        internal float TextScale = 1f;
        internal float NameScale = 1f;
        internal float AccuracyScale = 1f;
        internal float ScoreScale = 1f;
        internal float RowSpacing = 1f;
        internal float BackgroundWidthScale = 1f;
        internal float BackgroundHeightScale = 1f;
        internal float BackgroundWidth;
        internal float BackgroundHeight;
        internal float BackgroundOpacity = 1f;
        internal float TextBrightness = 1f;

        // Optional soft glow on the panel text. The panel builds its own TMP material copy;
        // the game's shared font material is never modified. Intensity 0 disables the effect
        // even while the toggle is on.
        internal bool Bloom;
        internal float BloomIntensity = DefaultBloomIntensity;

        // Visibility is stored with the same settings so Test Mode survives a restart and turning it
        // off cannot discard the position or appearance values.
        internal bool Enabled = true;
        internal bool TestMode;

        // Display switches only control what the panel draws. Score, combo and mistake tracking keep
        // running while a column is hidden, so re-enabling it needs no restart or re-subscription.
        internal bool ShowScore = true;
        internal bool ShowAccuracy = true;
        internal bool ShowCombo = true;
        internal bool ShowMistakes = true;

        // diagnostic summaries in the mod log. off unless turned on, the sampling used to run during songs
        internal bool PerformanceLogging;

        internal static string SettingsPath
        {
            get
            {
                return Path.Combine(LeaderboardBootstrap.GameRootPath, "UserData", "MultiplayerLeaderboard",
                                    "UISettings.ini");
            }
        }

        internal static string DiagnosticPath
        {
            get
            {
                return Path.Combine(LeaderboardBootstrap.GameRootPath, "Logs", "MultiplayerLeaderboard",
                                    "UISettings.txt");
            }
        }

        internal static UiSettings Load()
        {
            UiSettings settings = new UiSettings();
            string path = SettingsPath;

            try
            {
                if (!File.Exists(path))
                {
                    LeaderboardLog.Info("UI settings file does not exist yet: " + path);
                    return settings;
                }

                string[] lines = File.ReadAllLines(path);
                string section = string.Empty;
                bool hasX = false;
                bool hasY = false;
                bool hasZ = false;
                bool hasSpectatorX = false;
                bool hasSpectatorY = false;
                bool hasSpectatorZ = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) ||
                        line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    int equals = line.IndexOf('=');
                    if (equals <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();

                    if (string.Equals(section, "LeaderboardPosition", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "X", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float x))
                        {
                            settings.Position.x = x;
                            hasX = true;
                        }
                        else if (string.Equals(key, "Y", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float y))
                        {
                            settings.Position.y = y;
                            hasY = true;
                        }
                        else if (string.Equals(key, "Z", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float z))
                        {
                            settings.Position.z = z;
                            hasZ = true;
                        }
                    }
                    else if (string.Equals(section, "SpectatorPosition", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "X", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float spectatorX))
                        {
                            settings.SpectatorOffset.x = Mathf.Clamp(spectatorX, MinSpectatorOffset,
                                                                     MaxSpectatorOffset);
                            hasSpectatorX = true;
                        }
                        else if (string.Equals(key, "Y", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float spectatorY))
                        {
                            settings.SpectatorOffset.y = Mathf.Clamp(spectatorY, MinSpectatorOffset,
                                                                     MaxSpectatorOffset);
                            hasSpectatorY = true;
                        }
                        else if (string.Equals(key, "Z", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float spectatorZ))
                        {
                            settings.SpectatorOffset.z = Mathf.Clamp(spectatorZ, MinSpectatorOffset,
                                                                     MaxSpectatorOffset);
                            hasSpectatorZ = true;
                        }
                    }
                    else if (string.Equals(section, "LeaderboardRotation", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "QX", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float qx))
                        {
                            settings.Rotation.x = qx;
                        }
                        else if (string.Equals(key, "QY", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float qy))
                        {
                            settings.Rotation.y = qy;
                        }
                        else if (string.Equals(key, "QZ", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float qz))
                        {
                            settings.Rotation.z = qz;
                        }
                        else if (string.Equals(key, "QW", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float qw))
                        {
                            settings.Rotation.w = qw;
                        }
                    }
                    else if (string.Equals(section, "LeaderboardScale", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "Overall", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float overall))
                        {
                            settings.OverallScale = Mathf.Clamp(overall, MinOverallScale, MaxOverallScale);
                        }
                        else if (string.Equals(key, "Text", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float text))
                        {
                            settings.TextScale = Mathf.Clamp(text, MinTextScale, MaxTextScale);
                        }
                        else if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float name))
                        {
                            settings.NameScale = Mathf.Clamp(name, MinColumnScale, MaxColumnScale);
                        }
                        else if (string.Equals(key, "Accuracy", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float accuracy))
                        {
                            settings.AccuracyScale = Mathf.Clamp(accuracy, MinColumnScale, MaxColumnScale);
                        }
                        else if (string.Equals(key, "Score", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float score))
                        {
                            settings.ScoreScale = Mathf.Clamp(score, MinColumnScale, MaxColumnScale);
                        }
                        else if (string.Equals(key, "RowSpacing", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float spacing))
                        {
                            settings.RowSpacing = Mathf.Clamp(spacing, MinRowSpacing, MaxRowSpacing);
                        }
                    }
                    else if (string.Equals(section, "Background", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "Width", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float width))
                        {
                            settings.BackgroundWidth = width;
                        }
                        else if (string.Equals(key, "Height", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float height))
                        {
                            settings.BackgroundHeight = height;
                        }
                        else if (string.Equals(key, "WidthScale", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float widthScale))
                        {
                            settings.BackgroundWidthScale = Mathf.Clamp(widthScale, MinBackgroundScale,
                                                                        MaxBackgroundScale);
                        }
                        else if (string.Equals(key, "HeightScale", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float heightScale))
                        {
                            settings.BackgroundHeightScale = Mathf.Clamp(heightScale, MinBackgroundScale,
                                                                         MaxBackgroundScale);
                        }
                    }
                    else if (string.Equals(section, "Appearance", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "BackgroundOpacity", StringComparison.OrdinalIgnoreCase) &&
                            TryFloat(value, out float opacity))
                        {
                            settings.BackgroundOpacity = Mathf.Clamp(opacity, MinBackgroundOpacity,
                                                                      MaxBackgroundOpacity);
                        }
                        else if (string.Equals(key, "TextBrightness", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float brightness))
                        {
                            settings.TextBrightness = Mathf.Clamp(brightness, MinTextBrightness,
                                                                  MaxTextBrightness);
                        }
                        else if (string.Equals(key, "Bloom", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Bloom = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                                             value == "1";
                        }
                        else if (string.Equals(key, "BloomIntensity", StringComparison.OrdinalIgnoreCase) &&
                                 TryFloat(value, out float bloomIntensity))
                        {
                            settings.BloomIntensity = Mathf.Clamp(bloomIntensity, MinBloomIntensity,
                                                                  MaxBloomIntensity);
                        }
                    }
                    else if (string.Equals(section, "DisplayedInformation", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "Score", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ShowScore = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                                  value != "0";
                        }
                        else if (string.Equals(key, "Accuracy", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ShowAccuracy = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                                    value != "0";
                        }
                        else if (string.Equals(key, "Combo", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ShowCombo = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                                 value != "0";
                        }
                        else if (string.Equals(key, "Mistakes", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ShowMistakes = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                                    value != "0";
                        }
                    }
                    else if (string.Equals(section, "Visibility", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "Enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Enabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                               value != "0";
                        }
                        else if (string.Equals(key, "TestMode", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.TestMode = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                                                value == "1";
                        }
                        else if (string.Equals(key, "PerformanceLogging", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.PerformanceLogging = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                                                          value != "0";
                        }
                    }
                }

                settings.HasSpectatorOffset = hasSpectatorX && hasSpectatorY && hasSpectatorZ;
                if (!settings.HasSpectatorOffset)
                {
                    settings.SpectatorOffset = Vector3.zero;
                }

                settings.HasPosition = hasX && hasY && hasZ;
                if (!settings.HasPosition)
                {
                    settings.Position = Vector3.zero;
                    settings.Rotation = Quaternion.identity;
                }

                if (settings.Rotation.x == 0f && settings.Rotation.y == 0f &&
                    settings.Rotation.z == 0f && settings.Rotation.w == 0f)
                {
                    settings.Rotation = Quaternion.identity;
                }
                else
                {
                    settings.Rotation = Quaternion.Normalize(settings.Rotation);
                }

                LeaderboardLog.Info("UI settings loaded from " + path + ": " +
                                    (settings.HasPosition ? "saved position " + Format(settings.Position) +
                                     ", rotation " + Format(settings.Rotation) : "no saved position") +
                                    ", spectator offset " + (settings.HasSpectatorOffset ? Format(settings.SpectatorOffset) : "none") +
                                    ", overall " + F(settings.OverallScale) + ", text " + F(settings.TextScale) +
                                    ", name " + F(settings.NameScale) + ", accuracy " + F(settings.AccuracyScale) +
                                    ", score " + F(settings.ScoreScale) + ", row spacing " + F(settings.RowSpacing) +
                                    ", bg opacity " + F(settings.BackgroundOpacity) +
                                    ", text brightness " + F(settings.TextBrightness) +
                                    ", bloom " + (settings.Bloom ? "on" : "off") +
                                    " at intensity " + F(settings.BloomIntensity) +
                                    ", columns score/accuracy/combo/mistakes " +
                                    (settings.ShowScore ? "on" : "off") + "/" +
                                    (settings.ShowAccuracy ? "on" : "off") + "/" +
                                    (settings.ShowCombo ? "on" : "off") + "/" +
                                    (settings.ShowMistakes ? "on" : "off") +
                                    ", test mode " + (settings.TestMode ? "on" : "off") +
                                    ", performance diagnostics " + (settings.PerformanceLogging ? "on" : "off") + ".");
            }
            catch (Exception ex)
            {
                LeaderboardLog.Warn("Could not read UI settings from " + path + ": " + ex.GetType().Name +
                                    ". Defaults will be used.");
            }

            return settings;
        }

        internal void Save(float backgroundWidth, float backgroundHeight)
        {
            BackgroundWidth = backgroundWidth;
            BackgroundHeight = backgroundHeight;

            string text = ToIni(backgroundWidth, backgroundHeight);
            string path = SettingsPath;
            string diagnostic = DiagnosticPath;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporary = path + ".tmp";
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);

                string diagnosticDirectory = Path.GetDirectoryName(diagnostic);
                if (!string.IsNullOrEmpty(diagnosticDirectory))
                {
                    Directory.CreateDirectory(diagnosticDirectory);
                }

                File.WriteAllText(diagnostic, text, new UTF8Encoding(false));

                LeaderboardLog.Info("Saved UI settings to " + path + " (diagnostic copy: " + diagnostic + ").");
                LeaderboardLog.Info("Saved values:" + Environment.NewLine + text.TrimEnd());
            }
            catch (Exception ex)
            {
                LeaderboardLog.Error("Could not save UI settings to " + path + ".", ex);
            }
        }

        internal string ToIni(float backgroundWidth, float backgroundHeight)
        {
            Vector3 euler = Rotation.eulerAngles;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("; Multiplayer Leaderboard in-game UI settings.");
            builder.AppendLine("; Position is the panel's bottom-centre pivot in world space.");
            builder.AppendLine("; Rotation is saved twice: Euler angles for reading, quaternion for exact restore.");
            builder.AppendLine();
            if (HasSpectatorOffset)
            {
                builder.AppendLine("[SpectatorPosition]");
                builder.AppendLine("X=" + F(SpectatorOffset.x));
                builder.AppendLine("Y=" + F(SpectatorOffset.y));
                builder.AppendLine("Z=" + F(SpectatorOffset.z));
                builder.AppendLine();
            }

            if (HasPosition)
            {
                builder.AppendLine("[LeaderboardPosition]");
                builder.AppendLine("X=" + F(Position.x));
                builder.AppendLine("Y=" + F(Position.y));
                builder.AppendLine("Z=" + F(Position.z));
                builder.AppendLine();
                builder.AppendLine("[LeaderboardRotation]");
                builder.AppendLine("X=" + F(euler.x));
                builder.AppendLine("Y=" + F(euler.y));
                builder.AppendLine("Z=" + F(euler.z));
                builder.AppendLine("QX=" + F(Rotation.x));
                builder.AppendLine("QY=" + F(Rotation.y));
                builder.AppendLine("QZ=" + F(Rotation.z));
                builder.AppendLine("QW=" + F(Rotation.w));
                builder.AppendLine();
            }

            builder.AppendLine("[LeaderboardScale]");
            builder.AppendLine("Overall=" + F(OverallScale));
            builder.AppendLine("Text=" + F(TextScale));
            builder.AppendLine("Name=" + F(NameScale));
            builder.AppendLine("Accuracy=" + F(AccuracyScale));
            builder.AppendLine("Score=" + F(ScoreScale));
            builder.AppendLine("RowSpacing=" + F(RowSpacing));
            builder.AppendLine();
            builder.AppendLine("[Background]");
            builder.AppendLine("Width=" + F(backgroundWidth));
            builder.AppendLine("Height=" + F(backgroundHeight));
            builder.AppendLine("WidthScale=" + F(BackgroundWidthScale));
            builder.AppendLine("HeightScale=" + F(BackgroundHeightScale));
            builder.AppendLine();
            builder.AppendLine("[Appearance]");
            builder.AppendLine("BackgroundOpacity=" + F(BackgroundOpacity));
            builder.AppendLine("TextBrightness=" + F(TextBrightness));
            builder.AppendLine("Bloom=" + (Bloom ? "true" : "false"));
            builder.AppendLine("BloomIntensity=" + F(BloomIntensity));
            builder.AppendLine();
            builder.AppendLine("[DisplayedInformation]");
            builder.AppendLine("Score=" + (ShowScore ? "true" : "false"));
            builder.AppendLine("Accuracy=" + (ShowAccuracy ? "true" : "false"));
            builder.AppendLine("Combo=" + (ShowCombo ? "true" : "false"));
            builder.AppendLine("Mistakes=" + (ShowMistakes ? "true" : "false"));
            builder.AppendLine();
            builder.AppendLine("[Visibility]");
                builder.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
                builder.AppendLine("TestMode=" + (TestMode ? "true" : "false"));
                builder.AppendLine("PerformanceLogging=" + (PerformanceLogging ? "true" : "false"));
                builder.AppendLine();
            builder.AppendLine("[Meta]");
            builder.AppendLine("SavedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        internal string Describe()
        {
            return "pos " + Format(Position) + " rot " + Format(Rotation) +
                   " spectator offset " + (HasSpectatorOffset ? Format(SpectatorOffset) : "none") +
                   " overall " + F(OverallScale) +
                   " text " + F(TextScale) + " name " + F(NameScale) + " accuracy " + F(AccuracyScale) +
                   " score " + F(ScoreScale) + " rows " + F(RowSpacing) + " bg " +
                   F(BackgroundWidthScale) + " x " + F(BackgroundHeightScale) +
                   " opacity " + F(BackgroundOpacity) + " brightness " + F(TextBrightness) +
                   " bloom " + (Bloom ? "on" : "off") + " intensity " + F(BloomIntensity) +
                   " columns " + (ShowScore ? "S" : "-") + (ShowAccuracy ? "A" : "-") +
                   (ShowCombo ? "C" : "-") + (ShowMistakes ? "M" : "-") +
                   " diagnostics " + (PerformanceLogging ? "on" : "off");
        }

        private static bool TryFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string F(float value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static string Format(Vector3 value)
        {
            return "(" + F(value.x) + ", " + F(value.y) + ", " + F(value.z) + ")";
        }

        private static string Format(Quaternion value)
        {
            return "(" + F(value.x) + ", " + F(value.y) + ", " + F(value.z) + ", " + F(value.w) + ")";
        }
    }
}
