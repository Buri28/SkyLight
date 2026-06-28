using BeatSaberMarkupLanguage.GameplaySetup;
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

        public SkyLightMenuManager(SkyLightSettingsController settingsController)
        {
            _settingsController = settingsController;
        }

        public void Initialize()
        {
            GameplaySetup.Instance.AddTab("SkyLight", "SkyLight.Views.SkyLightSettingsView.bsml", _settingsController);
        }

        public void Dispose()
        {
            GameplaySetup.Instance.RemoveTab("SkyLight");
        }
    }
}
