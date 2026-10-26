using SkyLight.Configuration;
using SkyLight.Controllers.Settings;
using Zenject;

namespace SkyLight.Installers
{
    /// <summary>
    /// メニューシーンに BSML 設定タブを追加するインストーラー。
    /// </summary>
    public class SkyLightMenuInstaller : Installer
    {
        private readonly PluginConfig _config;

        public SkyLightMenuInstaller(PluginConfig config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            Container.BindInstance(_config).AsSingle();

            // 自動テスト（--skylight-autotest のときだけ曲を自動で始める）
            Container.BindInterfacesTo<AutoTest.AutoTestLevelStarter>().AsSingle().NonLazy();

            // 設定画面コントローラー
            Container.Bind<SkyLightSettingsController>()
                     .FromNewComponentAsViewController()
                     .AsSingle();

            // ソロ選曲画面の MODS タブに追加
            Container.BindInterfacesTo<SkyLightMenuManager>()
                     .AsSingle()
                     .NonLazy();
        }
    }
}
