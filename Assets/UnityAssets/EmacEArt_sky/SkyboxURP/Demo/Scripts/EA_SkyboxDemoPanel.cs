// EmacEArt - Stylized Procedural Sky for URP
// Demo control panel. Builds itself at runtime, so the demo scene stays a plain
// scene in the editor: a ground, a few props, a light and the sky.
//
// The panel NEVER edits the material asset on disk. On start it clones the sky
// material into memory, drives the clone, and puts the original back when the
// demo stops. Nothing you click here can change the material you ship with.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmacEArt.SkyboxURP.Demo
{
    [AddComponentMenu("EmacEArt/Stylized Sky Demo Panel")]
    public class EA_SkyboxDemoPanel : MonoBehaviour
    {
        [Tooltip("Directional light the sun is locked to. Left empty, the first " +
                 "directional light in the scene is used.")]
        public Light SunLight;

        [Tooltip("Sky material the demo starts from. Left empty, the scene skybox is used.")]
        public Material SourceSky;

        // ---------------------------------------------------------------- state
        Material _sky;          // runtime clone, the only thing the panel writes to
        Material _original;     // what the scene had before the demo started
        Font _font;
        readonly List<Slider> _sliders = new List<Slider>();
        readonly List<string> _sliderProps = new List<string>();
        readonly List<Text> _sliderValues = new List<Text>();
        Text _cloudsButtonLabel, _starsButtonLabel, _styleLabel, _helpLabel;
        Slider _azimuth, _elevation, _style;
        bool _panelVisible = true;
        RectTransform _panel;

        static readonly string[] StyleNames =
            { "Standard", "Feathered", "Round", "Diagonal", "Cotton" };

        // ---------------------------------------------------------------- rows
        struct Row
        {
            public string Prop, Label;
            public float Min, Max;
            public Row(string prop, string label, float min, float max)
            { Prop = prop; Label = label; Min = min; Max = max; }
        }

        static readonly Row[] SkyRows =
        {
            new Row("_HorizonSharpness",  "Horizon sharpness", 0.5f, 16f),
            new Row("_HorizonOffset",     "Horizon offset",   -0.5f, 0.5f),
            new Row("_AtmosphereStrength","Haze strength",     0f,   1.5f),
            new Row("_AtmosphereFalloff", "Haze falloff",      1f,   10f),
            new Row("_SkyGradStr",        "Side glow",         0f,   1f),
            new Row("_SkyGradAngle",      "Side glow angle",   0f,   360f),
            new Row("_SkyGradSpread",     "Side glow spread",  0.01f,1f),
            new Row("_SunSize",           "Sun size",          0.0005f, 0.05f),
            new Row("_SunGlowSize",       "Sun halo size",     0.01f, 1f),
            new Row("_SunGlowFalloff",    "Sun halo falloff",  1f,   16f),
            new Row("_Exposure",          "Brightness",        0.1f, 4f),
            new Row("_Contrast",          "Contrast",          0.5f, 2f),
            new Row("_Saturation",        "Colour strength",   0f,   2.5f),
        };

        static readonly Row[] CloudRows =
        {
            new Row("_CloudCoverage", "Cloud coverage", 0f,    1f),
            new Row("_CloudDensity",  "Cloud density",  0f,    1f),
            new Row("_CloudSoftness", "Edge softness",  0.001f,0.5f),
            new Row("_CloudBands",    "Flat steps",     1f,    8f),
            new Row("_CloudScale",    "Cloud size",     0.5f,  8f),
            new Row("_CloudStretch",  "Stretch",        0f,    0.97f),
            new Row("_CloudSwirl",    "Swirl",          0f,    1f),
            new Row("_CloudHeight",   "Lowest cloud",   0f,    0.5f),
            new Row("_CloudSpeed",    "Drift speed",    0f,    1f),
        };

        // ---------------------------------------------------------------- presets
        class Preset
        {
            public string Name;
            public bool Clouds, Stars;
            public float Azimuth, Elevation;
            public Dictionary<string, float> Floats = new Dictionary<string, float>();
            public Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        }

        static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

        static readonly Preset[] Presets =
        {
            new Preset {
                Name = "Clear day", Clouds = false, Stars = false, Azimuth = 150f, Elevation = 45f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",3.2f},{"_HorizonOffset",0f},
                    {"_AtmosphereStrength",0.30f},{"_AtmosphereFalloff",4.5f},
                    {"_SkyGradStr",0f},{"_SunSize",0.004f},{"_SunGlowSize",0.18f},
                    {"_SunGlowFalloff",9f},{"_Exposure",1.05f},{"_Contrast",1.05f},
                    {"_Saturation",1.15f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.10f,0.30f,0.75f)},{"_HorizonColor",C(0.66f,0.82f,0.95f)},
                    {"_GroundColor",C(0.18f,0.16f,0.13f)},{"_AtmosphereColor",C(0.55f,0.72f,0.94f)},
                    {"_SunColor",C(1f,0.98f,0.92f)},{"_SunGlowColor",C(1f,0.92f,0.78f)}}
            },
            new Preset {
                Name = "Soft clouds", Clouds = true, Stars = false, Azimuth = 150f, Elevation = 25f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",2.6f},{"_HorizonOffset",0.02f},
                    {"_AtmosphereStrength",0.34f},{"_AtmosphereFalloff",4f},
                    {"_SkyGradStr",0.30f},{"_SkyGradAngle",330f},{"_SkyGradSpread",0.62f},
                    {"_SunSize",0.006f},{"_SunGlowSize",0.28f},{"_SunGlowFalloff",6.5f},
                    {"_CloudCoverage",0.54f},{"_CloudDensity",0.92f},{"_CloudSoftness",0.09f},
                    {"_CloudBands",1f},{"_CloudScale",2.4f},{"_CloudStretch",0.36f},
                    {"_CloudSwirl",0.18f},{"_CloudHeight",0.04f},{"_CloudSpeed",0.018f},
                    {"_Exposure",1.08f},{"_Contrast",1.04f},{"_Saturation",1.12f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.13f,0.32f,0.72f)},{"_HorizonColor",C(0.62f,0.78f,0.92f)},
                    {"_GroundColor",C(0.18f,0.16f,0.13f)},{"_AtmosphereColor",C(0.55f,0.70f,0.92f)},
                    {"_CloudColor",C(1f,0.99f,0.97f)},{"_CloudShadowColor",C(0.62f,0.68f,0.80f)},
                    {"_SkyGradColor",C(1f,0.62f,0.30f)},
                    {"_SunColor",C(1f,0.97f,0.90f)},{"_SunGlowColor",C(1f,0.85f,0.62f)}}
            },
            new Preset {
                Name = "Golden hour", Clouds = true, Stars = false, Azimuth = 150f, Elevation = 9f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",1.9f},{"_HorizonOffset",0.06f},
                    {"_AtmosphereStrength",0.62f},{"_AtmosphereFalloff",3f},
                    {"_SkyGradStr",0.55f},{"_SkyGradAngle",330f},{"_SkyGradSpread",0.75f},
                    {"_SunSize",0.011f},{"_SunGlowSize",0.42f},{"_SunGlowFalloff",4.5f},
                    {"_CloudCoverage",0.46f},{"_CloudDensity",0.95f},{"_CloudSoftness",0.06f},
                    {"_CloudBands",1f},{"_CloudScale",3.1f},{"_CloudStretch",0.55f},
                    {"_CloudSwirl",0.12f},{"_CloudHeight",0.02f},{"_CloudSpeed",0.02f},
                    {"_Exposure",1.12f},{"_Contrast",1.06f},{"_Saturation",1.30f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.16f,0.24f,0.58f)},{"_HorizonColor",C(0.98f,0.74f,0.45f)},
                    {"_GroundColor",C(0.16f,0.13f,0.11f)},{"_AtmosphereColor",C(1f,0.66f,0.36f)},
                    {"_CloudColor",C(1f,0.90f,0.76f)},{"_CloudShadowColor",C(0.58f,0.48f,0.56f)},
                    {"_SkyGradColor",C(1f,0.52f,0.20f)},
                    {"_SunColor",C(1f,0.94f,0.82f)},{"_SunGlowColor",C(1f,0.64f,0.30f)}}
            },
            new Preset {
                Name = "Sunset", Clouds = true, Stars = false, Azimuth = 150f, Elevation = 2f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",1.5f},{"_HorizonOffset",0.10f},
                    {"_AtmosphereStrength",0.85f},{"_AtmosphereFalloff",2.4f},
                    {"_SkyGradStr",0.72f},{"_SkyGradAngle",330f},{"_SkyGradSpread",0.85f},
                    {"_SunSize",0.016f},{"_SunGlowSize",0.52f},{"_SunGlowFalloff",3.4f},
                    {"_CloudCoverage",0.52f},{"_CloudDensity",1f},{"_CloudSoftness",0.05f},
                    {"_CloudBands",1f},{"_CloudScale",2.8f},{"_CloudStretch",0.62f},
                    {"_CloudSwirl",0.20f},{"_CloudHeight",0.01f},{"_CloudSpeed",0.016f},
                    {"_Exposure",1.02f},{"_Contrast",1.10f},{"_Saturation",1.35f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.14f,0.14f,0.42f)},{"_HorizonColor",C(0.96f,0.55f,0.36f)},
                    {"_GroundColor",C(0.12f,0.10f,0.10f)},{"_AtmosphereColor",C(1f,0.48f,0.28f)},
                    {"_CloudColor",C(1f,0.82f,0.66f)},{"_CloudShadowColor",C(0.48f,0.36f,0.50f)},
                    {"_SkyGradColor",C(1f,0.38f,0.14f)},
                    {"_SunColor",C(1f,0.88f,0.70f)},{"_SunGlowColor",C(1f,0.50f,0.22f)}}
            },
            new Preset {
                Name = "Overcast", Clouds = true, Stars = false, Azimuth = 210f, Elevation = 32f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",3.5f},{"_HorizonOffset",0f},
                    {"_AtmosphereStrength",0.45f},{"_AtmosphereFalloff",5f},
                    {"_SkyGradStr",0.08f},{"_SkyGradAngle",210f},{"_SkyGradSpread",0.9f},
                    {"_SunSize",0.003f},{"_SunGlowSize",0.36f},{"_SunGlowFalloff",5f},
                    {"_CloudCoverage",0.88f},{"_CloudDensity",1f},{"_CloudSoftness",0.30f},
                    {"_CloudBands",1f},{"_CloudScale",1.9f},{"_CloudStretch",0.42f},
                    {"_CloudSwirl",0.25f},{"_CloudHeight",0f},{"_CloudSpeed",0.03f},
                    {"_Exposure",0.92f},{"_Contrast",0.94f},{"_Saturation",0.72f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.46f,0.50f,0.56f)},{"_HorizonColor",C(0.74f,0.76f,0.79f)},
                    {"_GroundColor",C(0.15f,0.15f,0.15f)},{"_AtmosphereColor",C(0.66f,0.69f,0.74f)},
                    {"_CloudColor",C(0.88f,0.89f,0.92f)},{"_CloudShadowColor",C(0.55f,0.58f,0.64f)},
                    {"_SkyGradColor",C(0.70f,0.72f,0.78f)},
                    {"_SunColor",C(0.90f,0.90f,0.90f)},{"_SunGlowColor",C(0.80f,0.82f,0.86f)}}
            },
            new Preset {
                Name = "Storm", Clouds = true, Stars = false, Azimuth = 60f, Elevation = 18f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",2.2f},{"_HorizonOffset",-0.04f},
                    {"_AtmosphereStrength",0.38f},{"_AtmosphereFalloff",3.2f},
                    {"_SkyGradStr",0.22f},{"_SkyGradAngle",60f},{"_SkyGradSpread",0.55f},
                    {"_SunSize",0.002f},{"_SunGlowSize",0.22f},{"_SunGlowFalloff",7f},
                    {"_CloudCoverage",0.80f},{"_CloudDensity",1f},{"_CloudSoftness",0.14f},
                    {"_CloudBands",1f},{"_CloudScale",3.6f},{"_CloudStretch",0.80f},
                    {"_CloudSwirl",0.55f},{"_CloudHeight",0f},{"_CloudSpeed",0.09f},
                    {"_Exposure",0.85f},{"_Contrast",1.18f},{"_Saturation",0.80f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.10f,0.12f,0.18f)},{"_HorizonColor",C(0.42f,0.46f,0.54f)},
                    {"_GroundColor",C(0.09f,0.09f,0.10f)},{"_AtmosphereColor",C(0.40f,0.45f,0.55f)},
                    {"_CloudColor",C(0.62f,0.65f,0.72f)},{"_CloudShadowColor",C(0.22f,0.24f,0.30f)},
                    {"_SkyGradColor",C(0.85f,0.72f,0.55f)},
                    {"_SunColor",C(0.85f,0.87f,0.92f)},{"_SunGlowColor",C(0.62f,0.66f,0.76f)}}
            },
            new Preset {
                Name = "Toon bands", Clouds = true, Stars = false, Azimuth = 150f, Elevation = 35f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",2.8f},{"_HorizonOffset",0.02f},
                    {"_AtmosphereStrength",0.20f},{"_AtmosphereFalloff",6f},
                    {"_SkyGradStr",0.18f},{"_SkyGradAngle",330f},{"_SkyGradSpread",0.7f},
                    {"_SunSize",0.012f},{"_SunGlowSize",0.16f},{"_SunGlowFalloff",12f},
                    {"_CloudCoverage",0.58f},{"_CloudDensity",1f},{"_CloudSoftness",0.012f},
                    {"_CloudBands",4f},{"_CloudScale",2.2f},{"_CloudStretch",0.24f},
                    {"_CloudSwirl",0.10f},{"_CloudHeight",0.03f},{"_CloudSpeed",0.02f},
                    {"_Exposure",1.10f},{"_Contrast",1.12f},{"_Saturation",1.45f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.16f,0.42f,0.86f)},{"_HorizonColor",C(0.62f,0.86f,0.98f)},
                    {"_GroundColor",C(0.20f,0.18f,0.14f)},{"_AtmosphereColor",C(0.50f,0.80f,0.98f)},
                    {"_CloudColor",C(1f,1f,1f)},{"_CloudShadowColor",C(0.70f,0.82f,0.95f)},
                    {"_SkyGradColor",C(1f,0.70f,0.35f)},
                    {"_SunColor",C(1f,1f,0.94f)},{"_SunGlowColor",C(1f,0.94f,0.72f)}}
            },
            new Preset {
                Name = "Night", Clouds = true, Stars = true, Azimuth = 150f, Elevation = -6f,
                Floats = new Dictionary<string, float> {
                    {"_HorizonSharpness",4.5f},{"_HorizonOffset",0.04f},
                    {"_AtmosphereStrength",0.28f},{"_AtmosphereFalloff",3.6f},
                    {"_SkyGradStr",0.20f},{"_SkyGradAngle",330f},{"_SkyGradSpread",0.6f},
                    {"_SunSize",0.006f},{"_SunGlowSize",0.30f},{"_SunGlowFalloff",5f},
                    {"_CloudCoverage",0.40f},{"_CloudDensity",0.85f},{"_CloudSoftness",0.12f},
                    {"_CloudBands",1f},{"_CloudScale",2.6f},{"_CloudStretch",0.44f},
                    {"_CloudSwirl",0.20f},{"_CloudHeight",0.02f},{"_CloudSpeed",0.012f},
                    {"_StarsIntensity",1.6f},{"_StarsThreshold",0.62f},{"_StarsSharpness",9f},
                    {"_Exposure",0.80f},{"_Contrast",1.08f},{"_Saturation",1.05f}},
                Colors = new Dictionary<string, Color> {
                    {"_ZenithColor",C(0.02f,0.03f,0.11f)},{"_HorizonColor",C(0.10f,0.16f,0.34f)},
                    {"_GroundColor",C(0.04f,0.04f,0.05f)},{"_AtmosphereColor",C(0.12f,0.20f,0.42f)},
                    {"_CloudColor",C(0.30f,0.36f,0.52f)},{"_CloudShadowColor",C(0.08f,0.10f,0.18f)},
                    {"_SkyGradColor",C(0.30f,0.26f,0.48f)},
                    {"_SunColor",C(0.80f,0.84f,1f)},{"_SunGlowColor",C(0.26f,0.32f,0.56f)}}
            },
        };

        // ---------------------------------------------------------------- lifecycle
        void Start()
        {
            if (SunLight == null)
            {
                Light[] lights = FindObjectsOfType<Light>();
                foreach (Light l in lights)
                    if (l.type == LightType.Directional) { SunLight = l; break; }
            }

            _original = SourceSky != null ? SourceSky : RenderSettings.skybox;
            if (_original == null)
            {
                Debug.LogWarning("[EmacEArt] No sky material found. " +
                                 "Set one in Lighting > Environment > Skybox Material.");
                enabled = false;
                return;
            }

            _sky = new Material(_original);
            _sky.name = _original.name + " (demo copy)";
            RenderSettings.skybox = _sky;

            // The built-in font is called LegacyRuntime.ttf from 2022.2 on and Arial.ttf
            // before that. Asking for the wrong one logs an error, so both are guarded.
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { _font = null; }
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch { _font = null; }
            }

            BuildUI();
            ReadFromMaterial();
            PushSunDirection();
        }

        void OnDestroy()
        {
            if (_original != null) RenderSettings.skybox = _original;
            if (_sky != null) Destroy(_sky);
        }

        void Update()
        {
            // A project set to the new Input System only makes the old Input class throw.
            // The hotkey is a convenience, so it switches itself off instead of spamming.
            if (!_hotkeyWorks) return;
            try
            {
                // H only. Tab was the second key here and it is far too easy to hit by
                // accident, which looked like the panel vanishing on its own.
                if (Input.GetKeyDown(KeyCode.H))
                {
                    _panelVisible = !_panelVisible;
                    if (_panel != null) _panel.gameObject.SetActive(_panelVisible);
                }
            }
            catch (System.Exception)
            {
                _hotkeyWorks = false;
                if (_helpLabel != null)
                    _helpLabel.text = "Nothing here changes the material on disk.";
            }
        }

        bool _hotkeyWorks = true;

        // ---------------------------------------------------------------- UI build
        const float PanelW = 660f, RowH = 24f, Pad = 10f, LabelW = 132f, ValueW = 46f;

        void BuildUI()
        {
            if (EventSystem.current == null)
            {
                GameObject es = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            GameObject canvasGo = new GameObject("Sky Demo Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            // Expand, not Match: the whole 1280x720 reference area is guaranteed to fit
            // on screen whatever the aspect is. Matching on height alone pushed the panel
            // off a fullscreen game view.
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            int rows = Mathf.Max(SkyRows.Length, CloudRows.Length + 4);
            float panelH = Pad * 2f + 28f          // title
                         + 28f * 2f                // preset rows
                         + 22f                     // column headers
                         + rows * RowH + 26f;      // rows + footer

            _panel = NewRect("Panel", canvasGo.transform,
                             new Vector2(12f, -12f), new Vector2(PanelW, panelH));
            Image bg = _panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.09f, 0.88f);

            float y = -Pad;

            Text title = NewLabel(_panel, new Vector2(Pad, y),
                                  new Vector2(PanelW - Pad * 2f, 22f),
                                  "EmacEArt - Stylized Procedural Sky", 15, FontStyle.Bold);
            title.color = new Color(0.65f, 0.86f, 1f);
            y -= 26f;

            // ---- preset buttons, two rows of four
            for (int i = 0; i < Presets.Length; i++)
            {
                int row = i / 4, col = i % 4;
                float bw = (PanelW - Pad * 2f - 3f * 6f) / 4f;
                Preset p = Presets[i];
                MakeButton(_panel,
                    new Vector2(Pad + col * (bw + 6f), y - row * 26f),
                    new Vector2(bw, 22f), p.Name,
                    delegate { ApplyPreset(p); });
            }
            y -= 26f * 2f + 6f;

            // ---- two columns
            float colW = (PanelW - Pad * 2f - 12f) / 2f;
            float xL = Pad, xR = Pad + colW + 12f;

            Text hL = NewLabel(_panel, new Vector2(xL, y), new Vector2(colW, 18f),
                               "SKY, SUN AND COLOUR", 11, FontStyle.Bold);
            hL.color = new Color(0.70f, 0.76f, 0.84f);
            Text hR = NewLabel(_panel, new Vector2(xR, y), new Vector2(colW, 18f),
                               "CLOUDS, STARS AND TIME OF DAY", 11, FontStyle.Bold);
            hR.color = new Color(0.70f, 0.76f, 0.84f);
            y -= 20f;

            float yL = y;
            foreach (Row r in SkyRows)
            {
                MakeSliderRow(_panel, xL, yL, colW, r);
                yL -= RowH;
            }

            float yR = y;
            foreach (Row r in CloudRows)
            {
                MakeSliderRow(_panel, xR, yR, colW, r);
                yR -= RowH;
            }

            // cloud style stepper
            _style = MakeRawSlider(_panel, xR, yR, colW, "Cloud shape", 0f, 4f, out _styleLabel);
            _style.wholeNumbers = true;
            _style.onValueChanged.AddListener(delegate (float v)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(v), 0, StyleNames.Length - 1);
                _sky.SetFloat("_CloudStyle", i);
                _styleLabel.text = StyleNames[i];
            });
            yR -= RowH;

            // time of day
            _azimuth = MakeRawSlider(_panel, xR, yR, colW, "Sun compass", 0f, 360f, out Text azV);
            _azimuth.onValueChanged.AddListener(delegate (float v)
            { azV.text = Mathf.RoundToInt(v).ToString(); PushSunDirection(); });
            yR -= RowH;

            _elevation = MakeRawSlider(_panel, xR, yR, colW, "Sun height", -15f, 88f, out Text elV);
            _elevation.onValueChanged.AddListener(delegate (float v)
            { elV.text = Mathf.RoundToInt(v).ToString(); PushSunDirection(); });
            yR -= RowH;

            // toggles
            float halfW = (colW - 6f) / 2f;
            _cloudsButtonLabel = MakeButton(_panel, new Vector2(xR, yR), new Vector2(halfW, 22f),
                "Clouds: on", delegate { ToggleKeyword("_CloudsEnabled", "_CLOUDSENABLED_ON"); });
            _starsButtonLabel = MakeButton(_panel, new Vector2(xR + halfW + 6f, yR),
                new Vector2(halfW, 22f), "Stars: off",
                delegate { ToggleKeyword("_StarsEnabled", "_STARSENABLED_ON"); });
            yR -= RowH + 4f;

            float yFoot = Mathf.Min(yL, yR) - 4f;
            _helpLabel = NewLabel(_panel, new Vector2(Pad, yFoot),
                new Vector2(PanelW - Pad * 2f, 18f),
                "H hides this panel. Nothing here changes the material on disk.",
                11, FontStyle.Normal);
            _helpLabel.color = new Color(0.62f, 0.66f, 0.72f);
        }

        // ---------------------------------------------------------------- widgets
        RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Text NewLabel(Transform parent, Vector2 pos, Vector2 size, string text,
                      int fontSize, FontStyle style)
        {
            RectTransform rt = NewRect("Label", parent, pos, size);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.text = text;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Text MakeButton(Transform parent, Vector2 pos, Vector2 size, string caption,
                        UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rt = NewRect("Button " + caption, parent, pos, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.16f, 0.20f, 0.26f, 1f);
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            b.colors = cb;
            b.onClick.AddListener(onClick);

            Text t = NewLabel(rt, Vector2.zero, size, caption, 12, FontStyle.Bold);
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        Slider MakeRawSlider(Transform parent, float x, float y, float colW, string label,
                             float min, float max, out Text valueText)
        {
            RectTransform rowRt = NewRect("Row " + label, parent,
                                          new Vector2(x, y), new Vector2(colW, RowH));

            NewLabel(rowRt, new Vector2(0f, -3f), new Vector2(LabelW, 18f), label, 12,
                     FontStyle.Normal);

            valueText = NewLabel(rowRt, new Vector2(colW - ValueW, -3f),
                                 new Vector2(ValueW, 18f), "", 12, FontStyle.Normal);
            valueText.alignment = TextAnchor.MiddleRight;
            valueText.color = new Color(0.72f, 0.88f, 1f);

            float sw = colW - LabelW - ValueW - 8f;
            RectTransform srt = NewRect("Slider", rowRt, new Vector2(LabelW, -6f),
                                        new Vector2(sw, 12f));
            Slider s = srt.gameObject.AddComponent<Slider>();

            RectTransform bgRt = NewRect("Background", srt, new Vector2(0f, -4f),
                                         new Vector2(sw, 4f));
            Image bgImg = bgRt.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.24f, 0.27f, 0.32f, 1f);

            RectTransform fillArea = NewRect("Fill Area", srt, new Vector2(0f, -4f),
                                             new Vector2(sw, 4f));
            RectTransform fill = NewRect("Fill", fillArea, Vector2.zero, new Vector2(sw, 4f));
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = new Color(0.35f, 0.66f, 0.95f, 1f);

            RectTransform handleArea = NewRect("Handle Slide Area", srt, Vector2.zero,
                                               new Vector2(sw, 12f));
            RectTransform handle = NewRect("Handle", handleArea, Vector2.zero,
                                           new Vector2(10f, 12f));
            Image handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = new Color(0.90f, 0.94f, 1f, 1f);

            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            return s;
        }

        void MakeSliderRow(Transform parent, float x, float y, float colW, Row r)
        {
            Text valueText;
            Slider s = MakeRawSlider(parent, x, y, colW, r.Label, r.Min, r.Max, out valueText);
            string prop = r.Prop;
            s.onValueChanged.AddListener(delegate (float v)
            {
                _sky.SetFloat(prop, v);
                valueText.text = v.ToString(v < 10f ? "0.000" : "0.0");
            });
            _sliders.Add(s);
            _sliderProps.Add(prop);
            _sliderValues.Add(valueText);
        }

        // ---------------------------------------------------------------- behaviour
        void PushSunDirection()
        {
            if (SunLight == null) return;
            float az = _azimuth != null ? _azimuth.value : 150f;
            float el = _elevation != null ? _elevation.value : 25f;
            SunLight.transform.rotation = Quaternion.Euler(el, az, 0f);
            Vector3 toward = -SunLight.transform.forward;
            _sky.SetVector("_SunDirection", new Vector4(toward.x, toward.y, toward.z, 0f));
            DynamicGI.UpdateEnvironment();
        }

        void ToggleKeyword(string prop, string keyword)
        {
            bool on = _sky.GetFloat(prop) < 0.5f;
            SetKeyword(prop, keyword, on);
            RefreshToggleLabels();
        }

        void SetKeyword(string prop, string keyword, bool on)
        {
            _sky.SetFloat(prop, on ? 1f : 0f);
            if (on) _sky.EnableKeyword(keyword);
            else _sky.DisableKeyword(keyword);
        }

        void RefreshToggleLabels()
        {
            if (_cloudsButtonLabel != null)
                _cloudsButtonLabel.text = "Clouds: " +
                    (_sky.GetFloat("_CloudsEnabled") > 0.5f ? "on" : "off");
            if (_starsButtonLabel != null)
                _starsButtonLabel.text = "Stars: " +
                    (_sky.GetFloat("_StarsEnabled") > 0.5f ? "on" : "off");
        }

        void ApplyPreset(Preset p)
        {
            foreach (KeyValuePair<string, float> kv in p.Floats)
                if (_sky.HasProperty(kv.Key)) _sky.SetFloat(kv.Key, kv.Value);
            foreach (KeyValuePair<string, Color> kv in p.Colors)
                if (_sky.HasProperty(kv.Key)) _sky.SetColor(kv.Key, kv.Value);

            SetKeyword("_CloudsEnabled", "_CLOUDSENABLED_ON", p.Clouds);
            SetKeyword("_StarsEnabled", "_STARSENABLED_ON", p.Stars);

            if (_azimuth != null) _azimuth.SetValueWithoutNotify(p.Azimuth);
            if (_elevation != null) _elevation.SetValueWithoutNotify(p.Elevation);

            ReadFromMaterial();
            PushSunDirection();
        }

        // Pulls every slider back to what the material now holds, without firing
        // the callbacks that would write the old value straight back in.
        void ReadFromMaterial()
        {
            for (int i = 0; i < _sliders.Count; i++)
            {
                string prop = _sliderProps[i];
                if (!_sky.HasProperty(prop)) continue;
                float v = _sky.GetFloat(prop);
                _sliders[i].SetValueWithoutNotify(v);
                _sliderValues[i].text = v.ToString(v < 10f ? "0.000" : "0.0");
            }

            if (_style != null)
            {
                int st = Mathf.Clamp(Mathf.RoundToInt(_sky.GetFloat("_CloudStyle")),
                                     0, StyleNames.Length - 1);
                _style.SetValueWithoutNotify(st);
                _styleLabel.text = StyleNames[st];
            }

            RefreshToggleLabels();
        }
    }
}
