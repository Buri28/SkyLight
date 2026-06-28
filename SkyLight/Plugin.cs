using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using SkyLight.Configuration;
using SkyLight.Installers;
using SiraUtil.Zenject;
using System.Reflection;
using IPALogger = IPA.Logging.Logger;

namespace SkyLight
{
    // IPAプラグインのエントリーポイント。BeatSaber起動時に1回だけ初期化される
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        // どこからでもログを書けるようにstaticで公開
        internal static IPALogger Log { get; private set; } = null!;
        // Harmonyパッチ（現在は未使用だが将来のパッチ用に確保）
        internal static Harmony HarmonyInstance { get; private set; } = null!;
        // 設定ファイル（UserData/SkyLight.json）へのアクセス
        internal static PluginConfig Config { get; private set; } = null!;

        // 詳細ログ。Config.DebugLogging が ON のときだけ Debug レベルで出力する（既定OFF）。
        // Warn/Error は従来どおり Plugin.Log.Warn/Error で常時出力する。
        internal static void DebugLog(string message)
        {
            if (Config != null && Config.DebugLogging)
                Log.Debug(message);
        }

        // IPAによってBeatSaber起動時に1回呼ばれるコンストラクタ
        [Init]
        public Plugin(IPALogger logger, Config conf, Zenjector zenjector)
        {
            Log             = logger;
            Config          = conf.Generated<PluginConfig>(); // IPAが設定ファイルを自動生成・ロード
            PluginConfig.Instance = Config;
            HarmonyInstance = new Harmony("com.buri28.skylight");

            zenjector.UseLogger(logger);

            // ゲームプレイシーン（曲プレイ中）に RenderSettings 適用コントローラーを登録
            zenjector.Install<SkyLightGameInstaller>(Location.Player, Config);

            // メニューシーン（設定画面）に設定UIを登録
            zenjector.Install<SkyLightMenuInstaller>(Location.Menu, Config);

            DebugLog("SkyLight initialized.");
        }

        // アプリ起動時（Init後）に呼ばれる。Harmonyパッチを適用する
        [OnStart]
        public void OnApplicationStart()
        {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            DebugLog("SkyLight started.");
        }

        // アプリ終了時に呼ばれる。Harmonyパッチを全て解除する
        [OnExit]
        public void OnApplicationQuit()
        {
            HarmonyInstance.UnpatchSelf();
            DebugLog("SkyLight exited.");
        }
    }
}
