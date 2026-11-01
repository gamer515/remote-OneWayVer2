using UnityEditor;
using UnityEngine;

namespace EmacEArt.SkyboxURP
{
    public class EmacSkyboxURPEditor : ShaderGUI
    {
        static readonly GUIContent[] _sections =
        {
            new GUIContent("Zenith & Horizon"),
            new GUIContent("Sun"),
            new GUIContent("Sky Gradient  (azimuthal)"),
            new GUIContent("Atmosphere  (Aerial Perspective)"),
            new GUIContent("Color Grading"),
            new GUIContent("Stars"),
            new GUIContent("Clouds"),
        };

        readonly bool[] _foldouts = { true, true, true, true, true, false, true };

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
        {
            EditorGUILayout.LabelField("EmacEArt - Skybox URP", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── Section 0: Gradient ─────────────────────────────────────────────
            _foldouts[0] = Foldout(_foldouts[0], _sections[0]);
            if (_foldouts[0])
            {
                using (new EditorGUI.IndentLevelScope())
                    Draw(props, editor,
                        "_ZenithColor", "_HorizonColor", "_GroundColor",
                        "_HorizonSharpness", "_HorizonOffset");
            }

            // ── Section 1: Sun ──────────────────────────────────────────────────
            _foldouts[1] = Foldout(_foldouts[1], _sections[1]);
            if (_foldouts[1])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    // Follow toggle (stored as float, not a shader keyword)
                    var followProp = FindProp("_SunFollowLight", props);
                    bool follow = followProp.floatValue > 0.5f;

                    EditorGUI.BeginChangeCheck();
                    bool newFollow = EditorGUILayout.Toggle(followProp.displayName, follow);
                    if (EditorGUI.EndChangeCheck())
                    {
                        followProp.floatValue = newFollow ? 1f : 0f;
                        follow = newFollow;
                    }

                    if (follow)
                        SyncSunDirectionFromLight(props);

                    // Direction field: shown read-only when syncing, editable otherwise
                    using (new EditorGUI.DisabledScope(follow))
                    {
                        var dirProp = FindProp("_SunDirection", props);
                        EditorGUILayout.Vector3Field(
                            follow ? "Sun Direction (auto)" : "Sun Direction (manual)",
                            dirProp.vectorValue);
                    }

                    if (follow)
                    {
                        Light light = GetMainDirectionalLight();
                        string hint = light != null
                            ? "Synced with: " + light.name
                            : "No Directional Light found in scene!";
                        EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
                    }

                    EditorGUILayout.Space(2);
                    Draw(props, editor, "_SunColor", "_SunGlowColor",
                         "_SunSize", "_SunGlowSize", "_SunGlowFalloff");
                }
            }

            // ── Section 2: Sky Gradient ─────────────────────────────────────────
            _foldouts[2] = Foldout(_foldouts[2], _sections[2]);
            if (_foldouts[2])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(
                        "Tints one side of the sky with a chosen color.\n" +
                        "Direction = compass angle (0=forward, 90=right, 180=back, 270=left).\n" +
                        "Spread = falloff width.  Strength = 0 -> disabled.",
                        MessageType.None);
                    Draw(props, editor,
                        "_SkyGradColor", "_SkyGradAngle", "_SkyGradSpread", "_SkyGradStr");
                }
            }

            // ── Section 3: Atmosphere ───────────────────────────────────────────
            _foldouts[3] = Foldout(_foldouts[3], _sections[3]);
            if (_foldouts[3])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(
                        "Atmosphere Strength = intensity of aerial perspective haze at the horizon.\n" +
                        "Atmosphere Falloff = how quickly the haze fades from horizon toward zenith.",
                        MessageType.None);
                    Draw(props, editor,
                        "_AtmosphereColor", "_AtmosphereStrength", "_AtmosphereFalloff");
                }
            }

            // ── Section 4: Color Grading ────────────────────────────────────────
            _foldouts[4] = Foldout(_foldouts[4], _sections[4]);
            if (_foldouts[4])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(
                        "Lift = shadows | Gamma = midtones (neutral = 0.5) | Gain = highlights",
                        MessageType.None);
                    Draw(props, editor,
                        "_Exposure", "_Contrast", "_Saturation",
                        "_Lift", "_Gamma", "_Gain");
                }
            }

            // ── Section 5: Stars ────────────────────────────────────────────────
            _foldouts[5] = Foldout(_foldouts[5], _sections[5]);
            if (_foldouts[5])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var starsOn = FindProp("_StarsEnabled", props);
                    editor.ShaderProperty(starsOn, starsOn.displayName);
                    if (starsOn.floatValue > 0.5f)
                        Draw(props, editor,
                            "_StarsTex", "_StarsIntensity", "_StarsThreshold", "_StarsSharpness");
                }
            }

            // ── Section 6: Clouds ───────────────────────────────────────────────
            _foldouts[6] = Foldout(_foldouts[6], _sections[6]);
            if (_foldouts[6])
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var cloudsOn = FindProp("_CloudsEnabled", props);
                    editor.ShaderProperty(cloudsOn, cloudsOn.displayName);
                    if (cloudsOn.floatValue > 0.5f)
                    {
                        // ── Style popup ─────────────────────────────────────────
                        var styleProp = FindProp("_CloudStyle", props);
                        int styleIdx = Mathf.RoundToInt(styleProp.floatValue);
                        string[] styleNames =
                        {
                            "0 - Standard (FBM)",
                            "1 - Feathered (Ridged / cirrus)",
                            "2 - Round (Voronoi Euclidean)",
                            "3 - Diagonal 45deg (FBM rotated)",
                            "4 - Cotton (merged blobs)",
                        };
                        EditorGUI.BeginChangeCheck();
                        styleIdx = EditorGUILayout.Popup("Cloud Style", styleIdx, styleNames);
                        if (EditorGUI.EndChangeCheck())
                            styleProp.floatValue = styleIdx;

                        EditorGUILayout.Space(4);

                        EditorGUILayout.HelpBox(
                            "Smooth     : Bands=1,  Softness=0.15, Stretch=0\n" +
                            "Cel-shaded : Bands=5,  Softness=0.02, Stretch=0\n" +
                            "Johnny Bravo: Bands=3, Softness=0.001, Stretch=0.85",
                            MessageType.None);

                        Draw(props, editor,
                            "_CloudColor", "_CloudShadowColor",
                            "_CloudCoverage", "_CloudDensity", "_CloudSoftness", "_CloudBands",
                            "_CloudStretch", "_CloudSwirl",
                            "_CloudScale", "_CloudHeight", "_CloudSpeed");
                    }
                }
            }

            EditorGUILayout.Space(8);
            editor.RenderQueueField();
        }

        // ── Sun sync ─────────────────────────────────────────────────────────────
        static void SyncSunDirectionFromLight(MaterialProperty[] props)
        {
            Light light = GetMainDirectionalLight();
            if (light == null) return;

            // Direction toward sun = opposite of where the light shines
            Vector3 towardSun = -light.transform.forward;

            var dirProp = FindProp("_SunDirection", props);
            dirProp.vectorValue = new Vector4(towardSun.x, towardSun.y, towardSun.z, 0f);
        }

        static Light GetMainDirectionalLight()
        {
            Light[] lights = Object.FindObjectsOfType<Light>();
            foreach (Light l in lights)
                if (l.type == LightType.Directional && l.isActiveAndEnabled)
                    return l;
            return null;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        static bool Foldout(bool state, GUIContent label)
        {
            var style = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            return EditorGUI.Foldout(r, state, label, true, style);
        }

        static void Draw(MaterialProperty[] props, MaterialEditor editor, params string[] names)
        {
            foreach (string n in names)
            {
                var p = FindProp(n, props);
                if (p != null)
                    editor.ShaderProperty(p, p.displayName);
            }
        }

        static MaterialProperty FindProp(string name, MaterialProperty[] props)
        {
            foreach (var p in props)
                if (p.name == name) return p;
            return null;
        }
    }
}
