using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace OneWay.Decision.DirectLlm
{
    [CustomEditor(typeof(DirectLlmSettings))]
    public sealed class DirectLlmSettingsEditor : Editor
    {
        private const string SettingsAsset = "Assets/Decision_YYS/ZLLMControl_HJH/Resources/DirectLlmSettings.asset";

        [MenuItem("Tools/Decision/Direct LLM API Settings")]
        private static void SelectSettings()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DirectLlmSettings>(SettingsAsset);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("API 키는 이 PC의 Unity Editor에 저장됩니다. PC마다 한 번씩 입력하세요.", HelpBoxMessageType.Info));
            var cloudflareToken = Credential(root, "Cloudflare API Token", "CLOUDFLARE_API_TOKEN", true);
            var cloudflareAccount = Credential(root, "Cloudflare Account ID", "CLOUDFLARE_ACCOUNT_ID", false);
            var groq = Credential(root, "Groq API Key", "GROQ_API_KEY", true);
            var gemini = Credential(root, "Gemini API Key", "GEMINI_API_KEY", true);
            var status = new Label();
            var save = new Button(() =>
            {
                DirectLlmCredentials.SaveInspectorValue("CLOUDFLARE_API_TOKEN", cloudflareToken.value);
                DirectLlmCredentials.SaveInspectorValue("CLOUDFLARE_ACCOUNT_ID", cloudflareAccount.value);
                DirectLlmCredentials.SaveInspectorValue("GROQ_API_KEY", groq.value);
                DirectLlmCredentials.SaveInspectorValue("GEMINI_API_KEY", gemini.value);
                status.text = "API 키를 이 PC에 저장했습니다.";
                LocalStoryRuntimeService.TryResumePending();
            }) { text = "Save API Keys" };
            root.Add(save);
            root.Add(status);
            root.Add(new PropertyField(serializedObject.FindProperty("options")));
            return root;
        }

        private static TextField Credential(VisualElement root, string label, string key, bool password)
        {
            var field = new TextField(label) { isPasswordField = password };
            field.SetValueWithoutNotify(DirectLlmCredentials.ReadInspectorValue(key));
            root.Add(field);
            return field;
        }
    }
}
