using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyLight.Configuration
{
    /// <summary>
    /// 環境ごとに非表示にするオブジェクトの名前リスト（UserData/SkyLight/Environments/HideObjects.json）。
    ///
    /// 形式（環境の serializedName ごと）:
    ///   "DefaultEnvironment": {
    ///     "Hide": "BackColumns;RectangleFakeGlow",   // 非表示にする名前（; 区切り、部分一致）。既定は空＝何も隠さない
    ///     "Candidates": [ "BackColumns", ... ]         // その環境で見つかったオブジェクト名（選ぶときの参考）
    ///   }
    /// まだ載っていない環境で遊ぶと、その環境の Candidates を自動で書き足す（Hide は空のまま）。
    /// 非表示は Renderer を止めるだけで、オブジェクトは削除しない（曲が終わると元に戻る）。
    /// プリセット（UserData/SkyLight/*.json）と混ざらないよう、サブフォルダに置く。
    /// </summary>
    internal static class EnvironmentHideList
    {
        public static string FilePath => Path.Combine(PresetManager.PresetDir, "Environments", "HideObjects.json");

        // 最後に遊んだ環境（設定画面の「Last Played」で選ぶため。プレイ開始時に SkyLightController が入れる）。
        public static string LastPlayed { get; set; } = "";

        // 設定画面用：ファイルにある環境の一覧（serializedName と表示名）。
        public static List<(string key, string name)> ListEnvironments()
        {
            try
            {
                var root = Load();
                if (root == null) return new List<(string, string)>();
                return root.Properties()
                           .Select(p => (p.Name, (p.Value["Name"]?.Value<string>() ?? "")))
                           .OrderBy(e => string.IsNullOrEmpty(e.Item2) ? e.Name : e.Item2, StringComparer.OrdinalIgnoreCase)
                           .ToList();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] HideObjects.json read failed: {ex.Message}");
                return new List<(string, string)>();
            }
        }

        // 設定画面用：その環境の候補名。
        public static List<string> GetCandidates(string environment)
        {
            try
            {
                return (Load()?[environment]?["Candidates"] as JArray)?.Select(t => t.Value<string>() ?? "")
                       .Where(n => n.Length > 0).ToList() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        // 設定画面用：その環境の Hide を書き換えて保存する（ほかの環境・項目はそのまま）。
        public static void SetHideHints(string environment, string hints)
        {
            if (string.IsNullOrEmpty(environment)) return;
            try
            {
                var root = Load() ?? new JObject();
                if (!(root[environment] is JObject entry))
                {
                    entry = new JObject { ["Candidates"] = new JArray() };
                    root[environment] = entry;
                }
                entry["Hide"] = hints ?? "";
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] HideObjects.json write failed: {ex.Message}");
            }
        }

        // その環境の Hide（; 区切り）。ファイルや項目が無ければ空。
        public static string GetHideHints(string environment)
        {
            try
            {
                var root = Load();
                return (root?[environment]?["Hide"]?.Value<string>() ?? "").Trim();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] HideObjects.json read failed: {ex.Message}");
                return "";
            }
        }

        // その環境がまだファイルに無ければ、候補名を付けて書き足す（既にあれば何もしない＝ユーザーの編集を壊さない）。
        public static void AddEnvironmentIfMissing(string environment, IEnumerable<string> candidateNames)
        {
            if (string.IsNullOrEmpty(environment)) return;
            try
            {
                var root = Load() ?? new JObject();
                if (root[environment] != null) return;

                root[environment] = new JObject
                {
                    ["Hide"] = "",
                    // "Fence (12)" のような番号違いは "Fence" にまとめる（Hide は部分一致なので "Fence" で全部に当たる）。
                    ["Candidates"] = new JArray(candidateNames.Select(n => System.Text.RegularExpressions.Regex.Replace(n, @" \(\d+\)$", ""))
                                                              .Distinct(StringComparer.Ordinal)
                                                              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
                };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
                Plugin.Log.Info($"[SkyLight] Added '{environment}' to {FilePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[SkyLight] HideObjects.json write failed: {ex.Message}");
            }
        }

        private static JObject? Load()
        {
            if (!File.Exists(FilePath)) return null;
            return JObject.Parse(File.ReadAllText(FilePath));
        }
    }
}
