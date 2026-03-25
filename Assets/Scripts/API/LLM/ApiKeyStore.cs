using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Manages API keys stored in a file OUTSIDE the Unity project directory.
    /// Keys are never serialized into ScriptableObjects or included in builds.
    ///
    /// Key file location:
    ///   Windows : %USERPROFILE%\.las\api_keys.txt   (e.g. C:\Users\YourName\.las\api_keys.txt)
    ///   Mac/Linux: ~/.las/api_keys.txt
    ///
    /// File format — one entry per line, no spaces around the '=':
    ///   ANTHROPIC_API_KEY=sk-ant-...
    ///   OPENAI_API_KEY=sk-...
    ///   Lines starting with # are comments and are ignored.
    ///
    /// Usage:
    ///   string key = ApiKeyStore.GetKey("ANTHROPIC_API_KEY");
    ///
    /// Provider ScriptableObjects store only the KEY NAME (e.g. "ANTHROPIC_API_KEY"),
    /// never the key value itself.
    /// </summary>
    public static class ApiKeyStore
    {
        public static readonly string FolderName = ".las";
        public static readonly string FileName    = "api_keys.txt";

        /// <summary>Full path to the directory that holds the key file.</summary>
        public static string KeyDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                FolderName);

        /// <summary>Full path to the key file.</summary>
        public static string KeyFilePath => Path.Combine(KeyDirectory, FileName);

        private static Dictionary<string, string> _cache;
        private static DateTime _lastLoad = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the value for <paramref name="keyName"/>, checking in order:
        ///   1. External key file  (~/.las/api_keys.txt)
        ///   2. System environment variable
        /// Creates a template key file on first call if none exists.
        /// Returns empty string if the key is not found in either source.
        /// </summary>
        public static string GetKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return "";

            EnsureFileExists();
            RefreshCacheIfStale();

            if (_cache != null &&
                _cache.TryGetValue(keyName, out string v) &&
                !string.IsNullOrEmpty(v))
                return v;

            return Environment.GetEnvironmentVariable(keyName) ?? "";
        }

        /// <summary>
        /// Returns true if a non-empty value exists for <paramref name="keyName"/>
        /// in the key file or as an environment variable.
        /// </summary>
        public static bool HasKey(string keyName) => !string.IsNullOrEmpty(GetKey(keyName));

        /// <summary>Forces the cache to reload from disk on the next GetKey() call.</summary>
        public static void Invalidate() => _cache = null;

        /// <summary>
        /// Creates the key directory and a template file if they don't yet exist.
        /// Safe to call repeatedly — does nothing if the file already exists.
        /// </summary>
        public static void EnsureFileExists()
        {
            if (File.Exists(KeyFilePath)) return;

            try
            {
                Directory.CreateDirectory(KeyDirectory);
                File.WriteAllText(KeyFilePath, BuildTemplate());
                Debug.Log(
                    "[ApiKeyStore] API key file created.\n\n" +
                    $"  ► Location : {KeyFilePath}\n\n" +
                    "  Open the file and replace 'your_key_here' with your actual API keys.\n" +
                    "  This file is OUTSIDE your project and will never be included in builds.\n" +
                    "  Use the provider Inspector buttons to open or reveal the file.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ApiKeyStore] Could not create key file: {e.Message}");
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private static void RefreshCacheIfStale()
        {
            if (_cache != null && (DateTime.Now - _lastLoad) < CacheTtl) return;

            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(KeyFilePath)) return;

            foreach (string raw in File.ReadAllLines(KeyFilePath))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                // Strip trailing inline comments  (e.g.  sk-abc123  # my prod key)
                int commentIdx = val.IndexOf(" #", StringComparison.Ordinal);
                if (commentIdx >= 0) val = val.Substring(0, commentIdx).Trim();

                if (!string.IsNullOrEmpty(key) &&
                    !string.IsNullOrEmpty(val) &&
                    val != "your_key_here")
                {
                    _cache[key] = val;
                }
            }
            _lastLoad = DateTime.Now;
        }

        private static string BuildTemplate() =>
            "# LAS API Keys\n" +
            "# ════════════════════════════════════════════════════════════════════════\n" +
            "# This file is stored OUTSIDE your Unity project.\n" +
            "# It will NOT be included in builds and must NOT be committed to source control.\n" +
            "#\n" +
            $"# File location: {KeyFilePath}\n" +
            "#\n" +
            "# FORMAT  →  KEY_NAME=value\n" +
            "#   • No spaces around the '='\n" +
            "#   • Lines starting with # are ignored\n" +
            "#   • Each provider's Inspector shows which KEY_NAME it reads\n" +
            "#\n" +
            "# HOW TO FILL IN YOUR KEYS:\n" +
            "#   1. Replace 'your_key_here' on the line for the service you use.\n" +
            "#   2. Save the file (Ctrl+S).\n" +
            "#   3. In Unity, press 'Reload Key File' on the provider asset (or wait 30s).\n" +
            "# ════════════════════════════════════════════════════════════════════════\n" +
            "\n" +
            "ANTHROPIC_API_KEY=your_key_here\n" +
            "OPENAI_API_KEY=your_key_here\n" +
            "DEEPSEEK_API_KEY=your_key_here\n" +
            "MISTRAL_API_KEY=your_key_here\n";
    }
}
