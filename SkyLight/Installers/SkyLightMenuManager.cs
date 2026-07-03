using BeatSaberMarkupLanguage.GameplaySetup;
using SkyLight.Configuration;
using SkyLight.Controllers;
using SkyLight.Controllers.Settings;
using System;
using Zenject;

namespace SkyLight.Installers
{
    /// <summary>
    /// ソロ選曲画面の MODS タブに「SkyLight」を追加します。
    /// </summary>
    public class SkyLightMenuManager : IInitializable, IDisposable
    {
        private readonly SkyLightSettingsController _settingsController;
        private readonly PluginConfig _config;

        public SkyLightMenuManager(SkyLightSettingsController settingsController, PluginConfig config)
        {
            _settingsController = settingsController;
            _config = config;
        }

        public void Initialize()
        {
            GameplaySetup.Instance.AddTab("SkyLight", "SkyLight.Views.SkyLightSettingsView.bsml", _settingsController);

            // 環境オーバーライド機能をSkyLight側に持たせられるか調査するための1回限りのダンプ。
            if (_config.DebugLogging)
            {
                try { MenuDiagnostics.DumpOnce(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Menu dump failed: {ex.Message}"); }
            }
        }

        public void Dispose()
        {
            GameplaySetup.Instance.RemoveTab("SkyLight");
        }
    }
}
