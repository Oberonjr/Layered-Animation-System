using UnityEditor;
using UnityEngine;
using System.IO;
using LAS;

namespace LAS
{
    [CustomEditor(typeof(ClaudeProvider))]
    public class ClaudeProviderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawApiKeySection("ANTHROPIC_API_KEY");
        }

        internal static void DrawApiKeySection(string defaultKeyName)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("── API Key File ──────────────────────────────", EditorStyles.miniLabel);

            // Ensure the template file exists so the path is always valid
            ApiKeyStore.EnsureFileExists();

            string filePath = ApiKeyStore.KeyFilePath;
            string dirPath  = ApiKeyStore.KeyDirectory;
            bool   fileExists = File.Exists(filePath);

            // Status
            bool keyLoaded = ApiKeyStore.HasKey(defaultKeyName);
            EditorGUILayout.HelpBox(
                keyLoaded
                    ? $"✓ Key '{defaultKeyName}' is loaded from the key file."
                    : $"✗ Key '{defaultKeyName}' is not set.\n  Fill it in the key file (see path below).",
                keyLoaded ? UnityEditor.MessageType.Info : UnityEditor.MessageType.Warning);

            // File path — selectable label
            EditorGUILayout.LabelField("Key file location:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(filePath, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            // Buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Key File"))
                {
                    if (fileExists)
                        OpenWithDefaultApp(filePath);
                    else
                        Debug.LogWarning($"[ApiKeyStore] Key file not found at: {filePath}");
                }

                if (GUILayout.Button("Reveal in Explorer"))
                {
                    if (Directory.Exists(dirPath))
                        EditorUtility.RevealInFinder(fileExists ? filePath : dirPath);
                    else
                        Debug.LogWarning($"[ApiKeyStore] Directory not found: {dirPath}");
                }

                if (GUILayout.Button("Copy Path"))
                {
                    EditorGUIUtility.systemCopyBuffer = filePath;
                    Debug.Log($"[ApiKeyStore] Path copied: {filePath}");
                }

                if (GUILayout.Button("Reload"))
                {
                    ApiKeyStore.Invalidate();
                    Debug.Log("[ApiKeyStore] Cache cleared — key will be re-read on next use.");
                }
            }

            EditorGUILayout.HelpBox(
                "The key file lives OUTSIDE the project and is never included in builds or source control.\n" +
                "Format: KEY_NAME=your_actual_key  (one per line, no spaces around '=')",
                UnityEditor.MessageType.None);
        }

        private static void OpenWithDefaultApp(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = path,
                    UseShellExecute = true
                });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ApiKeyStore] Could not open file: {e.Message}\nPath: {path}");
            }
        }
    }
}
