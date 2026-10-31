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
    ///     "Candidates": [ "BackColumns", ... ],        // その環境で見つかったオブジェクト名（選ぶときの参考）
    ///     "HiddenBy": { "BackColumns": "Structures" }  // 前回遊んだときに SkyLight の設定で消えていた物（一部だけなら "Part:…"）
    ///   }
    /// 遊ぶたびに、その環境の Candidates を書き足し、HiddenBy を書き直す（Hide は触らない）。
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
                Plugin.DebugWarn($"[SkyLight] HideObjects.json read failed: {ex.Message}");
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
                Plugin.DebugWarn($"[SkyLight] HideObjects.json write failed: {ex.Message}");
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
                Plugin.DebugWarn($"[SkyLight] HideObjects.json read failed: {ex.Message}");
                return "";
            }
        }

        // 候補に載せる名前。"Fence (12)" のような番号違いは "Fence" にまとめる（Hide は部分一致なので "Fence" で全部に当たる）。
        public static string CandidateName(string name)
            => System.Text.RegularExpressions.Regex.Replace(name ?? "", @" \(\d+\)$", "");

        // 設定画面用：前回遊んだときに SkyLight の設定で消えていた物（名前 → "Structures" / "Part:Structures" など）。
        public static Dictionary<string, string> GetHiddenBy(string environment)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Load()?[environment]?["HiddenBy"] is JObject o)
                    foreach (var p in o.Properties())
                        result[p.Name] = p.Value.Value<string>() ?? "";
            }
            catch { }
            return result;
        }

        // 遊ぶたびに呼ぶ。候補名を書き足し（今ある候補・Hide は消さない）、HiddenBy を今回の状態で置き換える。
        // 中身が変わらなければ書かない。
        public static void RecordPlayState(string environment, IEnumerable<string> candidateNames, IDictionary<string, string> hiddenBy)
        {
            if (string.IsNullOrEmpty(environment)) return;
            try
            {
                var root = Load() ?? new JObject();
                bool isNew = !(root[environment] is JObject);
                var entry = root[environment] as JObject ?? new JObject { ["Hide"] = "" };

                var names = new HashSet<string>(StringComparer.Ordinal);
                if (entry["Candidates"] is JArray old)
                    foreach (var t in old) { var s = t.Value<string>(); if (!string.IsNullOrEmpty(s)) names.Add(s!); }
                foreach (var n in candidateNames) if (!string.IsNullOrEmpty(n)) names.Add(n);
                var candidates = new JArray(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

                var hidden = new JObject();
                foreach (var kv in hiddenBy.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    hidden[kv.Key] = kv.Value;

                if (!isNew && JToken.DeepEquals(entry["Candidates"], candidates) && JToken.DeepEquals(entry["HiddenBy"], hidden))
                    return;

                entry["Candidates"] = candidates;
                entry["HiddenBy"] = hidden;
                root[environment] = entry;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
                if (isNew) Plugin.DebugLog($"[SkyLight] Added '{environment}' to {FilePath}");
            }
            catch (Exception ex)
            {
                Plugin.DebugWarn($"[SkyLight] HideObjects.json write failed: {ex.Message}");
            }
        }

        private static JObject? Load()
        {
            if (!File.Exists(FilePath)) return null;
            return JObject.Parse(File.ReadAllText(FilePath));
        }
    }
}
