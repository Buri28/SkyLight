using IPA.Config.Stores;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace SkyLight.Configuration
{
    // SkyLight の設定。UserData/SkyLight.json に永続化される。
    // 色は hex(#RRGGBB) 文字列で保持し、適用時に Color へ変換する（BSML の color-setting とも相性が良い）。
    public class PluginConfig
    {
        // MOD 有効/無効
        public virtual bool Enabled { get; set; } = true;

        // ログ出力フラグ（SkyLight.json だけで変える。設定画面には出さない）。
        // true のときだけ、Warn を含む SkyLight のログと診断ダンプを出す（既定 OFF）。
        // 既存の SkyLight.json に値があればそちらが優先される。
        public virtual bool DebugLogging { get; set; } = false;

        // シーン診断ダンプ（SkyLight.json だけで変える）。DebugLogging も true のときだけ、曲開始時にカメラ一覧・
        // シェーダー在庫・RenderSettings・Hide 候補をログ出力する。DebugLogging が false なら、これが true でも何も出さない。
        public virtual bool DumpScene { get; set; } = false;

        // 全画面ブルーム(MainEffectController)の ON/OFF。true=ON（ノーツ/レーザー発光あり）。
        public virtual bool Bloom { get; set; } = true;

        // ── 着色する対象には Color / Brightness / Alpha(不透明度) がある。
        //    Alpha=1 は不透明（アルファ 0 で描くので Bloom ON でも白飛びしない）、低いほど後ろが透ける（半透明）。
        //    Brightness は色のRGBを倍率でスケール。

        // ─── 背景（空ドーム） ───────────────────────────────
        // 元の背景（BloomSkyboxQuad）を隠して、空ドームで色を乗せる（Alpha<1 のときは元の背景が透ける）。
        public virtual bool RecolorBackground { get; set; } = true;
        public virtual string BackgroundColor { get; set; } = "#778BE2";
        public virtual float BackgroundBrightness { get; set; } = 1.0f;
        // Alpha=1: 元の背景を隠して不透明に塗り替える（従来通り、白飛びに強い）。
        // Alpha<1: 元の背景を隠さず、本物の半透明ブレンドで色を重ねる（奥の景色が透ける）。
        public virtual float BackgroundAlpha { get; set; } = 1.0f;
        public virtual string BackgroundShaderHints { get; set; } = "BloomSkyboxQuad;Skybox";

        // ネオン/レーザー（NeonLight レイヤー）を非表示にする。横の動くネオンバーや水色ビームを消す。
        // これらは BloomPrePass で描かれ色塗り不可のため、カメラの描画対象から外して隠す方式。
        public virtual bool HideNeon { get; set; } = false;
        // CustomBackgrounds（別 MOD）の背景画像を曲中に表示するか。Neon の表示/非表示とは別に選べる。
        public virtual bool ShowCustomBackground { get; set; } = true;

        // ─── 任意オブジェクトの非表示 ───────────────────────────────
        // 名前/パス/シェーダー名の部分一致（; 区切り）で対象を指定して非表示にする。
        // 環境ごとの非表示は、設定画面の [Hide] タブ（UserData/SkyLight/Environments/HideObjects.json）で選べる。
        // ここは全環境共通で消したい名前を書く。名前は、DebugLogging と DumpScene を true にして曲を開始し、ログの
        // "[SkyLight][diag] === Hide candidates ===" からコピーしてもよい。
        public virtual string HideObjectHints { get; set; } = "";
        public virtual string HideObjectExcludeHints { get; set; } = "";

        // ─── 床（反射床/Mirror） ───────────────────────────────
        public virtual bool ShowFloor { get; set; } = true;
        public virtual bool ShowSideLanes { get; set; } = true;
        public virtual bool PaintFloor { get; set; } = true;
        // ゲームのミラー画質がオンのときは、床をゲームの反射床のままにして空とノーツを映す。
        // ミラー画質がオフ、またはこれが OFF のときは FloorColor で直接塗る。
        public virtual bool MirrorReflection { get; set; } = true;
        public virtual string FloorColor { get; set; } = "#68696C";
        public virtual float FloorBrightness { get; set; } = 1.0f;
        public virtual float FloorAlpha { get; set; } = 1.0f;
        public virtual string FloorPaintShaderHint { get; set; } = "Mirror";
        public virtual string SideLaneHints { get; set; } = "TrackConstruction;FloorConstruction;TrackLane;Lane;Road";

        // ─── 表示/非表示のみの対象（Side Lanes と同方式） ───────────────────
        // ライト（名前に Light を含むオブジェクト）。
        public virtual bool ShowLights { get; set; } = true;
        public virtual string LightHints { get; set; } = "Light";
        // ロゴ（環境名ロゴ等）。
        public virtual bool ShowLogo { get; set; } = true;
        public virtual string LogoHints { get; set; } = "Logo";
        // スコア横の HUD パネルの背景（BasicGameHUD の LeftPanel/RightPanel の BG＝縦に長い半透明の板、Top＝上端の線）。
        // 黒い背景では見えないが、空を明るくすると濃い帯として見えるので、既定で隠す。
        public virtual bool ShowHudBackground { get; set; } = false;
        public virtual string HudBackgroundHints { get; set; } = "BasicGameHUD/LeftPanel/BG;BasicGameHUD/RightPanel/BG;BasicGameHUD/LeftPanel/Top;BasicGameHUD/RightPanel/Top";
        // その他の構造物（環境固有の演出オブジェクト群）。
        public virtual bool ShowOtherStructures { get; set; } = true;
        public virtual string OtherStructureHints { get; set; } = "Window;Tunnel;MagicDoor;Waterfall;Sun;Rain;Clouds;Aurora;Smoke;Flipbook;Video;Building;Mountain;Orbit;Star;Graffiti;HitQuad;Dust;DiamondMirror";

        // ─── 構造物（黒いシルエットの構造物。名前/シェーダーで対象指定） ───────────────────
        public virtual bool ShowStructures { get; set; } = true;
        public virtual bool PaintStructures { get; set; } = true;
        // true=指定色で着色 / false=元の色を残し透明度(StructureAlpha)だけ下げる。
        public virtual bool StructureColorize { get; set; } = true;
        public virtual string StructureColor { get; set; } = "#A695CA";
        public virtual float StructureBrightness { get; set; } = 1.0f;
        public virtual float StructureAlpha { get; set; } = 0.7f;
        // 対象ヒント（名前/シェーダー名に部分一致、; 区切り）。
        public virtual string StructureShaderHints { get; set; } = "SimpleLit";
        // 除外ヒント（床・ノーツ・空などを巻き込まないため）。
        public virtual string StructureExcludeHints { get; set; } = "Mirror;Note;Saber;Arrow;Bomb;Skybox;BloomSkyboxQuad;Ring;TrackConstruction;Spectrogram;";
        // 除外の例外（除外ヒントに一致しても、こちらに一致すれば構造物の対象に戻す）。
        // 例: 除外の "Mirror" に巻き込まれる "DiamondMirror" を対象に戻したい場合に "DiamondMirror" を指定。
        public virtual string StructureExcludeOverrideHints { get; set; } = "MirrorMesh;TrackMirror;";

        // ─── バー（Spectrogram の黒バー） ───────────────────
        public virtual bool ShowBars { get; set; } = true;
        public virtual string BarShaderHints { get; set; } = "Spectrogram";
        public virtual string BarExcludeHints { get; set; } = "";

        // ─── リング（名前に"Ring"を含む構造物） ───────────────────
        public virtual bool ShowRing { get; set; } = true;
        public virtual bool PaintRing { get; set; } = true;
        // リング対象のヒント（名前/シェーダー名に部分一致、; 区切り）。環境により "Ring" 名が無いので変更可。
        public virtual string RingShaderHints { get; set; } = "Ring";
        public virtual string RingExcludeHints { get; set; } = "";
        public virtual bool RingColorize { get; set; } = true;
        public virtual string RingColor { get; set; } = "#416CD6";
        public virtual float RingBrightness { get; set; } = 0.7f;
        public virtual float RingAlpha { get; set; } = 0.2f;

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
