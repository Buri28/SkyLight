using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SkyLight.Configuration
{
    /// <summary>
    /// プリセット（設定一式）を <c>UserData/SkyLight/</c> フォルダ内の json ファイルとして
    /// 保存・読込・列挙・削除する。各プリセットは PluginConfig の全フィールドのスナップショット。
    ///
    /// 値の対象は PluginConfig の public な仮想プロパティ（bool/int/float/string）のみ。
    /// リフレクションで列挙するので、PluginConfig に項目を足しても自動で含まれる。
    /// </summary>
    internal static class PresetManager
    {
        // UserData/SkyLight/。Beat Saber_Data の親（インストールルート）配下。
        public static string PresetDir
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(root, "UserData", "SkyLight");
            }
        }

        // プリセット対象プロパティ（bool/int/float/string、get/set 両方あり）。
        private static IEnumerable<PropertyInfo> ConfigProps()
        {
            return typeof(PluginConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => p.PropertyType == typeof(bool)
                         || p.PropertyType == typeof(int)
                         || p.PropertyType == typeof(float)
                         || p.PropertyType == typeof(string));
        }

        private static string PathFor(string name) => Path.Combine(PresetDir, SanitizeFileName(name) + ".json");

        // ファイル名に使えない文字を除去する。
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string((name ?? "").Trim().Where(c => Array.IndexOf(invalid, c) < 0).ToArray());
            return string.IsNullOrEmpty(clean) ? "preset" : clean;
        }

        /// <summary>保存済みプリセット名（拡張子なし）を昇順で返す。</summary>
        public static List<string> List()
        {
            try
            {
                if (!Directory.Exists(PresetDir)) return new List<string>();
                return Directory.GetFiles(PresetDir, "*.json")
                                .Select(Path.GetFileNameWithoutExtension)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] Preset list failed: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>現在の設定を name のプリセットとして保存する（上書き）。</summary>
        public static bool Save(string name, PluginConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(name) || cfg == null) return false;
            try
            {
                Directory.CreateDirectory(PresetDir);
                var obj = new JObject();
                foreach (var p in ConfigProps())
                    obj[p.Name] = JToken.FromObject(p.GetValue(cfg) ?? "");
                File.WriteAllText(PathFor(name), obj.ToString(Formatting.Indented));
                Plugin.DebugLog($"[SkyLight] Preset saved: {name}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] Preset save failed ({name}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// name のプリセットを読み込み、cfg に反映する。プリセットを「完全なスナップショット」として扱い、
        /// ファイルに無い項目は既定値へ戻す（古いスキーマのプリセットでも予測可能な状態になる）。
        /// </summary>
        public static bool Load(string name, PluginConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(name) || cfg == null) return false;
            var path = PathFor(name);
            if (!File.Exists(path))
            {
                Plugin.Log.Warn($"[SkyLight] Preset not found: {name}");
                return false;
            }
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var defaults = new PluginConfig(); // ファイルに無い項目はこの既定値に戻す
                foreach (var p in ConfigProps())
                {
                    object value;
                    if (obj.TryGetValue(p.Name, out var tok) && tok.Type != JTokenType.Null)
                    {
                        value =
                            p.PropertyType == typeof(bool)   ? (object)tok.Value<bool>() :
                            p.PropertyType == typeof(int)    ? (object)tok.Value<int>() :
                            p.PropertyType == typeof(float)  ? (object)tok.Value<float>() :
                                                               (object)(tok.Value<string>() ?? "");
                    }
                    else
                    {
                        value = p.GetValue(defaults); // プリセットに無い項目は既定値
                    }
                    p.SetValue(cfg, value);
                }
                cfg.Changed();
                Plugin.DebugLog($"[SkyLight] Preset loaded: {name}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] Preset load failed ({name}): {ex.Message}");
                return false;
            }
        }

        /// <summary>name のプリセットを削除する。</summary>
        public static bool Delete(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var path = PathFor(name);
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] Preset delete failed ({name}): {ex.Message}");
                return false;
            }
        }
    }
}
