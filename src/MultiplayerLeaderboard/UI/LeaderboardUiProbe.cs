using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BeatSaberMarkupLanguage;
using HMUI;
using MultiplayerLeaderboard.Diagnostics;
using MultiplayerLeaderboard.Gameplay;
using MultiplayerLeaderboard.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MultiplayerLeaderboard.UI
{
    /// <summary>
    /// Opt-in UI probe used while validating the settings menu. It behaves like the other mods' menu
    /// automation: it activates the real Mods-pane action, then presses the actual category and Back
    /// buttons rather than calling page methods directly. A marker file must exist in
    /// UserData/MultiplayerLeaderboard/UiProbe, so ordinary launches never run this code.
    /// </summary>
    internal sealed class LeaderboardUiProbe : MonoBehaviour
    {
        private const string ProbeDirectoryName = "UiProbe";
        private const string MarkerFileName = "run.flag";
        private const string LogFileName = "ui-probe.log";
        private const float PageHoldSeconds = 3f;

        private static readonly string[] PageLabels =
        {
            "Positioning", "Rotation", "Scaling", "Appearance", "Text Settings", "Displayed Information"
        };

        private static readonly string[] PageFields =
        {
            "_pagePositioning", "_pageRotation", "_pageScaling", "_pageAppearance", "_pageText", "_pageDisplay"
        };

        private static readonly string[] PageSlugs =
        {
            "positioning", "rotation", "scaling", "appearance", "text", "display"
        };

        private Coroutine _run;

        internal static string ProbeRoot
        {
            get
            {
                return Path.Combine(LeaderboardBootstrap.GameRootPath, "UserData",
                                    "MultiplayerLeaderboard", ProbeDirectoryName);
            }
        }

        internal static string MarkerPath
        {
            get { return Path.Combine(ProbeRoot, MarkerFileName); }
        }

        internal static bool IsRequested
        {
            get
            {
                try
                {
                    return File.Exists(MarkerPath);
                }
                catch
                {
                    return false;
                }
            }
        }

        private void Start()
        {
            if (!IsRequested)
            {
                Destroy(this);
                return;
            }

            try
            {
                Directory.CreateDirectory(ProbeRoot);
            }
            catch
            {
                // The game root is normally writable; a failure here only disables the probe log.
            }

            Write("requested; waiting for the main menu");
            _run = StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }
        }

        private IEnumerator Run()
        {
            MainMenuViewController menu = null;
            for (float elapsed = 0f; elapsed < 90f && menu == null; elapsed += 0.5f)
            {
                menu = FindActive<MainMenuViewController>();
                if (menu == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }

            if (menu == null)
            {
                Write("timeout: main menu");
                yield break;
            }

            Write("main menu active");
            yield return new WaitForSeconds(2f);

            bool opened = false;
            try
            {
                LeaderboardMenuIntegration integration = FindActive<LeaderboardMenuIntegration>();
                if (integration != null)
                {
                    MethodInfo open = typeof(LeaderboardMenuIntegration).GetMethod(
                        "OpenSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (open != null)
                    {
                        open.Invoke(integration, null);
                        opened = true;
                        Write("pressed the Mods-pane settings action");
                    }
                }
            }
            catch (Exception ex)
            {
                Write("Mods-pane action failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (!opened)
            {
                try
                {
                    LeaderboardSettingsFlowCoordinator flow =
                        BeatSaberUI.CreateFlowCoordinator<LeaderboardSettingsFlowCoordinator>();
                    BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(
                        flow, null, ViewController.AnimationDirection.Horizontal, false, false);
                    Write("opened the settings flow directly");
                }
                catch (Exception ex)
                {
                    Write("direct flow presentation failed: " + ex.GetType().Name + ": " + ex.Message);
                    yield break;
                }
            }

            yield return new WaitForSeconds(3f);

            LeaderboardSettingsViewController settings = null;
            for (float elapsed = 0f; elapsed < 15f && settings == null; elapsed += 0.5f)
            {
                settings = FindSettings();
                if (settings == null || !settings.gameObject.activeInHierarchy)
                {
                    settings = null;
                    yield return new WaitForSeconds(0.5f);
                }
            }

            if (settings == null)
            {
                Write("timeout: settings view controller");
                yield break;
            }

            RectTransform main = GetRect(settings, "_pageMain");
            Write("settings active; main page injected=" + (main != null));
            Write("settings layout: " + DescribeRect(settings.transform as RectTransform) +
                  "; scale=" + DescribeScale(settings.transform));
            Write("injected pages: " + DescribeInjectedPages(settings));
            Write("visible pages: " + DescribeVisiblePages(settings));

            yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < PageLabels.Length; i++)
            {
                string label = PageLabels[i];
                string field = PageFields[i];
                string slug = PageSlugs[i];

                Button category = FindActiveButton(settings.transform, label);
                Write("button '" + label + "' found=" + (category != null));
                if (category == null)
                {
                    continue;
                }

                category.onClick.Invoke();
                yield return new WaitForSeconds(1.2f);

                RectTransform page = GetRect(settings, field);
                Write("page " + slug + ": injected=" + (page != null) +
                      " visible=" + (page != null && page.gameObject.activeInHierarchy) +
                      " mainHidden=" + (main == null || !main.gameObject.activeInHierarchy) +
                      " layout=" + DescribeRect(page) +
                      " visibleSet=" + DescribeVisiblePages(settings));

                yield return new WaitForSeconds(PageHoldSeconds);

                Button back = FindActiveButton(page != null ? page : (Transform)settings.transform,
                                                "Back to Settings");
                Write("back button on " + slug + " found=" + (back != null));
                if (back != null)
                {
                    back.onClick.Invoke();
                    yield return new WaitForSeconds(0.8f);
                    Write("back from " + slug + ": mainVisible=" +
                          (main != null && main.gameObject.activeInHierarchy) +
                          " visibleSet=" + DescribeVisiblePages(settings));
                }
            }

            RunBloomSelfTest(settings);
            RunResetSelfTest(settings);
            RunRankFlashSelfTest();
            RestoreProbeSettings();

            Write("done");
        }

        private static void RunBloomSelfTest(LeaderboardSettingsViewController settings)
        {
            LeaderboardRuntime runtime = FindActive<LeaderboardRuntime>();
            WorldLeaderboardPanel panel = runtime != null ? runtime.Panel : null;
            if (settings == null || panel == null)
            {
                Write("bloom test: skipped (settings or Test Mode panel unavailable)");
                return;
            }

            PropertyInfo bloom = typeof(LeaderboardSettingsViewController).GetProperty(
                "Bloom", BindingFlags.Instance | BindingFlags.Public);
            if (bloom == null)
            {
                Write("bloom test: Bloom property missing");
                return;
            }

            Write("bloom test: initial=" + bloom.GetValue(settings, null) + ", saved=" + ReadBloomFlag());

            bloom.SetValue(settings, true, null);
            Write("bloom test: enabled panel=" + panel.Bloom +
                  ", material=" + (GetMember(panel, "_bloomMaterial") != null) +
                  ", mode=" + GetMember(panel, "_bloomMode") +
                  ", labels=" + CountBloomLabels(panel, true) + "/" + CountBloomLabels(panel, false) +
                  ", saved=" + ReadBloomFlag());

            bloom.SetValue(settings, false, null);
            Write("bloom test: disabled panel=" + panel.Bloom +
                  ", bloomLabels=" + CountBloomLabels(panel, true) +
                  ", saved=" + ReadBloomFlag());

            bloom.SetValue(settings, true, null);
            Write("bloom test: re-enabled panel=" + panel.Bloom +
                  ", mode=" + GetMember(panel, "_bloomMode") +
                  ", saved=" + ReadBloomFlag());

            PropertyInfo intensity = typeof(LeaderboardSettingsViewController).GetProperty(
                "BloomIntensity", BindingFlags.Instance | BindingFlags.Public);
            if (intensity != null)
            {
                string[] values = { "0", "1", "5", "10" };
                for (int i = 0; i < values.Length; i++)
                {
                    intensity.SetValue(settings, values[i], null);
                    Write("bloom intensity " + values[i] + ": panel=" +
                          panel.BloomIntensity.ToString("0.##") +
                          ", material=" + (GetMember(panel, "_bloomMaterial") != null) +
                          ", mode=" + GetMember(panel, "_bloomMode") +
                          ", bloomLabels=" + CountBloomLabels(panel, true) + "/" +
                          CountBloomLabels(panel, false) + ", saved=" + ReadBloomFlag());
                    if (values[i] == "1" || values[i] == "5" || values[i] == "10")
                    {
                        CaptureBloomSample(panel, null,
                                           "bloom-sample-" + values[i] + ".png",
                                           "BLOOM " + values[i]);
                    }

                    if (values[i] == "10")
                    {
                        LogBloomMaterial(panel);
                        LogTmpShaders();
                    }
                }

                // Leave a visible but reasonable value on for the restart check.
                intensity.SetValue(settings, "5", null);
            }
        }

        private static void LogTmpShaders()
        {
            Shader[] shaders = Resources.FindObjectsOfTypeAll<Shader>();
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < shaders.Length; i++)
            {
                Shader shader = shaders[i];
                if (shader == null || string.IsNullOrEmpty(shader.name))
                {
                    continue;
                }

                if (shader.name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    shader.name.IndexOf("SDF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (names.Length > 0) names.Append(" | ");
                    names.Append(shader.name);
                }
            }

            Write("TMP shaders: " + (names.Length > 0 ? names.ToString() : "none"));
        }

        private static void ConfigureProbeBloom(Material material, float intensity)
        {
            float k = Mathf.Clamp01(intensity / 10f);
            if (material.HasProperty("_OutlineColor"))
            {
                material.SetColor("_OutlineColor", new Color(0.82f, 0.92f, 1f, Mathf.Lerp(0.45f, 1f, k)));
                material.SetFloat("_OutlineWidth", Mathf.Lerp(0.12f, 1.05f, k));
                material.SetFloat("_OutlineSoftness", Mathf.Lerp(0.20f, 0.62f, k));
                material.EnableKeyword("OUTLINE_ON");
            }
            if (material.HasProperty("_UnderlayColor"))
            {
                material.SetColor("_UnderlayColor", new Color(0.82f, 0.92f, 1f, Mathf.Lerp(0.30f, 0.95f, k)));
                material.SetFloat("_UnderlayOffsetX", 0f);
                material.SetFloat("_UnderlayOffsetY", 0f);
                material.SetFloat("_UnderlayDilate", Mathf.Lerp(0.08f, 0.90f, k));
                material.SetFloat("_UnderlaySoftness", Mathf.Lerp(0.35f, 0.80f, k));
                material.EnableKeyword("UNDERLAY_ON");
                material.DisableKeyword("UNDERLAY_INNER");
            }
        }

        private static void LogBloomMaterial(WorldLeaderboardPanel panel)
        {
            Material material = GetMember(panel, "_bloomMaterial") as Material;
            if (material == null)
            {
                Write("bloom material: null");
                return;
            }

            Write("bloom material: shader=" + material.shader.name +
                  ", passes=" + material.passCount +
                  ", keywords=" + string.Join(",", material.shaderKeywords) +
                  ", outline=" + MaterialFloat(material, "_OutlineWidth") +
                  ", glow=" + MaterialFloat(material, "_GlowOuter") +
                  ", underlay=" + MaterialFloat(material, "_UnderlayDilate"));

            System.Collections.IList labels = GetMember(panel, "_texts") as System.Collections.IList;
            if (labels != null && labels.Count > 0)
            {
                TextMeshProUGUI label = labels[0] as TextMeshProUGUI;
                if (label != null)
                {
                    Material rendered = label.canvasRenderer.GetMaterial();
                    Write("bloom renderer: labelMaterial=" +
                          (label.fontSharedMaterial != null ? label.fontSharedMaterial.name : "null") +
                          ", canvasMaterial=" + (rendered != null ? rendered.name : "null") +
                          ", labelColor=" + label.color);
                }
            }
        }

        private static string MaterialFloat(Material material, string property)
        {
            return material != null && material.HasProperty(property)
                ? material.GetFloat(property).ToString("0.###")
                : "n/a";
        }

        private static void CaptureBloomSample(WorldLeaderboardPanel panel, Material bloomOverride = null,
                                                string fileName = "bloom-sample.png",
                                                string bloomText = "BLOOM 10")
        {
            Material bloom = bloomOverride != null
                ? bloomOverride
                : GetMember(panel, "_bloomMaterial") as Material;
            TMP_FontAsset font = LeaderboardFont.Resolve();
            if (bloom == null || font == null || font.material == null)
            {
                Write("bloom sample: skipped (material or font unavailable)");
                return;
            }

            GameObject cameraObject = new GameObject("BloomProbeCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.006f, 0.010f, 0.018f, 1f);
            camera.cullingMask = (1 << 30) | (1 << 5);
            camera.enabled = false;
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;
            RenderTexture target = new RenderTexture(900, 260, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;

            GameObject canvasObject = new GameObject("BloomProbeCanvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 30;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 260f);

            GameObject backdrop = new GameObject("Backdrop", typeof(RectTransform));
            backdrop.layer = 30;
            RectTransform backdropRect = (RectTransform)backdrop.transform;
            backdropRect.SetParent(canvasRect, false);
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0.012f, 0.018f, 0.030f, 1f);
            backdropImage.raycastTarget = false;

            TextMeshProUGUI normal = CreateSampleText(canvasRect, "Normal", 24f, 24f, "NORMAL",
                                                       font, font.material, Color.white);
            TextMeshProUGUI glowing = CreateSampleText(canvasRect, "Bloom", 24f, 132f, bloomText,
                                                        font, bloom, Color.white);
            Canvas.ForceUpdateCanvases();
            normal.ForceMeshUpdate(true, true);
            glowing.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture.active = target;
            Texture2D image = new Texture2D(900, 260, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 900f, 260f), 0, 0);
            image.Apply();
            RenderTexture.active = null;

            try
            {
                string path = Path.Combine(ProbeRoot, fileName);
                File.WriteAllBytes(path, image.EncodeToPNG());
                Write("bloom sample: " + path + " bytes=" + new FileInfo(path).Length);
            }
            catch (Exception ex)
            {
                Write("bloom sample failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            UnityEngine.Object.Destroy(canvasObject);
            UnityEngine.Object.Destroy(cameraObject);
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }

        private static TextMeshProUGUI CreateSampleText(RectTransform parent, string name, float x, float y,
                                                        string value, TMP_FontAsset font, Material material,
                                                        Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.layer = 30;
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(850f, 90f);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 64f;
            text.color = color;
            text.text = value;
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
            text.richText = false;
            text.raycastTarget = false;
            if (material != null)
            {
                text.fontSharedMaterial = material;
            }

            return text;
        }

        private static int CountBloomLabels(WorldLeaderboardPanel panel, bool bloomOnly)
        {
            System.Collections.IList labels = GetMember(panel, "_texts") as System.Collections.IList;
            if (labels == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                TextMeshProUGUI label = labels[i] as TextMeshProUGUI;
                if (label == null)
                {
                    continue;
                }

                bool hasBloom = label.fontSharedMaterial != null &&
                                string.Equals(label.fontSharedMaterial.name,
                                              "MultiplayerLeaderboardBloom", StringComparison.Ordinal);
                if (!bloomOnly || hasBloom)
                {
                    count++;
                }
            }

            return count;
        }

        private static string ReadBloomFlag()
        {
            try
            {
                string path = MultiplayerLeaderboard.Unity.UiSettings.SettingsPath;
                if (!File.Exists(path))
                {
                    return "missing-file";
                }

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("Bloom=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("Bloom=".Length);
                    }
                }

                return "missing-key";
            }
            catch (Exception ex)
            {
                return "error:" + ex.GetType().Name;
            }
        }

        private sealed class ProbeRowLabels
        {
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Score;
            public TextMeshProUGUI Accuracy;
            public TextMeshProUGUI Combo;
            public TextMeshProUGUI Mistakes;
        }

        private static void RunResetSelfTest(LeaderboardSettingsViewController settings)
        {
            if (settings == null)
            {
                Write("reset test: skipped (settings unavailable)");
                return;
            }

            Write("reset test: begin");
            CheckFloatReset(settings, "OverallScale", 1.45f, "ResetOverallScale", 1f,
                            "LeaderboardScale", "Overall");
            CheckFloatReset(settings, "TextScale", 1.35f, "ResetTextScale", 1f,
                            "LeaderboardScale", "Text");
            CheckFloatReset(settings, "NameScale", 1.3f, "ResetNameScale", 1f,
                            "LeaderboardScale", "Name");
            CheckFloatReset(settings, "AccuracyScale", 1.3f, "ResetAccuracyScale", 1f,
                            "LeaderboardScale", "Accuracy");
            CheckFloatReset(settings, "ScoreScale", 1.3f, "ResetScoreScale", 1f,
                            "LeaderboardScale", "Score");
            CheckFloatReset(settings, "RowSpacing", 1.35f, "ResetRowSpacing", 1f,
                            "LeaderboardScale", "RowSpacing");
            CheckFloatReset(settings, "BackgroundWidth", 1.3f, "ResetBackgroundWidth", 1f,
                            "Background", "WidthScale");
            CheckFloatReset(settings, "BackgroundHeight", 1.3f, "ResetBackgroundHeight", 1f,
                            "Background", "HeightScale");
            CheckFloatReset(settings, "BackgroundOpacity", 0.35f, "ResetBackgroundOpacity", 1f,
                            "Appearance", "BackgroundOpacity");
            CheckFloatReset(settings, "TextBrightness", 1.2f, "ResetTextBrightness", 1f,
                            "Appearance", "TextBrightness");
            CheckFloatReset(settings, "RotationX", 45f, "ResetRotationX", 0f,
                            "LeaderboardRotation", "X");
            CheckFloatReset(settings, "RotationY", 35f, "ResetRotationY", 0f,
                            "LeaderboardRotation", "Y");
            CheckFloatReset(settings, "RotationZ", 25f, "ResetRotationZ", 0f,
                            "LeaderboardRotation", "Z");
            CheckBloomIntensityReset(settings);
            Write("reset test: complete");
        }

        private static void CheckFloatReset(LeaderboardSettingsViewController settings,
                                            string propertyName, float probeValue, string actionName,
                                            float expected, string section, string key)
        {
            PropertyInfo property = typeof(LeaderboardSettingsViewController).GetProperty(
                propertyName, BindingFlags.Instance | BindingFlags.Public);
            MethodInfo action = typeof(LeaderboardSettingsViewController).GetMethod(
                actionName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null || action == null)
            {
                Write("reset " + propertyName + ": FAIL (member missing)");
                return;
            }

            property.SetValue(settings, probeValue, null);
            float applied = Convert.ToSingle(property.GetValue(settings, null), CultureInfo.InvariantCulture);
            action.Invoke(settings, null);
            float restored = Convert.ToSingle(property.GetValue(settings, null), CultureInfo.InvariantCulture);
            float saved = ReadSavedFloat(section, key);
            bool valueOk = Mathf.Abs(applied - probeValue) < 0.01f &&
                           Mathf.Abs(restored - expected) < 0.01f;
            bool savedOk = !float.IsNaN(saved) && Mathf.Abs(saved - expected) < 0.01f;
            Write("reset " + propertyName + ": " + (valueOk && savedOk ? "PASS" : "FAIL") +
                  " applied=" + applied.ToString("0.##", CultureInfo.InvariantCulture) +
                  " restored=" + restored.ToString("0.##", CultureInfo.InvariantCulture) +
                  " expected=" + expected.ToString("0.##", CultureInfo.InvariantCulture) +
                  " saved=" + (float.IsNaN(saved) ? "missing" : saved.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        private static void CheckBloomIntensityReset(LeaderboardSettingsViewController settings)
        {
            PropertyInfo property = typeof(LeaderboardSettingsViewController).GetProperty(
                "BloomIntensity", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo action = typeof(LeaderboardSettingsViewController).GetMethod(
                "ResetBloomIntensity", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null || action == null)
            {
                Write("reset BloomIntensity: FAIL (member missing)");
                return;
            }

            property.SetValue(settings, "7", null);
            string applied = property.GetValue(settings, null) as string;
            action.Invoke(settings, null);
            string restored = property.GetValue(settings, null) as string;
            float saved = ReadSavedFloat("Appearance", "BloomIntensity");
            bool valueOk = applied == "7" && restored == "2";
            bool savedOk = !float.IsNaN(saved) && Mathf.Abs(saved - 2f) < 0.01f;
            Write("reset BloomIntensity: " + (valueOk && savedOk ? "PASS" : "FAIL") +
                  " applied=" + applied + " restored=" + restored + " expected=2 saved=" +
                  (float.IsNaN(saved) ? "missing" : saved.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        // The old five-label spectator card was retired. The spectator platform now uses the same
        // WorldLeaderboardPanel class and the same row buffer as the normal multiplayer leaderboard.
        // The probe therefore has no separate spectator-card test to run.

        private static void RestoreProbeSettings()
        {
            LeaderboardRuntime runtime = FindActive<LeaderboardRuntime>();
            if (runtime == null)
            {
                return;
            }

            runtime.SetBloom(false);
            runtime.SetBloomIntensity(UiSettings.DefaultBloomIntensity);
            runtime.SetTestMode(false);
            runtime.SaveSettings();
            Write("probe settings restored: bloom off, intensity default, Test Mode off");
        }

        private static float ReadSavedFloat(string section, string key)
        {
            try
            {
                string current = string.Empty;
                string[] lines = File.ReadAllLines(UiSettings.SettingsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) ||
                        line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("[", StringComparison.Ordinal) &&
                        line.EndsWith("]", StringComparison.Ordinal))
                    {
                        current = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int equals = line.IndexOf('=');
                    if (equals <= 0 ||
                        !string.Equals(line.Substring(0, equals).Trim(), key,
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    float value;
                    if (float.TryParse(line.Substring(equals + 1).Trim(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // Missing or unreadable settings are reported as a failed save by the caller.
            }

            return float.NaN;
        }

        private static void RunRankFlashSelfTest()
        {
            LeaderboardRuntime runtime = FindActive<LeaderboardRuntime>();
            WorldLeaderboardPanel panel = runtime != null ? runtime.Panel : null;
            if (panel == null)
            {
                Write("flash test: skipped (Test Mode panel unavailable)");
                return;
            }

            MethodInfo render = typeof(WorldLeaderboardPanel).GetMethod(
                "Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo nameColorFor = typeof(WorldLeaderboardPanel).GetMethod(
                "NameColorFor", BindingFlags.Instance | BindingFlags.NonPublic);
            if (render == null || nameColorFor == null)
            {
                Write("flash test: panel methods not found");
                return;
            }

            PlayerRow a = CreateTestRow("probe-a", "ProbeA", 1);
            PlayerRow b = CreateTestRow("probe-b", "ProbeB", 2);
            List<PlayerRow> rows = new List<PlayerRow> { a, b };

            // EnsureRows creates one row per render, so a few passes make both rows available.
            for (int i = 0; i < 3; i++)
            {
                render.Invoke(panel, new object[] { rows, null });
            }

            ProbeRowLabels first = ReadRowLabels(panel, 0);
            ProbeRowLabels second = ReadRowLabels(panel, 1);
            if (first == null || second == null)
            {
                Write("flash test: panel rows unavailable");
                return;
            }

            Color baseA = first.Name.color;
            Color baseB = second.Name.color;
            Color scoreA = first.Score.color;
            Color accuracyA = first.Accuracy.color;
            Color comboA = first.Combo.color;
            Color mistakesA = first.Mistakes.color;
            Color scoreB = second.Score.color;

            // Rendering the same ranks again is a routine refresh and must not start a flash.
            render.Invoke(panel, new object[] { rows, null });
            ProbeRowLabels stableA = ReadRowLabels(panel, 0);
            ProbeRowLabels stableB = ReadRowLabels(panel, 1);
            bool routineQuiet = stableA != null && stableB != null &&
                                SameColor(stableA.Name.color, baseA) &&
                                SameColor(stableB.Name.color, baseB);

            a.Rank = 2;
            b.Rank = 1;
            render.Invoke(panel, new object[] { rows, null });

            ProbeRowLabels movedDown = ReadRowLabels(panel, 0);
            ProbeRowLabels movedUp = ReadRowLabels(panel, 1);
            bool downFlashed = movedDown != null && IsReddish(movedDown.Name.color);
            bool upFlashed = movedUp != null && IsReddish(movedUp.Name.color);
            bool onlyName = movedDown != null && movedUp != null &&
                            SameColor(movedDown.Score.color, scoreA) &&
                            SameColor(movedDown.Accuracy.color, accuracyA) &&
                            SameColor(movedDown.Combo.color, comboA) &&
                            SameColor(movedDown.Mistakes.color, mistakesA) &&
                            SameColor(movedUp.Score.color, scoreB);

            Color expired = (Color)nameColorFor.Invoke(
                panel, new object[] { b, Time.unscaledTime + 2f });
            bool expires = !IsReddish(expired);

            Write("flash test: routineRefreshed=" + (!routineQuiet) +
                  ", rankDownNameRed=" + downFlashed +
                  ", rankUpNameRed=" + upFlashed +
                  ", otherColumnsUnchanged=" + onlyName +
                  ", flashExpires=" + expires);
        }

        private static PlayerRow CreateTestRow(string id, string name, int rank)
        {
            return new PlayerRow
            {
                UserId = id,
                UserName = name,
                CacheKey = id,
                IsParticipant = true,
                HasScoreData = true,
                Score = rank == 1 ? 2000 : 1000,
                Accuracy = 95f,
                HasAccuracy = true,
                Rank = rank,
                Combo = 42,
                HasCombo = true,
                Mistakes = 1,
                HasMistakes = true
            };
        }

        private static ProbeRowLabels ReadRowLabels(WorldLeaderboardPanel panel, int index)
        {
            FieldInfo rowsField = typeof(WorldLeaderboardPanel).GetField(
                "_rows", BindingFlags.Instance | BindingFlags.NonPublic);
            Array rows = rowsField != null ? rowsField.GetValue(panel) as Array : null;
            if (rows == null || index >= rows.Length)
            {
                return null;
            }

            object row = rows.GetValue(index);
            if (row == null)
            {
                return null;
            }

            Type type = row.GetType();
            return new ProbeRowLabels
            {
                Name = ReadTextField(type, row, "Name"),
                Score = ReadTextField(type, row, "Score"),
                Accuracy = ReadTextField(type, row, "Accuracy"),
                Combo = ReadTextField(type, row, "Combo"),
                Mistakes = ReadTextField(type, row, "Mistakes")
            };
        }

        private static TextMeshProUGUI ReadTextField(Type type, object row, string field)
        {
            FieldInfo info = type.GetField(field, BindingFlags.Instance | BindingFlags.Public);
            return info != null ? info.GetValue(row) as TextMeshProUGUI : null;
        }

        private static bool SameColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f &&
                   Mathf.Abs(a.g - b.g) < 0.002f &&
                   Mathf.Abs(a.b - b.b) < 0.002f &&
                   Mathf.Abs(a.a - b.a) < 0.002f;
        }

        private static bool IsReddish(Color color)
        {
            return color.r > 0.70f && color.r - color.g > 0.12f && color.r - color.b > 0.12f;
        }

        private static LeaderboardSettingsViewController FindSettings()
        {
            LeaderboardSettingsFlowCoordinator[] flows =
                Resources.FindObjectsOfTypeAll<LeaderboardSettingsFlowCoordinator>();
            for (int i = 0; i < flows.Length; i++)
            {
                LeaderboardSettingsFlowCoordinator flow = flows[i];
                if (flow == null)
                {
                    continue;
                }

                object settings = GetMember(flow, "_settings");
                LeaderboardSettingsViewController controller = settings as LeaderboardSettingsViewController;
                if (controller != null)
                {
                    return controller;
                }
            }

            return null;
        }

        private static T FindActive<T>() where T : Component
        {
            T[] objects = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && objects[i].gameObject.activeInHierarchy)
                {
                    return objects[i];
                }
            }

            return null;
        }

        private static Button FindActiveButton(Transform root, string label)
        {
            if (root == null)
            {
                return null;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy)
                {
                    continue;
                }

                TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text != null && string.Equals(text.text.Trim(), label, StringComparison.Ordinal))
                {
                    return button;
                }
            }

            return null;
        }

        private static RectTransform GetRect(object target, string field)
        {
            return GetMember(target, field) as RectTransform;
        }

        private static object GetMember(object target, string field)
        {
            if (target == null)
            {
                return null;
            }

            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return info != null ? info.GetValue(target) : null;
        }

        private static string DescribeInjectedPages(LeaderboardSettingsViewController settings)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < PageFields.Length; i++)
            {
                if (text.Length > 0) text.Append(',');
                text.Append(PageSlugs[i]);
                text.Append('=');
                text.Append(GetRect(settings, PageFields[i]) != null ? "yes" : "no");
            }

            return text.ToString();
        }

        private static string DescribeRect(RectTransform rect)
        {
            if (rect == null)
            {
                return "null";
            }

            return "size=" + rect.rect.width.ToString("0.0") + "x" + rect.rect.height.ToString("0.0") +
                   " delta=" + rect.sizeDelta.x.ToString("0.0") + "x" + rect.sizeDelta.y.ToString("0.0");
        }

        private static string DescribeScale(Transform transform)
        {
            if (transform == null)
            {
                return "null";
            }

            Vector3 local = transform.localScale;
            Vector3 lossy = transform.lossyScale;
            return "local=" + local.x.ToString("0.00") + "," + local.y.ToString("0.00") +
                   "," + local.z.ToString("0.00") + " lossy=" + lossy.x.ToString("0.00") +
                   "," + lossy.y.ToString("0.00") + "," + lossy.z.ToString("0.00");
        }

        private static string DescribeVisiblePages(LeaderboardSettingsViewController settings)
        {
            List<string> visible = new List<string>();
            if (settings != null)
            {
                RectTransform main = GetRect(settings, "_pageMain");
                if (main != null && main.gameObject.activeInHierarchy) visible.Add("main");
                for (int i = 0; i < PageFields.Length; i++)
                {
                    RectTransform page = GetRect(settings, PageFields[i]);
                    if (page != null && page.gameObject.activeInHierarchy) visible.Add(PageSlugs[i]);
                }
            }

            return visible.Count == 0 ? "none" : string.Join(",", visible.ToArray());
        }

        private static void Write(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;
            try
            {
                Directory.CreateDirectory(ProbeRoot);
                File.AppendAllText(Path.Combine(ProbeRoot, LogFileName), line + Environment.NewLine);
            }
            catch
            {
                // The probe log is diagnostic only.
            }

            try
            {
                LeaderboardLog.Info("[UI probe] " + message);
            }
            catch
            {
                // Logging must never break the probe.
            }
        }
    }
}
