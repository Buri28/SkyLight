using SkyLight.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace SkyLight.Controllers.Gameplay
{
    /// <summary>
    /// ゲームプレイシーンで、背景の空（自前のドーム）・床・リングなどの色を変え、環境の構造物を非表示にして、
    /// 背景を明るくしノーツとの明暗差を縮めるコントローラー。
    ///
    /// 塗り・非表示は読み込み時（Initialize）と3フレーム後の2回だけ行い、曲の間は Bloom の ON/OFF 維持以外は何もしない。
    /// </summary>
    public class SkyLightController : IInitializable, ILateTickable, IDisposable
    {
        private readonly PluginConfig _config;

        // json の色が読めないときに使う色。PluginConfig の既定値を正とする（別の値を二重に持たない）
        private static readonly PluginConfig Defaults = new PluginConfig();
        private static Color DefaultColor(string hex) => ColorUtil.ParseHex(hex, Color.white);

        // 空ドームで背景を着色する（不透明なら元の背景を隠して置き換え、半透明なら元の背景の上に被せる）。
        private readonly SkyBackdrop _backdrop = new();
        // 全画面ブルームの ON/OFF。
        private readonly BloomTamer _bloomTamer = new();
        // 床：ゲームのミラー画質がオフ（または反射に空を映せない）ときは、FloorColor で直接塗る（Mirror は止める）。
        private readonly TargetPainter _floorFlat = new("floor", alwaysReassign: true, writeDepth: true);
        private readonly TargetPainter _sideLanes = new("side-lanes");
        // 表示/非表示のみの対象（Side Lanes と同方式）：ライト・ロゴ・その他の構造物。
        private readonly TargetPainter _lights = new("lights");
        private readonly TargetPainter _logo = new("logo");
        private readonly TargetPainter _otherStructures = new("other-struct");
        private readonly TargetPainter _hudBackground = new("hud-bg");
        // 構造物・バー・リングのペインター（着色は Alpha=1 なら不透明、Alpha<1 なら半透明）。
        private readonly TargetPainter _structures = new("struct", excludeFloorLike: true);
        private readonly TargetPainter _bars = new("bars");
        // リングは最初のフレームで生成されるので、3フレーム後の2回目で拾う。
        private readonly TargetPainter _ring = new("ring");
        // GlowLineL/R 等、NeonLightレイヤー(13)に属さない光る線。HideNeonと連動して隠す。
        private readonly TargetPainter _glowLines = new("glowline");
        private bool _glowLinesCollected;
        // NeonLightレイヤーのRendererを直接無効化する保険（cullingMaskがBloom専用パスに効かない場合の対策）。
        private readonly TargetPainter _neonRenderers = new("neon-renderers");
        private bool _neonRenderersCollected;
        // ユーザー指定（HideObjectHints）のオブジェクトを非表示にする。
        // 対象名は、設定画面の [Hide] タブ（環境ごと）か、ログの "Hide candidates"（DebugLogging と DumpScene）から拾って設定に追加する。
        private readonly TargetPainter _hider = new("hide");
        private string _hiderHints = "";
        // 環境ごとの非表示リスト（UserData/SkyLight/Environments/HideObjects.json）。曲の読み込み時に1回だけ読む。
        private string _environment = "";
        private bool _useMirrorReflection; // 床をゲームの反射床のままにするか（Initialize で決める）
        // CustomBackgrounds（別 MOD）の背景の球 "_SkyBGObject"。NeonLight レイヤー(13)に置かれるので、
        // Neon を非表示にすると一緒に消える。表示するときは曲の間だけ別のレイヤーへ移し、終わったら戻す。
        private Renderer? _customBg;
        private int _customBgOrigLayer;
        private bool _customBgOrigEnabled;
        private const string CustomBackgroundObjectName = "_SkyBGObject";
        private const int CustomBackgroundLayer = 4; // 床の反射に映らないレイヤー（空ドームと同じ）
        private string _envHideHints = "";

        private bool _active;
        private int _frame;

        private bool _latePassDone; // 3フレーム後の2回目を済ませたか


        // 触ったカメラの元 clearFlags / 背景色（復元用）。シーンをまたいで生き残るものがあるため必ず戻す。
        private readonly Dictionary<Camera, (CameraClearFlags flags, Color bg)> _origCameras = new();
        private int _neonLayer = -1;   // NeonLight レイヤー番号（1回解決）
        private readonly Dictionary<Camera, int> _neonCamMasks = new(); // ネオン非表示で変更したカメラの元 cullingMask

        [Inject]
        public SkyLightController(PluginConfig config)
        {
            _config = config;
        }

        // Bloom は設定の Bloom トグルだけで決める。空・床はアルファ 0 で描くので、Sky Background 中でも白飛びしない。
        private bool IsBloomOn() => _config.Bloom;

        // プレイ画面（GameCore シーン）がロードされているかどうか。
        // メニュー等でこの MOD が動作しないようにするガード条件。
        private static bool IsGameplaySceneLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name == "GameCore") return true;
            }
            return false;
        }

        // GameCore がアンロードされたら（＝プレイ終了でメニューへ戻る時点で）即座に全復元する。
        // Zenject の Dispose より早いタイミングで確実に戻し、メニュー側への影響を残さない。
        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name != "GameCore") return;
            RestoreAll();
        }

        public void Initialize()
        {
            if (!_config.Enabled) return;

            // プレイ画面以外（メニュー等）では何もしない。
            if (!IsGameplaySceneLoaded())
            {
                Plugin.DebugLog("[SkyLight] GameCore not loaded; skip applying (non-gameplay scene).");
                return;
            }

            SceneManager.sceneUnloaded += OnSceneUnloaded;

            // 例外が出ても、プレイ画面の初期化（Zenject の Initialize）を止めない。出るのはデバッグモードの Warn だけ。
            try { _bloomTamer.Capture(); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] Bloom capture failed: {ex.Message}"); }

            // ミラー画質がオンなら床は反射床のまま（空とノーツを映す）。1プレイ中は変わらないので1回だけ調べる。
            // 反射に空を映せるのは、空ドームがあり（RecolorBackground）、不透明（BackgroundAlpha=1）のときだけ。
            // 半透明の空だと反射カメラのアルファが 0 のままで床が黒く映るので、床は FloorColor の直接塗りに戻す。
            try
            {
                _useMirrorReflection = _config.MirrorReflection && _config.ShowFloor && _config.RecolorBackground
                                       && _config.BackgroundAlpha >= 0.999f && MirrorProbe.IsReflectionOn();
            }
            catch (Exception ex)
            {
                _useMirrorReflection = false;
                Plugin.DebugWarn($"[SkyLight] Mirror probe failed: {ex.Message}");
            }
            Plugin.DebugInfo(() => $"[SkyLight][floor] mirror reflection={_useMirrorReflection}");

            try
            {
                _environment = SceneDiagnostics.GetEnvironmentName();
                _envHideHints = EnvironmentHideList.GetHideHints(_environment);
                EnvironmentHideList.LastPlayed = _environment;
            }
            catch (Exception ex)
            {
                Plugin.DebugWarn($"[SkyLight] Environment lookup failed: {ex.Message}");
            }
            Plugin.DebugInfo(() => $"[SkyLight] environment='{_environment}' env hide='{_envHideHints}'");

            // 読み込み時に一度塗る：最初からあるもの（背景・床・構造物など）は1フレーム目から非表示/着色される。
            // 後から出てくるもの（リング等）は3フレーム後の2回目(IsLatePass)で増分収集する。
            SafeApplyMode();
            _active = true;
            Plugin.DebugLog("[SkyLight] Applied on gameplay scene (late objects at frame 3).");
        }

        // LateTick（Update 後・描画前）。毎フレームは Bloom の強制OFF維持だけ（ライト演出で再有効化されても
        // 白飛びさせないため、対象のenabledを確認するだけの軽い処理）。
        // カメラ背景・重い塗り（ApplyMode：FindObjectsOfType を含む）は、読み込み時と3フレーム後の2回だけ実行する。
        public void LateTick()
        {
            if (!_active) return;
            _frame++;

            _bloomTamer.Reassert(!IsBloomOn());

            // 遅れて生成される MainEffectController を、1秒後（60フレーム）に1回だけ探し直す
            if (_frame == BloomRefreshFrame)
                Step("bloom refresh", _bloomTamer.Refresh);

            if (!_latePassDone && _frame == LatePassFrame)
            {
                _latePassDone = true;
                SafeApplyMode(); // 後から出現する対象（リング等）も拾って塗る
            }
        }

        // 塗り処理で例外が出ても、プレイ画面の初期化や他の LateTick を止めないようにする。
        private void SafeApplyMode()
        {
            try { ApplyMode(); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] ApplyMode failed: {ex}"); }
        }

        // ─── 着色の適用 ───────────────────────────
        // Bloom は独立トグル。各対象（背景/床/構造物/リング）を Color×Brightness と Alpha(不透明度) で着色する
        // （Alpha=1 は不透明でアルファ 0 で描く。Alpha<1 は半透明）。
        // 空ドームの半径（内部固定）。床(TrackMirror)は奥行き約250、BackColumnsは約157まで伸びており、
        // 旧来の60だとそれより手前でドームが終わってしまい、床の奥側だけ元の(真っ黒な)背景が透けて見えていた。
        // ドームは無地の単色キューブなので大きくしても見た目は変わらない。カメラの far クリップ内で、
        // 実在するオブジェクトの最大奥行きより十分大きい値にする。
        private const float DomeScale = 1000f;
        private const int BloomRefreshFrame = 60; // 開始60フレーム後に1回だけ BloomTamer を増分収集
        private const int LatePassFrame = 3; // 開始3フレーム後に一度だけ後発の対象（リング等。最初のフレームで生成される）を増分収集（以降は何もしない）
        // ApplyMode の2回目（3フレーム後）かどうか。1回目は読み込み時(Initialize, _frame=0)。
        // Refresh は1回呼ぶと以後は探索しないので、必ず2回目だけで呼ぶ。
        private bool IsLatePass => _frame > 0;
        private bool _domeBuilt;
        private int _floorMode; // 0=床を塗らない / 1=フラット塗り
        private bool _floorPainted;
        private bool _sideLanesPainted;
        private bool _lightsPainted;
        private bool _logoPainted;
        private bool _otherStructuresPainted;
        private bool _hudBackgroundPainted;
        private bool _structPainted;
        private bool _barsPainted;
        private bool _ringPainted;

        // 診断ダンプ。SkyLight.json で DumpScene と DebugLogging の両方が true のときだけ（毎曲のオブジェクト走査コストを避ける）。
        // 読み込み時と、実環境が出揃った3フレーム後の2回（3フレーム後だけ Spectrogram のシェーダー値も）。
        private void DumpDiagnostics()
        {
            if (!(_config.DumpScene && _config.DebugLogging)) return;
            try { SceneDiagnostics.DumpHideCandidates(_config); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] Hide candidates dump failed: {ex.Message}"); }
            try { SceneDiagnostics.Dump(); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] Scene dump failed: {ex.Message}"); }
            if (!IsLatePass) return;
            try { SceneDiagnostics.DumpShaderProperties("Spectrogram"); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] Shader property dump failed: {ex.Message}"); }
        }

        // ApplyMode の1つの段階を実行する。例外が出ても、あとの段階（Neon・Hide など）を止めない。
        private static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Plugin.DebugWarn($"[SkyLight] {what} failed: {ex}"); }
        }

        private void ApplyMode()
        {
            // 診断: Initialize()時点だと実環境がまだ読み込み切っていないことがあるため、
            // 本塗りと同じこのタイミング(3フレーム後)でも再度ダンプする（DumpScene と DebugLogging のときだけ）。
            DumpDiagnostics();

            // Bloom の強制OFFは LateTick が毎フレーム行うのでここでは触らない。
            bool bloomOn = IsBloomOn();
            bool useBackdrop = _config.RecolorBackground;

            // シーン走査は1回だけ行い、全ペインターで共有する（以前は各 Collect/Refresh が
            // それぞれ FindObjectsOfType で全走査しており、同一フレームに10回以上走っていた）。
            var scan = RendererScan.Capture();

            // 各段階は Step で個別に包む。1つが例外を出しても、あとの段階（Neon・Hide など）は動く。

            // 背景ドーム。Sky Background ON 時に元背景を置き換える。
            Step("sky dome", () =>
            {
                if (useBackdrop && !_domeBuilt)
                {
                    _backdrop.Build(scan, _config.BackgroundShaderHints, GetDomeColor(), DomeScale, _useMirrorReflection);
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
            });

            // 床は、ゲームのミラー画質がオフ・反射に空を映せないとき（_useMirrorReflection=false）に FloorColor で直接塗る（Mirror は止める）。
            // 不透明ならアルファ 0 で描くので Bloom ON でも白飛びしない（TargetPainter.EnsureMaterial）。
            Step("floor", () =>
            {
                int wantFloorMode = (_config.PaintFloor && _config.ShowFloor && !_useMirrorReflection) ? 1 : 0;
                if (wantFloorMode != _floorMode)
                {
                    _floorFlat.Restore();
                    _floorPainted = false;
                    _floorMode = wantFloorMode;
                }
                UpdateFloorVisibility(scan);
                UpdateSideLaneVisibility(scan);
            });

            // 表示/非表示のみの対象（Side Lanes と同方式）。
            Step("lights", () => UpdateVisibilityOnly(scan, _lights, ref _lightsPainted, _config.LightHints, _config.ShowLights));
            Step("logo", () => UpdateVisibilityOnly(scan, _logo, ref _logoPainted, _config.LogoHints, _config.ShowLogo));
            Step("other structures", () => UpdateVisibilityOnly(scan, _otherStructures, ref _otherStructuresPainted, _config.OtherStructureHints, _config.ShowOtherStructures));
            Step("hud background", () => UpdateVisibilityOnly(scan, _hudBackground, ref _hudBackgroundPainted, _config.HudBackgroundHints, _config.ShowHudBackground));

            // 構造物（名前/シェーダーヒントで対象、除外あり）。Colorize=false なら元の色のまま透明度だけ。
            // 塗らない（Paint=false）で隠すだけのときは、材質を差し替えない。
            Step("structures", () =>
            {
                UpdateTarget(scan, _structures, _config.PaintStructures || !_config.ShowStructures, _config.PaintStructures, ref _structPainted,
                    _config.StructureShaderHints, GetEffectiveStructureExcludes(), disableMirror: false,
                    _config.StructureColorize,
                    MakeColor(_config.StructureColor, _config.StructureBrightness, _config.StructureAlpha, DefaultColor(Defaults.StructureColor)),
                    _config.StructureExcludeOverrideHints);
                if (_structPainted)
                    _structures.SetVisible(_config.ShowStructures);
            });

            // バー（Spectrogram）は表示/非表示だけを管理する。
            Step("bars", () => UpdateBarVisibility(scan));

            // リング。対象は RingShaderHints で指定（環境により "Ring" 名が無いため）。
            Step("ring", () =>
            {
                UpdateTarget(scan, _ring, _config.PaintRing || !_config.ShowRing, _config.PaintRing, ref _ringPainted,
                    GetEffectiveRingHints(), _config.RingExcludeHints, disableMirror: false, _config.RingColorize,
                    MakeColor(_config.RingColor, _config.RingBrightness, _config.RingAlpha, DefaultColor(Defaults.RingColor)));
                if (_ringPainted)
                    _ring.SetVisible(_config.ShowRing);
            });

            // CustomBackgrounds の背景。ネオンの処理より先に、レイヤーを移しておく（Neon 非表示に巻き込まれないように）。
            Step("custom background", () => ApplyCustomBackground(scan));

            // ネオン/レーザー(NeonLightレイヤー)の表示/非表示。横の動くネオンバー・水色ビームを消す。
            Step("neon", () => ApplyNeonVisibility(scan));

            // ユーザー指定オブジェクトの非表示（Hide Objects）。
            Step("hide objects", () => ApplyHideObjects(scan));
        }

        // 環境ごとの候補に載せるか。プレイヤー・HUD・ノーツ・セイバーの効果・SkyLight 自身など
        // 「環境の物ではない」と分かっている物を除く（カスタムプラットフォームの物は残るよう、除外方式にする）。
        private static readonly string[] NonEnvironmentRoots =
            { "Wrapper", "GameCore", "HsvFlyingEffect", "BurnPS", "SaberBurnMarkLine", "SaberTrailRenderer",
              "ObstacleSaberSparkleEffect", "_SkyBGObject", "SkyLightBackdrop" };
        // まとまりの名前（例 Environment/Car4/Body/Body → "Car4"、BigTrackLaneRing(Clone)/... → そのまま）。
        // 車1台・リング1組のように、部品をまとめて非表示にしたいときに選べるよう候補に載せる。
        private static string? GroupName(string path)
        {
            var parts = path.Split('/');
            if (parts.Length <= 2) return null;
            return parts[0] == "Environment" ? parts[1] : parts[0];
        }

        private static bool IsEnvironmentCandidate(string name, string path)
        {
            int slash = path.IndexOf('/');
            var root = slash >= 0 ? path.Substring(0, slash) : path;
            if (NonEnvironmentRoots.Any(r => root.StartsWith(r, StringComparison.OrdinalIgnoreCase))) return false;
            foreach (var x in GetCommonVisibilityExcludes().Split(';'))
                if (root.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            return true;
        }

        // HideObjectHints（全環境共通）と、環境ごとの Hide（HideObjects.json）に一致するレンダラーを非表示にする。
        // 収集はこの ApplyMode のタイミング（読み込み時＋3フレーム後）だけで、毎フレーム処理はしない。
        private void ApplyHideObjects(RendererScan scan)
        {
            var hints = MergeHints(_config.HideObjectHints, _envHideHints.Split(';'));

            if (hints != _hiderHints)
            {
                // ヒントが変わったら前回の非表示を戻してから収集し直す。
                _hider.Restore();
                _hiderHints = hints;
                if (hints.Length > 0)
                    _hider.Collect(scan, hints, _config.HideObjectExcludeHints, disableMirror: false, colorize: false);
            }
            else if (IsLatePass && hints.Length > 0)
            {
                _hider.Refresh(scan, hints, _config.HideObjectExcludeHints, disableMirror: false);
            }
            if (hints.Length > 0)
                _hider.SetVisible(false);

            // 2回目（リング等も出揃い、全部の表示/非表示を済ませた時点）で1回だけ、
            // 候補名と「SkyLight のどの設定で消えているか」を書く（設定画面の Object 一覧に印を出すため）。
            if (IsLatePass && _environment.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0)
                RecordEnvironmentState(scan);
        }

        // 候補ごとに、そのレンダラーを消している設定の名前を集める。
        // 全部消えていれば "Structures" のように、一部だけなら "Part:Structures" と書く。
        // 何も消えていなければ書かない。環境ごとの Hide で消した物は、設定画面が今の Hide から [Hidden] を付けるので、ここでは数えない。
        private void RecordEnvironmentState(RendererScan scan)
        {
            var globalHide = SplitHintList(_config.HideObjectHints);
            var globalExclude = SplitHintList(_config.HideObjectExcludeHints);

            // 名前 → (レンダラー数, 消えている数, 消した設定)
            var stats = new Dictionary<string, (int total, int hidden, SortedSet<string> reasons)>(StringComparer.Ordinal);
            foreach (var e in scan.Entries)
            {
                if (e.Renderer == null || !IsEnvironmentCandidate(e.Name, e.Path)) continue;
                var reason = HiddenReason(e, globalHide, globalExclude);
                foreach (var n in new[] { EnvironmentHideList.CandidateName(e.Name), GroupName(e.Path) })
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!stats.TryGetValue(n!, out var s)) s = (0, 0, new SortedSet<string>(StringComparer.Ordinal));
                    s.total++;
                    if (reason != null) { s.hidden++; s.reasons.Add(reason); }
                    stats[n!] = s;
                }
            }

            var hiddenBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in stats)
            {
                var (total, hidden, reasons) = kv.Value;
                if (hidden == 0) continue;
                var r = string.Join("/", reasons);
                hiddenBy[kv.Key] = hidden == total ? r : "Part:" + r;
            }

            EnvironmentHideList.RecordPlayState(_environment, stats.Keys, hiddenBy);
        }

        // そのレンダラーを SkyLight のどの設定で消しているか（設定画面での呼び名）。消していなければ null。
        private string? HiddenReason(RendererScan.Entry e, string[] globalHide, string[] globalExclude)
        {
            var r = e.Renderer;
            if (_domeBuilt && _backdrop.IsHiding(r)) return "Sky";
            if (_floorFlat.IsHiding(r)) return "Floor";
            if (_structures.IsHiding(r)) return "Structures";
            if (_ring.IsHiding(r)) return "Ring";
            if (_neonRenderers.IsHiding(r) || _glowLines.IsHiding(r)) return "Neon";
            if (_bars.IsHiding(r)) return "Bars";
            if (_sideLanes.IsHiding(r)) return "SideLanes";
            if (_lights.IsHiding(r)) return "Lights";
            if (_logo.IsHiding(r)) return "Logo";
            if (_otherStructures.IsHiding(r)) return "Others";
            if (_hudBackground.IsHiding(r)) return "HudBG";
            // 全環境共通の Hide Objects。環境ごとの Hide だけに当たった物は数えない（上のコメント参照）。
            if (_hider.IsHiding(r) && HintHit(e, globalHide) && !HintHit(e, globalExclude)) return "HideObjects";
            return null;
        }

        private static bool HintHit(RendererScan.Entry e, string[] hints)
            => hints.Any(h => e.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                           || e.Path.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                           || e.ShaderNames.Any(s => s.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0));

        private static string[] SplitHintList(string s) => HintUtil.Split(s);

        private void ApplyCustomBackground(RendererScan scan)
        {
            if (_customBg == null)
            {
                foreach (var e in scan.Entries)
                    if (e.Renderer != null && e.Name == CustomBackgroundObjectName) { _customBg = e.Renderer; break; }
                if (_customBg == null) return; // CustomBackgrounds が入っていない／無効
                _customBgOrigLayer = _customBg.gameObject.layer;
                _customBgOrigEnabled = _customBg.enabled;
            }

            if (_config.ShowCustomBackground)
            {
                _customBg.gameObject.layer = CustomBackgroundLayer;
                _customBg.enabled = _customBgOrigEnabled;
            }
            else
            {
                _customBg.enabled = false;
            }
        }

        private void RestoreCustomBackground()
        {
            if (_customBg != null)
            {
                _customBg.gameObject.layer = _customBgOrigLayer;
                _customBg.enabled = _customBgOrigEnabled;
            }
            _customBg = null;
        }

        // NeonLight レイヤー(13)を全カメラの cullingMask から外す/戻す。BloomPrePass のネオンは色塗り不可なので、
        // 描画対象から外して隠す。元のマスクは保存して Dispose で復元。
        private void ApplyNeonVisibility(RendererScan scan)
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
                _glowLines.Collect(scan, "GlowLine", "", disableMirror: false, colorize: false);
                _glowLinesCollected = true;
            }
            else if (IsLatePass)
            {
                _glowLines.Refresh(scan, "GlowLine", "", disableMirror: false);
            }
            _glowLines.SetVisible(!_config.HideNeon);

            // 保険: cullingMask除外がBloom専用パスに効かないケースに備え、NeonLightレイヤーの
            // Renderer自体も直接無効化する（GlowLine同様、こちらは色を塗らず表示/非表示のみ）。
            if (!_neonRenderersCollected)
            {
                _neonRenderers.CollectByLayer(scan, _neonLayer);
                _neonRenderersCollected = true;
            }
            else if (IsLatePass)
            {
                _neonRenderers.RefreshByLayer(scan, _neonLayer);
            }
            _neonRenderers.SetVisible(!_config.HideNeon);
        }

        // want：収集して扱うか（塗る、または隠す）。paint：材質を差し替えて塗るか（false なら隠すだけで、材質は触らない）。
        private void UpdateTarget(RendererScan scan, TargetPainter p, bool want, bool paint, ref bool painted, string hints, string exclude, bool disableMirror, bool colorize, Color rgba, string excludeOverride = "")
        {
            if (want && !painted)
            {
                p.Collect(scan, hints, exclude, disableMirror, colorize, excludeOverride);
                painted = true;
            }
            else if (!want && painted)
            {
                p.Restore();
                painted = false;
            }
            if (painted)
            {
                // リング等は最初のフレームで生成されるため、3フレーム後に一度だけ増分収集する（以降 Refresh は何もしない）。
                if (IsLatePass)
                    p.Refresh(scan, hints, exclude, disableMirror, excludeOverride);
                if (paint) p.Apply(rgba);
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

        private void UpdateBarVisibility(RendererScan scan)
        {
            if (!_barsPainted)
            {
                _bars.Collect(scan, GetEffectiveBarHints(), _config.BarExcludeHints, disableMirror: false, colorize: true);
                _barsPainted = true;
            }
            else if (IsLatePass)
            {
                _bars.Refresh(scan, GetEffectiveBarHints(), _config.BarExcludeHints, disableMirror: false);
            }

            _bars.SetVisible(_config.ShowBars);
        }

        // 表示/非表示のみの対象（ライト・ロゴ・その他の構造物）。Side Lanes と同じく
        // 初回に収集し、3フレーム後に一度だけ増分収集して、表示状態を設定に合わせる。
        // ノーツ/セイバー等を巻き込まないよう共通の除外を常に適用する。
        private void UpdateVisibilityOnly(RendererScan scan, TargetPainter p, ref bool collected, string hints, bool show)
        {
            var effective = (hints ?? string.Empty).Trim();
            if (effective.Length == 0) return;
            var excludes = GetCommonVisibilityExcludes();

            if (!collected)
            {
                p.Collect(scan, effective, excludes, disableMirror: false, colorize: false);
                collected = true;
            }
            else if (IsLatePass)
            {
                p.Refresh(scan, effective, excludes, disableMirror: false);
            }

            p.SetVisible(show);
        }

        // 表示/非表示のみ対象の共通除外（ノーツ・セイバー・床・空などの巻き添え防止）。
        private static string GetCommonVisibilityExcludes()
            // TextMeshPro;HsvFlyingEffect：ゲームの飛ぶ文字（スコア表示など）。"Mesh" のようなヒントに、シェーダー名の TextMeshPro が一致して消えるのを防ぐ
            => "Mirror;Note;Saber;Arrow;Bomb;Skybox;BloomSkyboxQuad;Debris;Obstacle;PreviewQuad;Feet;TextMeshPro;HsvFlyingEffect";

        private void UpdateSideLaneVisibility(RendererScan scan)
        {
            if (!_sideLanesPainted)
            {
                _sideLanes.Collect(scan, GetEffectiveSideLaneHints(), GetEffectiveSideLaneExcludes(), disableMirror: false, colorize: true);
                _sideLanesPainted = true;
            }

            if (IsLatePass)
                _sideLanes.Refresh(scan, GetEffectiveSideLaneHints(), GetEffectiveSideLaneExcludes(), disableMirror: false);

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

        private void UpdateFloorVisibility(RendererScan scan)
        {
            bool wantTracking = !_config.ShowFloor || _floorMode == 1;
            if (wantTracking && !_floorPainted)
            {
                _floorFlat.Collect(scan, _config.FloorPaintShaderHint, "", disableMirror: _floorMode == 1, colorize: true);
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
                _floorFlat.Apply(MakeColor(_config.FloorColor, _config.FloorBrightness, _config.FloorAlpha, DefaultColor(Defaults.FloorColor)));
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
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            RestoreAll();
        }

        // 適用した変更をすべて元に戻す（GameCore アンロード時と Dispose の両方から呼ぶ。多重呼び出し安全）。
        private void RestoreAll()
        {
            if (_active)
                RestoreOriginal();

            _backdrop.Cleanup();
            _bloomTamer.Restore();
            RestoreCustomBackground();
            // ApplyMode で集めた順の逆順に戻す。同じレンダラーを複数のペインターが触ったとき、
            // 最初に触ったペインターが記録した「本当の元の状態」が最後に書き戻されるようにするため。
            _hider.Restore();
            _neonRenderers.Restore();
            _glowLines.Restore();
            _ring.Restore();
            _bars.Restore();
            _structures.Restore();
            _hudBackground.Restore();
            _otherStructures.Restore();
            _logo.Restore();
            _lights.Restore();
            _sideLanes.Restore();
            _floorFlat.Restore();
            _hiderHints = "";
            _domeBuilt = false;
            _floorMode = 0;
            _floorPainted = false;
            _sideLanesPainted = false;
            _lightsPainted = false;
            _logoPainted = false;
            _otherStructuresPainted = false;
            _hudBackgroundPainted = false;
            _structPainted = false;
            _barsPainted = false;
            _ringPainted = false;
            _glowLinesCollected = false;
            _neonRenderersCollected = false;
            _latePassDone = false;
        }

        // 背景ドームの色（hex×brightness）。Alpha=1で不透明置き換え、Alpha<1で本物の半透明ブレンド。
        private Color GetDomeColor()
            => MakeColor(_config.BackgroundColor, _config.BackgroundBrightness, _config.BackgroundAlpha, DefaultColor(Defaults.BackgroundColor));


        // ─── 適用 / 退避 / 復元 ────────────────────────────────────────────

        // 背景をクリアするカメラ(Skybox/SolidColor)の背景を空色で塗り、黒い虚無を消す。
        // 重ね描き用カメラ(Depth/Nothing)は触らない。
        private void FillCameraBackgrounds()
        {
            var bg = GetDomeColor();
            bg.a = 0f; // アルファ 0＝全画面ブルームで光らせない（SkyBackdrop.DomeOutput と同じ理由）

            foreach (var cam in Camera.allCameras)
            {
                if (cam == null) continue;
                bool fillable = cam.clearFlags == CameraClearFlags.Skybox || cam.clearFlags == CameraClearFlags.SolidColor;
                // 診断: 背景を塗れたカメラ／塗れなかったカメラ（1.44.1 で背景が黒い原因の確認用）。
                Plugin.DebugInfo(() => $"[SkyLight][camfill] '{cam.name}' tag={cam.tag} clearFlags={cam.clearFlags} depth={cam.depth} " +
                    $"targetTex={(cam.targetTexture != null)} stereo={cam.stereoTargetEye} filled={fillable}");
                if (!fillable)
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
