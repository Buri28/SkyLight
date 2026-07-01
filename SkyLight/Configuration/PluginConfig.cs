using IPA.Config.Stores;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace SkyLight.Configuration
{
    // SkyLight の設定。UserData/SkyLight.json に永続化される。
    // 色は hex(#RRGGBB) 文字列で保持し、適用時に Color へ変換する（BSML の color-setting とも相性が良い）。
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; } = null!;

        // MOD 有効/無効
        public virtual bool Enabled { get; set; } = true;

        // 詳細ログ出力フラグ。true のときだけ Debug レベルの診断ログを出す。
        public virtual bool DebugLogging { get; set; } = false;

        // シーン診断ダンプ。曲開始時にカメラ一覧・シェーダー在庫・RenderSettings をログ出力する（DebugLogging が前提）。
        public virtual bool DumpScene { get; set; } = false;


        // カメラ背景（Skybox/SolidColor クリア）を背景色で塗る保険。
        public virtual bool FillBackground { get; set; } = true;

        // 全画面ブルーム(MainEffectController)の ON/OFF。true=ON（ノーツ/レーザー発光あり）。
        public virtual bool Bloom { get; set; } = true;

        // ── 着色は全対象「半透明（B方式）」で行う。各対象に Color / Brightness / Alpha(不透明度) を持つ。
        //    Alpha=1 で不透明、低いほど後ろが透ける。Brightness は色のRGBを倍率でスケール。

        // ─── 背景（空ドーム） ───────────────────────────────
        // 元の背景を隠さず、半透明ドームを被せて色を乗せる（Alpha<1 で元の背景が透ける）。
        public virtual bool RecolorBackground { get; set; } = true;
        public virtual string BackgroundColor { get; set; } = "#778BE2";
        public virtual float BackgroundBrightness { get; set; } = 1.0f;
        public virtual int BackgroundLayer { get; set; } = 0;
        public virtual string FloorShaderHint { get; set; } = "SimpleLit"; // レイヤー自動検出用
        public virtual string BackgroundShaderHints { get; set; } = "BloomSkyboxQuad;Skybox"; // 予約（未使用）

        // ネオン/レーザー（NeonLight レイヤー）を非表示にする。横の動くネオンバーや水色ビームを消す。
        // これらは BloomPrePass で描かれ色塗り不可のため、カメラの描画対象から外して隠す方式。
        public virtual bool HideNeon { get; set; } = false;

        // ─── 床（反射床/Mirror） ───────────────────────────────
        public virtual bool ShowFloor { get; set; } = true;
        public virtual bool ShowSideLanes { get; set; } = true;
        public virtual bool PaintFloor { get; set; } = true;
        public virtual string FloorColor { get; set; } = "#68696C";
        public virtual float FloorBrightness { get; set; } = 1.0f;
        public virtual float FloorAlpha { get; set; } = 1.0f;
        public virtual string FloorPaintShaderHint { get; set; } = "Mirror";
        public virtual string SideLaneHints { get; set; } = "TrackConstruction;TrackLane;Lane;Road";

        // ─── 構造物（黒いシルエットの構造物。名前/シェーダーで対象指定） ───────────────────
        public virtual bool ShowStructures { get; set; } = true;
        public virtual bool PaintStructures { get; set; } = true;
        // true=指定色で着色 / false=元の色を残し透明度(StructureAlpha)だけ下げる。
        public virtual bool StructureColorize { get; set; } = true;
        public virtual string StructureColor { get; set; } = "#A695CA";
        public virtual float StructureBrightness { get; set; } = 1.0f;
        public virtual float StructureAlpha { get; set; } = 0.6999999f;
        // 対象ヒント（名前/シェーダー名に部分一致、; 区切り）。
        public virtual string StructureShaderHints { get; set; } = "SimpleLit";
        // 除外ヒント（床・ノーツ・空などを巻き込まないため）。
        public virtual string StructureExcludeHints { get; set; } = "Mirror;Note;Saber;Arrow;Bomb;Skybox;BloomSkyboxQuad;Ring;TrackConstruction;Spectrogram";

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
        public virtual float RingBrightness { get; set; } = 0.6999999f;
        public virtual float RingAlpha { get; set; } = 0.2999998f;

        // Changed イベント（IPA が生成するストアのためのメソッド）
        public virtual void Changed() { }

        public virtual void OnReload() { }
    }
}
