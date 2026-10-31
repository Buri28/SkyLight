using IPA;
using IPA.Config;
using IPA.Config.Stores;
using SkyLight.Configuration;
using SkyLight.Installers;
using SiraUtil.Zenject;
using IPALogger = IPA.Logging.Logger;

namespace SkyLight
{
    // IPAプラグインのエントリーポイント。BeatSaber起動時に1回だけ初期化される
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        // どこからでもログを書けるようにstaticで公開
        internal static IPALogger Log { get; private set; } = null!;
        // 設定ファイル（UserData/SkyLight.json）へのアクセス
        internal static PluginConfig Config { get; private set; } = null!;

        // 詳細ログ。Config.DebugLogging が ON のときだけ Debug レベルで出力する（既定OFF）。
        // Warn 以上も公開用に常時は出さず、DebugLogging が ON のときだけ DebugWarn で出す。
        internal static void DebugLog(string message)
        {
            if (Config != null && Config.DebugLogging)
                Log.Debug(message);
        }

        internal static void DebugWarn(string message)
        {
            if (Config != null && Config.DebugLogging)
                Log.Warn(message);
        }

        internal static bool IsDebugLoggingEnabled()
            => Config != null && Config.DebugLogging;

        internal static void DebugInfo(System.Func<string> messageFactory)
        {
            if (!IsDebugLoggingEnabled())
                return;
            Log.Info(messageFactory());
        }

        // IPAによってBeatSaber起動時に1回呼ばれるコンストラクタ
        [Init]
        public Plugin(IPALogger logger, Config conf, Zenjector zenjector)
        {
            Log             = logger;
            Config          = conf.Generated<PluginConfig>(); // IPAが設定ファイルを自動生成・ロード

            zenjector.UseLogger(logger);

            // ゲームプレイシーン（曲プレイ中）に RenderSettings 適用コントローラーを登録
            zenjector.Install<SkyLightGameInstaller>(Location.Player, Config);

            // メニューシーン（設定画面）に設定UIを登録
            zenjector.Install<SkyLightMenuInstaller>(Location.Menu, Config);

            DebugLog("SkyLight initialized.");
        }

        // アプリ起動時（Init後）に呼ばれる
        [OnStart]
        public void OnApplicationStart()
        {
            DebugLog("SkyLight started.");
        }

        // アプリ終了時に呼ばれる
        [OnExit]
        public void OnApplicationQuit()
        {
            DebugLog("SkyLight exited.");
        }
    }
}
