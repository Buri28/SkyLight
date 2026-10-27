using System.Collections;
using System.IO;
using UnityEngine;
using Zenject;

namespace SkyLight.AutoTest
{
    /// <summary>
    /// 曲が始まったら決まった秒数ごとに画面を PNG で保存し、撮り終えたらゲームを終了する
    /// （--skylight-autotest のときだけ）。保存先は UserData/SkyLight/AutoTest/shot-N.png。
    /// </summary>
    internal class AutoTestCapture : IInitializable
    {
        private static bool _captured; // 1回の起動で1回だけ

        public void Initialize()
        {
            if (!AutoTestOptions.Enabled || _captured) return;
            _captured = true;
            AutoTestOptions.Status("GAMECORE loaded");
            AutoTestRunner.Instance.StartCoroutine(Capture());
        }

        private static IEnumerator Capture()
        {
            yield return new WaitForSeconds(AutoTestOptions.Delay);

            // 非表示にできるオブジェクトの候補一覧を保存する（全環境の候補集め用）。
            try
            {
                var text = Controllers.SceneDiagnostics.DumpHideCandidates(Plugin.Config);
                File.WriteAllText(Path.Combine(AutoTestOptions.OutputDir, "candidates.txt"), text);
                AutoTestOptions.Status("CANDIDATES saved");
            }
            catch (System.Exception ex) { AutoTestOptions.Status("WARN candidates: " + ex.Message.Replace(System.Environment.NewLine, " ")); }
            for (int i = 1; i <= AutoTestOptions.Shots; i++)
            {
                var path = Path.Combine(AutoTestOptions.OutputDir, $"shot-{i}.png");
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(path);
                AutoTestOptions.Status($"SHOT {path} ({Screen.width}x{Screen.height}) {BloomState()}");
                if (i < AutoTestOptions.Shots)
                    yield return new WaitForSeconds(AutoTestOptions.Interval);
            }

            // CaptureScreenshot はフレームの終わりに書き出すので、少し待ってから終了する。
            yield return new WaitForSeconds(2f);
            AutoTestOptions.Status("DONE");
            if (AutoTestOptions.Quit)
                Application.Quit();
        }

        // 撮影した瞬間に全画面ブルームが本当に効いているか（設定値ではなく実物の enabled）を書く。
        // 設定の Bloom が ON でも MOD 側で止めていた、という見落としを防ぐため。
        private static string BloomState()
        {
            var sb = new System.Text.StringBuilder("bloom:");
            int n = 0;
            foreach (var b in Resources.FindObjectsOfTypeAll<Behaviour>())
            {
                if (b == null || b.GetType().Name != "MainEffectController" || !b.gameObject.scene.IsValid()) continue;
                sb.Append($" {b.gameObject.name}={(b.isActiveAndEnabled ? "ON" : "OFF")}");
                n++;
            }
            if (n == 0) sb.Append(" none");
            return sb.ToString();
        }
    }
}
