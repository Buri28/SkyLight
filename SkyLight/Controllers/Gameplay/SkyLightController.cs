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
    public class SkyLightController : IInitializable, ILateTickable, IDisposable
    {
        private readonly PluginConfig _config;

        // 半透明の空ドームで背景を着色する（元の背景の上に被せる）。
        private readonly SkyBackdrop _backdrop = new();
        // 全画面ブルームの ON/OFF。
        private readonly BloomTamer _bloomTamer = new();
        // 床：Bloom ON は FloorColor を反射ドームに塗って反射、Bloom OFF はフラット塗り。
        private readonly TargetPainter _floorFlat = new("floor", alwaysReassign: true, writeDepth: true);
        private readonly TargetPainter _sideLanes = new("side-lanes");
        // 構造物・バー・リングは半透明で着色するペインター。
        private readonly TargetPainter _structures = new("struct", excludeFloorLike: true);
        private readonly TargetPainter _bars = new("bars");
        // リングは曲の間ずっと新規に出現し続けるため、他と違って「出揃った」判定をせず毎回スキャンし続ける。
        private readonly TargetPainter _ring = new("ring", neverSettle: true);
        // GlowLineL/R 等、NeonLightレイヤー(13)に属さない光る線。HideNeonと連動して隠す。
        private readonly TargetPainter _glowLines = new("glowline");
        private bool _glowLinesCollected;
        // NeonLightレイヤーのRendererを直接無効化する保険（cullingMaskがBloom専用パスに効かない場合の対策）。
        private readonly TargetPainter _neonRenderers = new("neon-renderers");
        private bool _neonRenderersCollected;

        private bool _active;
        private int _frame;

        private bool _isFirstApply = false;


        // 触ったカメラの元 clearFlags / 背景色（復元用）。シーンをまたいで生き残るものがあるため必ず戻す。
        private readonly Dictionary<Camera, (CameraClearFlags flags, Color bg)> _origCameras = new();
        private int _neonLayer = -1;   // NeonLight レイヤー番号（1回解決）
        private readonly Dictionary<Camera, int> _neonCamMasks = new(); // ネオン非表示で変更したカメラの元 cullingMask

        [Inject]
        public SkyLightController(PluginConfig config)
        {
            _config = config;
        }

        // Bloom を実際に有効扱いするかどうか。RecolorBackground(背景壁)使用中は原則Bloomを強制OFFにするが、
        // AllowBloomWithBackground=true なら維持する（BackgroundAlpha<1の透過モードと併用する想定）。
        private bool IsBloomOn()
            => _config.Bloom && (!_config.RecolorBackground || _config.AllowBloomWithBackground);

        public void Initialize()
        {
            if (!_config.Enabled) return;

            // 診断ダンプは DumpScene のときだけ（毎曲のオブジェクト走査コストを避ける）。
            if (_config.DebugLogging && _config.DumpScene)
            {
                try { SceneDiagnostics.Dump(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Scene dump failed: {ex.Message}"); }
                try { BloomDiagnostics.DumpOnce(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Bloom dump failed: {ex.Message}"); }
            }

            _bloomTamer.Capture();

            // 起動時は軽い処理だけ（カメラ背景＝反射床の色）。重い塗り(ApplyMode)は起動が落ち着く約1秒後に回す。
            ApplyCameraBackground();
            _active = true;
            Plugin.DebugLog("[SkyLight] Applied on gameplay scene (paint deferred to ~1s).");
        }

        // LateTick（Update 後・描画前）。毎フレームは Bloom の強制OFF維持だけ（ライト演出で再有効化されても
        // 白飛びさせないため、対象のenabledを確認するだけの軽い処理）。
        // カメラ背景・重い塗り（ApplyMode：FindObjectsOfType を含む）は、起動直後と約1秒後の2回だけ実行する。
        public void LateTick()
        {
            if (!_active) return;
            _frame++;

            _bloomTamer.Reassert(!IsBloomOn());

            if (!_isFirstApply && _frame % 60 == 0)
            {
                // 順序が重要: ApplyMode 内の FillCameraBackgrounds が背景ドームの色でカメラ背景を
                // 上書きするため、床の色(ApplyCameraBackground)は必ずその後に適用して確定させる。
                ApplyMode(); // 後から出現する対象（リング等）も拾って塗る
                ApplyCameraBackground();
                _isFirstApply = true;
            }
        }

        // ─── 着色の適用（全対象 半透明＝B方式） ───────────────────────────
        // Bloom は独立トグル。各対象（背景/床/構造物/リング）を Color×Brightness と Alpha(不透明度) で半透明着色。
        // 空ドームの半径（内部固定）。床(TrackMirror)は奥行き約250、BackColumnsは約157まで伸びており、
        // 旧来の60だとそれより手前でドームが終わってしまい、床の奥側だけ元の(真っ黒な)背景が透けて見えていた。
        // ドームは無地の単色キューブなので大きくしても見た目は変わらない。カメラの far クリップ内で、
        // 実在するオブジェクトの最大奥行きより十分大きい値にする。
        private const float DomeScale = 400f;
        private const int TargetRefreshInterval = 60; // 約1秒ごとに後発の対象を増分収集（出揃ったら自動で停止）
        private bool _domeBuilt;
        private int _floorMode; // 0=Bloom ONでカメラ背景に映す / 1=Bloom OFFでフラット塗り
        private bool _floorPainted;
        private bool _sideLanesPainted;
        private bool _structPainted;
        private bool _barsPainted;
        private bool _ringPainted;

        private void ApplyMode()
        {
            // 診断: Initialize()時点だと実環境がまだ読み込み切っていないことがあるため、
            // 本塗りと同じこのタイミング(約1秒後)でも再度ダンプする（DumpSceneのときだけ）。
            if (_config.DebugLogging && _config.DumpScene)
            {
                try { SceneDiagnostics.Dump(); }
                catch (Exception ex) { Plugin.Log.Warn($"[SkyLight] Scene dump (ApplyMode) failed: {ex.Message}"); }
            }

            // Bloom の強制OFFは LateTick が毎フレーム行うのでここでは触らない。
            bool bloomOn = IsBloomOn();
            bool useBackdrop = _config.RecolorBackground;

            // 背景ドーム。Sky Background ON 時に元背景を置き換える。
            if (useBackdrop && !_domeBuilt)
            {
                _backdrop.Build(_config.BackgroundShaderHints, GetDomeColor(), DomeScale);
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
                // 黒い虚無の保険塗り（ドームが覆うので1回でよい）。
                FillCameraBackgrounds();
            }

            // 床の色は FloorColor。塗り方が Bloom で違う：
            //  ① Bloom ON  → メインカメラの backgroundColor を FloorColor にする（ミラーが反射＝床に映る・直接背景は据え置きで白飛びしない）。Apply() で処理。
            //  ② Bloom OFF → 床に直接フラット塗り。
            int wantFloorMode = (_config.PaintFloor && _config.ShowFloor && (!bloomOn || _config.FloorDirectPaint)) ? 1 : 0;
            if (wantFloorMode != _floorMode)
            {
                _floorFlat.Restore();
                _floorPainted = false;
                _floorMode = wantFloorMode;
            }
            UpdateFloorVisibility();
            UpdateSideLaneVisibility();

            // 構造物（名前/シェーダーヒントで対象、除外あり）。Colorize=false なら元の色のまま透明度だけ。
            UpdateTarget(_structures, _config.PaintStructures || !_config.ShowStructures, ref _structPainted,
                _config.StructureShaderHints, GetEffectiveStructureExcludes(), disableMirror: false,
                _config.StructureColorize,
                MakeColor(_config.StructureColor, _config.StructureBrightness, _config.StructureAlpha, new Color(0.25f, 0.38f, 0.63f)));
            if (_structPainted)
                _structures.SetVisible(_config.ShowStructures);

            // バー（Spectrogram）は表示/非表示だけを管理する。
            UpdateBarVisibility();

            // リング。対象は RingShaderHints で指定（環境により "Ring" 名が無いため）。
            UpdateTarget(_ring, _config.PaintRing || !_config.ShowRing, ref _ringPainted,
                GetEffectiveRingHints(), _config.RingExcludeHints, disableMirror: false, _config.RingColorize,
                MakeColor(_config.RingColor, _config.RingBrightness, _config.RingAlpha, new Color(0.227f, 0.627f, 1f)));
            if (_ringPainted)
                _ring.SetVisible(_config.ShowRing);

            // ネオン/レーザー(NeonLightレイヤー)の表示/非表示。横の動くネオンバー・水色ビームを消す。
            ApplyNeonVisibility();
        }

        // NeonLight レイヤー(13)を全カメラの cullingMask から外す/戻す。BloomPrePass のネオンは色塗り不可なので、
        // 描画対象から外して隠す。元のマスクは保存して Dispose で復元。
        private void ApplyNeonVisibility()
        {
            if (_neonLayer < 0)
                _neonLayer = LayerMask.NameToLayer("NeonLight");
            if (_neonLayer < 0) return;
            int bit = 1 << _neonLayer;

            if (_config.HideNeon)
            {
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    if (!_neonCamMasks.ContainsKey(cam))
                        _neonCamMasks[cam] = cam.cullingMask;
                    cam.cullingMask &= ~bit;
                }
            }
            else if (_neonCamMasks.Count > 0)
            {
                foreach (var kv in _neonCamMasks)
                    if (kv.Key != null) kv.Key.cullingMask = kv.Value;
                _neonCamMasks.Clear();
            }

            // GlowLineL/R 等、NeonLightレイヤーに属さない光る線はレイヤー除外が効かないので、
            // 名前一致で個別に収集して表示/非表示を切り替える。
            if (!_glowLinesCollected)
            {
                _glowLines.Collect("GlowLine", "", disableMirror: false, colorize: false);
                _glowLinesCollected = true;
            }
            _glowLines.SetVisible(!_config.HideNeon);

            // 保険: cullingMask除外がBloom専用パスに効かないケースに備え、NeonLightレイヤーの
            // Renderer自体も直接無効化する（GlowLine同様、こちらは色を塗らず表示/非表示のみ）。
            if (!_neonRenderersCollected)
            {
                _neonRenderers.CollectByLayer(_neonLayer);
                _neonRenderersCollected = true;
            }
            _neonRenderers.SetVisible(!_config.HideNeon);
        }

        private void UpdateTarget(TargetPainter p, bool want, ref bool painted, string hints, string exclude, bool disableMirror, bool colorize, Color rgba)
        {
            if (want && !painted)
            {
                p.Collect(hints, exclude, disableMirror, colorize);
                painted = true;
            }
            else if (!want && painted)
            {
                p.Restore();
                painted = false;
            }
            if (painted)
            {
                // 後から出現する対象（リング等は再生開始から少し遅れて現れる/動く）を増分で拾って塗る。
                if (_frame % TargetRefreshInterval == 0)
                    p.Refresh(hints, exclude, disableMirror);
                p.Apply(rgba);
            }
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
            return hints.Length == 0 ? "Ring" : hints;
        }

        private string GetEffectiveStructureExcludes()
        {
            return MergeHints(
                _config.StructureExcludeHints,
                "Mirror",
                "Note",
                "Saber",
                "Arrow",
                "Bomb",
                "Skybox",
                "BloomSkyboxQuad",
                "Ring",
                "TrackConstruction",
                "FloorConstruction",
                "Spectrogram",
                "Runway",
                "TrackLane",
                "Lane",
                "Road");
        }

        private string GetEffectiveBarHints()
        {
            var hints = (_config.BarShaderHints ?? string.Empty).Trim();
            return hints.Length == 0 ? "Spectrogram" : hints;
        }

        private void UpdateBarVisibility()
        {
            if (!_barsPainted)
            {
                _bars.Collect(GetEffectiveBarHints(), _config.BarExcludeHints, disableMirror: false, colorize: true);
                _barsPainted = true;
            }
            else if (_frame % TargetRefreshInterval == 0)
            {
                _bars.Refresh(GetEffectiveBarHints(), _config.BarExcludeHints, disableMirror: false);
            }

            _bars.SetVisible(_config.ShowBars);
        }

        private void UpdateSideLaneVisibility()
        {
            if (!_sideLanesPainted)
            {
                _sideLanes.Collect(GetEffectiveSideLaneHints(), GetEffectiveSideLaneExcludes(), disableMirror: false, colorize: true);
                _sideLanesPainted = true;
            }

            if (_frame % TargetRefreshInterval == 0)
                _sideLanes.Refresh(GetEffectiveSideLaneHints(), GetEffectiveSideLaneExcludes(), disableMirror: false);

            _sideLanes.SetVisible(_config.ShowSideLanes);
        }

        private string GetEffectiveSideLaneHints()
        {
            return MergeHints(
                _config.SideLaneHints,
                "TrackConstruction",
                "TrackLane",
                "Road",
                "Runway");
        }

        private string GetEffectiveSideLaneExcludes()
        {
            return MergeHints(
                string.Empty,
                "Mirror",
                "Spectrogram",
                "Ring",
                "Note",
                "Saber",
                "Arrow",
                "Bomb",
                "BloomSkyboxQuad",
                "Skybox");
        }

        private void UpdateFloorVisibility()
        {
            bool wantTracking = !_config.ShowFloor || _floorMode == 1;
            if (wantTracking && !_floorPainted)
            {
                _floorFlat.Collect(_config.FloorPaintShaderHint, "", disableMirror: _floorMode == 1, colorize: true);
                _floorPainted = true;
            }
            else if (!wantTracking && _floorPainted)
            {
                _floorFlat.Restore();
                _floorPainted = false;
            }

            if (!_floorPainted) return;

            _floorFlat.SetVisible(_config.ShowFloor);
            if (_config.ShowFloor && _floorMode == 1)
            {
                _floorFlat.Apply(MakeColor(_config.FloorColor, _config.FloorBrightness, _config.FloorAlpha, new Color(0.13f, 0.16f, 0.19f)));
            }
        }

        private static string MergeHints(string current, params string[] required)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hint in (current ?? string.Empty).Split(';'))
            {
                var trimmed = hint.Trim();
                if (trimmed.Length > 0)
                    all.Add(trimmed);
            }

            foreach (var hint in required)
            {
                var trimmed = (hint ?? string.Empty).Trim();
                if (trimmed.Length > 0)
                    all.Add(trimmed);
            }

            return string.Join(";", all);
        }

        public void Dispose()
        {
            if (_active)
                RestoreOriginal();

            _backdrop.Cleanup();
            _bloomTamer.Restore();
            _floorFlat.Restore();
            _sideLanes.Restore();
            _structures.Restore();
            _bars.Restore();
            _ring.Restore();
            _glowLines.Restore();
            _neonRenderers.Restore();
            _domeBuilt = false;
            _floorMode = 0;
            _floorPainted = false;
            _sideLanesPainted = false;
            _structPainted = false;
            _barsPainted = false;
            _ringPainted = false;
            _glowLinesCollected = false;
            _neonRenderersCollected = false;
            GC.SuppressFinalize(this);
        }

        // 背景ドームの色（hex×brightness）。Alpha=1で不透明置き換え、Alpha<1で本物の半透明ブレンド。
        private Color GetDomeColor()
            => MakeColor(_config.BackgroundColor, _config.BackgroundBrightness, _config.BackgroundAlpha, new Color(0.29f, 0.48f, 0.71f));


        // ─── 適用 / 退避 / 復元 ────────────────────────────────────────────

        // 毎フレーム呼ばれる軽い処理。Bloom ON のときだけ、ミラーが映すメインカメラ背景色を FloorColor に保つ。
        // （clearFlags は変えない＝直接背景はそのまま＝白飛びしない。反射カメラが CopyFrom で引き継いで床に映す）
        // メインカメラ背景はキャッシュ済みで「色が変わったときだけ代入」なので実質ゼロコスト。
        // Bloom OFF（空ドーム）側のカメラ塗りつぶしは空ドームが背景を覆うので毎フレーム不要 → ApplyMode で1回行う。
        private void ApplyCameraBackground()
        {
            bool bloomOn = IsBloomOn();
            if (bloomOn && _config.PaintFloor && _config.ShowFloor && !_config.FloorDirectPaint)
                SetGameplayCameraBackgroundColor(MakeColor(_config.FloorColor, _config.FloorBrightness, 1f, new Color(0.13f, 0.16f, 0.19f)));
        }

        // Camera.allCameras は呼ぶたびに配列を新規確保するため、毎フレーム呼ぶ SetGameplayCameraBackgroundColor
        // 用に使い回すバッファ。GetAllCameras(バッファ) は非アロケーション。
        private Camera[] _cameraBuffer = new Camera[8];

        // 背景をクリアするゲームプレイ用カメラの backgroundColor を同期する（clearFlags は据え置き）。
        // 録画/デスクトップ側のカメラもここへ含めることで、VR と同じ床反射色になるようにする。
        private void SetGameplayCameraBackgroundColor(Color color)
        {
            int count = Camera.allCamerasCount;
            if (_cameraBuffer.Length < count)
                _cameraBuffer = new Camera[count];
            int n = Camera.GetAllCameras(_cameraBuffer);

            for (int i = 0; i < n; i++)
            {
                var cam = _cameraBuffer[i];
                if (cam == null) continue;
                if (cam.clearFlags != CameraClearFlags.Skybox && cam.clearFlags != CameraClearFlags.SolidColor)
                    continue;

                if (!_origCameras.ContainsKey(cam))
                    _origCameras[cam] = (cam.clearFlags, cam.backgroundColor);

                if (cam.backgroundColor != color)
                    cam.backgroundColor = color;
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

            // ネオン非表示で変更したカメラの cullingMask を元に戻す。
            foreach (var kv in _neonCamMasks)
                if (kv.Key != null) kv.Key.cullingMask = kv.Value;
            _neonCamMasks.Clear();

            _active = false;
        }
    }
}
