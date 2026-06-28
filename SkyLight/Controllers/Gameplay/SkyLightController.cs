using SkyLight.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace SkyLight.Controllers.Gameplay
{
    /// <summary>
    /// ゲームプレイシーンで <see cref="RenderSettings"/> とカメラ背景を書き換え、ステージを明るくし
    /// 黒い虚無を空色で塗ってノーツとの明暗差を縮めるコントローラー。ブレ（残像）解消の本体。
    ///
    /// CustomPlatforms は非同期にプラットフォームをロードし、その際 RenderSettings を上書きしうるため、
    /// 既定では毎フレーム再適用してこちらの設定で確実に「勝つ」。
    /// </summary>
    public class SkyLightController : IInitializable, ITickable, ILateTickable, IDisposable
    {
        private readonly PluginConfig _config;

        // 半透明の空ドームで背景を着色する（元の背景の上に被せる）。
        private readonly SkyBackdrop _backdrop = new();
        // 全画面ブルームの ON/OFF。
        private readonly BloomTamer _bloomTamer = new();
        // 床：Bloom ON は FloorColor を反射ドームに塗って反射、Bloom OFF はフラット塗り。
        private readonly TargetPainter _floorFlat = new("floor", alwaysReassign: true);
        // 構造物・リングは半透明で着色するペインター。
        private readonly TargetPainter _structures = new("struct");
        private readonly TargetPainter _ring = new("ring");

        private bool _active;
        private int _frame;

        // 触ったカメラの元 clearFlags / 背景色（復元用）。MainCamera はシーンをまたいで生き残るため必ず戻す。
        private readonly Dictionary<Camera, (CameraClearFlags flags, Color bg)> _origCameras = new();

        [Inject]
        public SkyLightController(PluginConfig config)
        {
            _config = config;
        }

        public void Initialize()
        {
            if (!_config.Enabled) return;

            if (_config.DebugLogging && _config.DumpScene)
            {
                try { SceneDiagnostics.Dump(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Scene dump failed: {ex.Message}"); }
            }

            if (_config.DebugLogging)
            {
                try { BloomDiagnostics.DumpOnce(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Bloom dump failed: {ex.Message}"); }
            }

            _bloomTamer.Capture();

            // 空/床/リングの選択と Bloom の関係に応じて、ドーム・床・リング・ブルームをまとめて適用する。
            ApplyMode();

            Apply();
            _active = true;
            Plugin.DebugLog("[SkyLight] Applied RenderSettings on gameplay scene.");
        }

        // CustomPlatforms 等が後から RenderSettings を上書きしても、毎フレーム上書きし返す。
        public void Tick()
        {
            if (!_active) return;
            Apply();
        }

        // 背景の差し替え保証は LateTick（通常の Update 後）で行い、環境のライティング更新に勝つ。
        public void LateTick()
        {
            if (!_active) return;

            _frame++;

            // 空/床/リング/Bloom のモードを毎フレーム適用（構造変化時のみ作り直し、色は毎フレーム維持）。
            ApplyMode();

            // クオリティ設定確定後に MainEffectController の実体が遅れて生成されることがあるため、定期再収集。
            if (_frame % 60 == 0)
                _bloomTamer.Refresh();
        }

        // ─── 着色の適用（全対象 半透明＝B方式） ───────────────────────────
        // Bloom は独立トグル。各対象（背景/床/構造物/リング）を Color×Brightness と Alpha(不透明度) で半透明着色。
        // 空ドームの半径（内部固定）。カメラの far クリップに収まり、かつ構造物より外側に出ない値。
        private const float DomeScale = 60f;
        private const int TargetRefreshInterval = 120;
        private bool _domeBuilt;
        private int _floorMode; // 0=Bloom ONでカメラ背景に映す / 1=Bloom OFFでフラット塗り
        private bool _floorPainted;
        private bool _structPainted;
        private bool _ringPainted;

        private void ApplyMode()
        {
            // Sky Background ON の間は Bloom を強制OFFにして背景ドームを表示する。
            bool bloomOn = _config.Bloom && !_config.RecolorBackground;
            _bloomTamer.Reassert(!bloomOn);

            bool useBackdrop = _config.RecolorBackground;

            // 背景ドーム。Sky Background ON 時に元背景を置き換える。
            if (useBackdrop && !_domeBuilt)
            {
                _backdrop.Build(_config.BackgroundShaderHints, _config.BackgroundLayer, 0,
                                GetDomeColor(), DomeScale, _config.FloorShaderHint);
                _domeBuilt = true;
            }
            else if (!useBackdrop && _domeBuilt)
            {
                _backdrop.Cleanup();
                _domeBuilt = false;
            }
            if (_domeBuilt)
            {
                _backdrop.Reassert();
                _backdrop.SetColor(GetDomeColor());
            }

            // 床の色は FloorColor。塗り方が Bloom で違う：
            //  ① Bloom ON  → メインカメラの backgroundColor を FloorColor にする（ミラーが反射＝床に映る・直接背景は据え置きで白飛びしない）。Apply() で処理。
            //  ② Bloom OFF → 床に直接フラット塗り。
            int wantFloorMode = (_config.PaintFloor && !bloomOn) ? 1 : 0;
            if (wantFloorMode != _floorMode)
            {
                _floorFlat.Restore();
                _floorPainted = false;
                _floorMode = wantFloorMode;
            }
            if (_floorMode == 1)
            {
                UpdateTarget(_floorFlat, true, ref _floorPainted,
                    _config.FloorPaintShaderHint, "", disableMirror: true, colorize: true,
                    MakeColor(_config.FloorColor, _config.FloorBrightness, _config.FloorAlpha, new Color(0.13f, 0.16f, 0.19f)));
            }

            // 構造物（名前/シェーダーヒントで対象、除外あり）。Colorize=false なら元の色のまま透明度だけ。
            UpdateTarget(_structures, _config.PaintStructures, ref _structPainted,
                _config.StructureShaderHints, _config.StructureExcludeHints, disableMirror: false,
                _config.StructureColorize,
                MakeColor(_config.StructureColor, _config.StructureBrightness, _config.StructureAlpha, new Color(0.25f, 0.38f, 0.63f)));

            // リング。対象は RingShaderHints で指定（環境により "Ring" 名が無いため）。
            UpdateTarget(_ring, _config.PaintRing, ref _ringPainted,
                GetEffectiveRingHints(), _config.RingExcludeHints, disableMirror: false, _config.RingColorize,
                MakeColor(_config.RingColor, _config.RingBrightness, _config.RingAlpha, new Color(0.227f, 0.627f, 1f)));
        }

        private void UpdateTarget(TargetPainter p, bool want, ref bool painted, string hints, string exclude, bool disableMirror, bool colorize, Color rgba)
        {
            if (want && !painted)
            {
                p.Collect(hints, exclude, disableMirror, colorize);
                painted = true;
            }
            else if (want && _frame % TargetRefreshInterval == 0 && !p.HasLiveTargets)
            {
                p.Restore();
                p.Collect(hints, exclude, disableMirror, colorize);
            }
            else if (!want && painted)
            {
                p.Restore();
                painted = false;
            }
            if (painted)
                p.Apply(rgba);
        }

        // hex×brightness（RGB）に alpha を載せた色を作る。
        private static Color MakeColor(string hex, float brightness, float alpha, Color fallback)
        {
            var c = ColorUtil.ParseHex(hex, fallback);
            float s = Mathf.Max(0f, brightness);
            return new Color(c.r * s, c.g * s, c.b * s, Mathf.Clamp01(alpha));
        }

        private string GetEffectiveRingHints()
        {
            var hints = (_config.RingShaderHints ?? string.Empty).Trim();
            if (hints.Length == 0) return "Ring;TrackConstruction;BackColumns";
            if (string.Equals(hints, "Ring", StringComparison.OrdinalIgnoreCase))
                return "Ring;TrackConstruction;BackColumns";
            return hints;
        }

        public void Dispose()
        {
            if (_active)
                RestoreOriginal();

            _backdrop.Cleanup();
            _bloomTamer.Restore();
            _floorFlat.Restore();
            _structures.Restore();
            _ring.Restore();
            _domeBuilt = false;
            _floorMode = 0;
            _floorPainted = false;
            _structPainted = false;
            _ringPainted = false;
            GC.SuppressFinalize(this);
        }

        // 背景ドームの色（hex×brightness、不透明）。背景は単色（後ろに何も無いので透明度は持たない）。
        private Color GetDomeColor()
            => MakeColor(_config.BackgroundColor, _config.BackgroundBrightness, 1f, new Color(0.29f, 0.48f, 0.71f));


        // ─── 適用 / 退避 / 復元 ────────────────────────────────────────────

        private void Apply()
        {
            bool bloomOn = _config.Bloom && !_config.RecolorBackground;
            if (bloomOn)
            {
                // Bloom ON：ミラーが映す背景＝メインカメラの backgroundColor を FloorColor にする。
                // clearFlags は変えない（Skybox のまま＝直接の背景は元のまま＝白飛びしない）。
                // 反射カメラは CopyFrom(メインカメラ) で backgroundColor を引き継ぎ SolidColor でクリアするので、床に FloorColor が映る。
                if (_config.PaintFloor)
                    SetMainCameraBackgroundColor(MakeColor(_config.FloorColor, _config.FloorBrightness, 1f, new Color(0.13f, 0.16f, 0.19f)));
            }
            else
            {
                // Bloom OFF：Sky Background ON のときはカメラ背景を空色で塗る（空ドーム用）。
                if (_config.FillBackground && _config.RecolorBackground)
                    FillCameraBackgrounds();
            }
        }

        // メインカメラの backgroundColor だけを設定（clearFlags は据え置き）。反射カメラがこれを引き継いで床に映す。
        private void SetMainCameraBackgroundColor(Color color)
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.CompareTag("MainCamera")) continue;
                if (!_origCameras.ContainsKey(cam))
                    _origCameras[cam] = (cam.clearFlags, cam.backgroundColor);
                cam.backgroundColor = color; // clearFlags は変えない
            }
        }

        // 背景をクリアするカメラ(Skybox/SolidColor)の背景を空色で塗り、黒い虚無を消す。
        // 重ね描き用カメラ(Depth/Nothing)は触らない。
        private void FillCameraBackgrounds()
        {
            var bg = GetDomeColor();

            foreach (var cam in Camera.allCameras)
            {
                if (cam == null) continue;
                if (cam.clearFlags != CameraClearFlags.Skybox && cam.clearFlags != CameraClearFlags.SolidColor)
                    continue;

                if (!_origCameras.ContainsKey(cam))
                    _origCameras[cam] = (cam.clearFlags, cam.backgroundColor);

                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = bg;
            }
        }

        private void RestoreOriginal()
        {
            foreach (var kv in _origCameras)
            {
                if (kv.Key == null) continue;
                kv.Key.clearFlags = kv.Value.flags;
                kv.Key.backgroundColor = kv.Value.bg;
            }
            _origCameras.Clear();
            _active = false;
        }
    }
}
