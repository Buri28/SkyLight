using SkyLight.Configuration;
using SkyLight.Controllers.Gameplay;
using Zenject;

namespace SkyLight.Installers
{
    /// <summary>
    /// ゲームプレイシーン（プレイ中）に RenderSettings 適用コントローラーを登録するインストーラー。
    /// </summary>
    public class SkyLightGameInstaller : Installer
    {
        private readonly PluginConfig _config;

        public SkyLightGameInstaller(PluginConfig config)
        {
            _config = config;
        }

        public override void InstallBindings()
        {
            // 自動テスト（--skylight-autotest のときだけ動く）。SkyLight が無効でも撮影はする。
            Container.BindInterfacesTo<AutoTest.AutoTestCapture>().AsSingle().NonLazy();

            if (!_config.Enabled) return;

            Container.BindInstance(_config).AsSingle();

            // RenderSettings を適用するコントローラー（Initialize / Tick / Dispose を使う）
            Container.BindInterfacesAndSelfTo<SkyLightController>()
                     .AsSingle()
                     .NonLazy();
        }
    }
}
