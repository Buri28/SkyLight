using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SkyLight.AutoTest
{
    /// <summary>
    /// 自動テストの設定。起動引数で指定する（設定ファイルは使わないので普段のプレイには影響しない）。
    ///   --skylight-autotest          自動テストを有効にする（これが無いと何もしない）
    ///   --skylight-level=ID          曲の levelID（省略時は OST の先頭の曲）
    ///   --skylight-diff=Expert       難易度（Easy/Normal/Hard/Expert/ExpertPlus）
    ///   --skylight-delay=5           曲が始まってから1枚目を撮るまでの秒数
    ///   --skylight-shots=3           撮る枚数
    ///   --skylight-interval=2        撮る間隔（秒）
    ///   --skylight-noquit            撮り終えてもゲームを終了しない
    ///   --skylight-env=NAME          環境（serializedName。例 DefaultEnvironment）を指定して曲を始める
    ///   --skylight-listenvs          曲を始めず、通常の環境の一覧を envs.txt に書いて終了する
    ///   --skylight-menushot          曲を始めず、設定画面を浮かべて撮影する（AutoTestMenuShot）
    ///   --skylight-menushot-object=X menushot で、--skylight-env の環境の候補 X で Hide を押した状態も撮る
    /// 結果は UserData/SkyLight/AutoTest/ に shot-N.png と status.txt（最後の行が DONE / FAIL）。
    /// </summary>
    internal static class AutoTestOptions
    {
        public static readonly bool Enabled;
        public static readonly string? LevelId;
        public static readonly string Difficulty = "Expert";
        public static readonly float Delay = 5f;
        public static readonly int Shots = 3;
        public static readonly float Interval = 2f;
        public static readonly bool Quit = true;
        public static readonly string? EnvName;
        public static readonly bool ListEnvs;
        public static readonly bool MenuShot;
        public static readonly string? MenuShotObject;

        static AutoTestOptions()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return; }

            foreach (var a in args)
            {
                if (a.Equals("--skylight-autotest", StringComparison.OrdinalIgnoreCase)) Enabled = true;
                else if (a.Equals("--skylight-noquit", StringComparison.OrdinalIgnoreCase)) Quit = false;
                else if (a.Equals("--skylight-listenvs", StringComparison.OrdinalIgnoreCase)) ListEnvs = true;
                else if (TryValue(a, "--skylight-env=", out var env)) EnvName = env;
                else if (a.Equals("--skylight-menushot", StringComparison.OrdinalIgnoreCase)) MenuShot = true;
                else if (TryValue(a, "--skylight-menushot-object=", out var mo)) MenuShotObject = mo;
                else if (TryValue(a, "--skylight-level=", out var v)) LevelId = v;
                else if (TryValue(a, "--skylight-diff=", out v)) Difficulty = v;
                else if (TryValue(a, "--skylight-delay=", out v) && TryFloat(v, out var f)) Delay = f;
                else if (TryValue(a, "--skylight-shots=", out v) && int.TryParse(v, out var n)) Shots = Math.Max(1, n);
                else if (TryValue(a, "--skylight-interval=", out v) && TryFloat(v, out f)) Interval = f;
            }
        }

        public static string OutputDir
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(root, "UserData", "SkyLight", "AutoTest");
            }
        }

        // status.txt に1行追記し、ログにも出す。外部スクリプトはこのファイルで進み具合と終了を知る。
        public static void Status(string line)
        {
            Plugin.Log.Info($"[SkyLight][autotest] {line}");
            try
            {
                Directory.CreateDirectory(OutputDir);
                File.AppendAllText(Path.Combine(OutputDir, "status.txt"),
                    $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight][autotest] status write failed: {ex.Message}"); }
        }

        private static bool TryValue(string arg, string prefix, out string value)
        {
            value = "";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            value = arg.Substring(prefix.Length).Trim('"');
            return value.Length > 0;
        }

        private static bool TryFloat(string s, out float f)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        public static string Describe()
            => $"level={LevelId ?? "(first custom/OST)"} env={EnvName ?? "(map default)"} listenvs={ListEnvs} diff={Difficulty} delay={Delay} shots={Shots} interval={Interval} quit={Quit} " +
               $"args=[{string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}]";
    }
}
