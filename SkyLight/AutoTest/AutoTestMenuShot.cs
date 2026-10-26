using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using SkyLight.Configuration;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SkyLight.AutoTest
{
    /// <summary>
    /// 自動テスト用：メニューで SkyLight の設定画面を目の前に浮かべて撮影する（--skylight-menushot）。
    /// 本物の MODS タブまで画面遷移するのは手間なので、同じ BSML を同じコントローラーで浮かべた画面に作る。
    ///   menu-tab0.png 〜：タブごと / menu-last-bottom.png：最後のタブを一番下までスクロール
    ///   menu-hide-pressed.png：--skylight-env の環境で、--skylight-menushot-object の候補の Hide を押した状態
    /// Hide を押した分は撮影後に元へ戻す（HideObjects.json を書き換えたままにしない）。
    /// </summary>
    internal static class AutoTestMenuShot
    {
        public static IEnumerator Run(object settingsController)
        {
            var cam = Camera.main;
            if (cam == null) { AutoTestOptions.Status("FAIL menushot: no main camera"); yield break; }

            if (!string.IsNullOrEmpty(AutoTestOptions.EnvName))
                EnvironmentHideList.LastPlayed = AutoTestOptions.EnvName!;
            string originalHide = string.IsNullOrEmpty(AutoTestOptions.EnvName) ? "" : EnvironmentHideList.GetHideHints(AutoTestOptions.EnvName!);

            FloatingScreen? screen = null;
            try
            {
                var pos = cam.transform.position + cam.transform.forward * 2.0f;
                var rot = Quaternion.LookRotation(cam.transform.forward);
                screen = FloatingScreen.CreateFloatingScreen(new Vector2(130f, 95f), false, pos, rot, 0f, true);
                var content = Utilities.GetResourceContent(typeof(Plugin).Assembly, "SkyLight.Views.SkyLightSettingsView.bsml");
                BSMLParser.Instance.Parse(content, screen.gameObject, settingsController);
                AutoTestOptions.Status("MENUSHOT parsed");
            }
            catch (Exception ex)
            {
                AutoTestOptions.Status("FAIL menushot parse: " + ex.GetType().Name + ": " + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
                Plugin.Log.Warn($"[SkyLight][autotest] menushot parse failed: {ex}");
                yield break;
            }

            yield return new WaitForSeconds(1.5f);

            // タブを1つずつ選んで撮る（menu-tab0.png 〜）。タブが無い画面なら1枚だけ。
            var selector = FindComponents(screen.gameObject, "TabSelector").FirstOrDefault();
            int tabCount = selector != null ? CountTabs(screen.gameObject) : 1;
            for (int i = 0; i < tabCount; i++)
            {
                if (selector != null) SelectTab(selector, i);
                yield return new WaitForSeconds(1.0f);
                yield return Shot($"menu-tab{i}.png");
            }

            // 最後のタブ（Hide）を一番下までスクロールして撮る。
            // ドロップダウンの中にも（見えていない）ScrollView があるので、表示中のものすべてを送る。
            foreach (var sv in FindComponents(screen.gameObject, "ScrollView"))
            {
                CallIfExists(sv, "UpdateContentSize");
                CallIfExists(sv, "ScrollToEnd", false);
            }
            yield return new WaitForSeconds(1.0f);
            yield return Shot("menu-last-bottom.png");

            // 候補を選んで Hide を押した状態。
            var obj = AutoTestOptions.MenuShotObject;
            if (!string.IsNullOrEmpty(AutoTestOptions.EnvName) && !string.IsNullOrEmpty(obj))
            {
                var t = settingsController.GetType();
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                t.GetMethod("SelectEnvObject", F)?.Invoke(settingsController, new object[] { obj! });
                t.GetMethod("HideSelectedObject", F)?.Invoke(settingsController, null);
                yield return new WaitForSeconds(1.0f);
                yield return Shot("menu-hide-pressed.png");
                AutoTestOptions.Status($"MENUSHOT hide after press: '{EnvironmentHideList.GetHideHints(AutoTestOptions.EnvName!)}'");
                EnvironmentHideList.SetHideHints(AutoTestOptions.EnvName!, originalHide); // 元に戻す
            }

            yield return new WaitForSeconds(1.0f);
            AutoTestOptions.Status("DONE");
            if (AutoTestOptions.Quit) Application.Quit();
        }

        private static int CountTabs(GameObject root)
            => root.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name == "Tab");

        // BSML TabSelector の非公開メソッド TabSelected(SegmentedControl, int) と、見た目の選択(SelectCellWithNumber)。
        private static void SelectTab(Component selector, int index)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var control = selector.GetType().GetProperty("TextSegmentedControl", F)?.GetValue(selector);
            CallIfExists(control, "SelectCellWithNumber", index);
            selector.GetType().GetMethod("TabSelected", F)?.Invoke(selector, new object?[] { null, index });
        }

        private static IEnumerator Shot(string file)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(AutoTestOptions.OutputDir, file);
            ScreenCapture.CaptureScreenshot(path);
            AutoTestOptions.Status($"SHOT {path}");
            yield return new WaitForSeconds(0.5f);
        }

        private static System.Collections.Generic.IEnumerable<Component> FindComponents(GameObject root, string typeName)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(false))
                if (c != null && c.GetType().Name.EndsWith(typeName)) yield return c;
        }

        private static void CallIfExists(object? target, string method, params object[] args)
        {
            if (target == null) return;
            try { target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(target, args); }
            catch (Exception ex) { Plugin.Log.Warn($"[SkyLight][autotest] {method} failed: {ex.Message}"); }
        }
    }
}
