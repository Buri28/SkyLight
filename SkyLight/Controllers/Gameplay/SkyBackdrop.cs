using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyLight.Controllers.Gameplay
{
    // 背景の空を自前の「空ドーム」（カメラに被せた巨大な立方体、layer 4）で描く。
    //   不透明モード(alpha=1)：元の背景クワッド(BloomSkyboxQuad)を非表示にし、ドームを最背面に描く。
    //   透過モード(alpha<1)：元の背景を残し、その上にドームを半透明で重ねる。
    // 全画面ブルームは画面のアルファで光る量を決めるので、どちらのモードでも空の画素のアルファは 0 にする
    // （不透明は色のアルファ 0、透過は直後のアルファ消しパス）。これで Bloom ON でも白飛びしない。
    //
    // ドームの半径(scale)は、床(TrackMirror)など奥行きのある実オブジェクトより必ず大きくすること。
    // 小さいと、それらの奥側だけドームの外＝元の(真っ黒な)背景が透けて2色に見える。
    internal class SkyBackdrop
    {
        private GameObject? _dome;
        private Material? _mat;
        // 半透明モード用：空の部分のアルファだけを 0 にする描画（色は変えない）。詳しくは EnsureAlphaClearMaterial。
        private Material? _alphaClearMat;
        private readonly List<Renderer> _hidden = new(); // 非表示にした元の背景クワッド（不透明モードのみ使用）
        private Transform? _camT;
        private bool _logged;
        private bool _transparentMode; // alpha<1 のとき true。元背景を隠さず半透明ブレンドする。
        private bool _reflectInMirror; // 床の反射にも空を映すか
        private Color _color;          // 今の空の色（反射カメラ用に使う）

        // scan: ApplyMode 側で1回だけ取得した共有スキャン（背景クワッド探索用）。
        // reflectInMirror=true：床の反射に空を映す（ドームを反射カメラが描くレイヤー 0 に置く）。
        public void Build(RendererScan scan, string hideHints, Color color, float scale, bool reflectInMirror = false)
        {
            // 実描画している MainCamera を探してドームの親にする
            var cam = Camera.allCameras.FirstOrDefault(c =>
                c.CompareTag("MainCamera") && c.isActiveAndEnabled && c.targetTexture == null) ?? Camera.main;
            _camT = cam != null ? cam.transform : null;

            _transparentMode = color.a < 0.999f;

            // 不透明モードのみ: 元の背景クワッドを集めて非表示にする。ドームが背景を置き換える。
            // 透過モードでは元の背景を残し、その上に半透明の色を重ねるので隠さない。
            _hidden.Clear();
            if (!_transparentMode)
            {
                var hints = (hideHints ?? "").Split(';').Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();
                foreach (var e in scan.Entries)
                {
                    if (e.Renderer != null &&
                        e.ShaderNames.Any(s => hints.Any(h => s.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)))
                        _hidden.Add(e.Renderer);
                }
            }

            EnsureMaterial(color);
            // 不透明モードでもドームを描く。1.40.8 はカメラの backgroundColor（FillCameraBackgrounds）だけで
            // 空が出たが、1.44.1 ではそれだけだと背景が黒いままだったため、カメラ設定に頼らないドームを使う。
            EnsureDome(reflectInMirror ? ReflectedDomeLayer : DomeLayer, scale);
            Apply();

            // 反射床に空を映すとき：反射カメラが描く間だけ、空をアルファ 1 で描き、背景色もアルファ 1 にする。
            // （反射床はアルファ 0 の反射を黒く出す。画面側はアルファ 0 のままなので Bloom で白飛びしない）
            if (reflectInMirror && !_reflectInMirror) Camera.onPreRender += OnCameraPreRender;
            else if (!reflectInMirror && _reflectInMirror) Camera.onPreRender -= OnCameraPreRender;
            _reflectInMirror = reflectInMirror;

            if (!_logged)
            {
                // 診断: 元の背景クワッドの描画順（半透明モードでアルファを消す順番を決めるため）。
                Plugin.DebugInfo(() => "[SkyLight][dome] background quads: " + string.Join(", ", scan.Entries
                    .Where(e => e.Renderer != null && e.ShaderNames.Any(s => s.IndexOf("Skybox", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(e => $"{e.Path}<{string.Join("/", e.SharedMaterials.Where(m => m != null).Select(m => $"{m.shader.name} q={m.renderQueue}"))}> layer={e.Renderer.gameObject.layer} enabled={e.Renderer.enabled}")));
                Plugin.DebugInfo(() => $"[SkyLight][dome] camera={(cam != null ? cam.name : "null")} hiddenQuads={_hidden.Count} scale={scale} transparentMode={_transparentMode} domeMesh={_dome != null}");
                _logged = true;
            }
        }

        // 反射カメラ（MirrorRendererSO が作る "MirrorCam<ID>"）が描く直前だけアルファ 1、それ以外のカメラはアルファ 0。
        private void OnCameraPreRender(Camera cam)
        {
            if (_mat == null || _transparentMode || cam == null) return;
            bool mirror = cam.name.StartsWith("MirrorCam", System.StringComparison.Ordinal);
            _mat.SetColor("_Color", mirror ? new Color(_color.r, _color.g, _color.b, 1f) : DomeOutput(_color));
            if (mirror) cam.backgroundColor = new Color(_color.r, _color.g, _color.b, 1f);
        }

        // 空ドームに置き換えるため、元の背景として非表示にしているか。
        public bool IsHiding(Renderer r) => r != null && _hidden.Contains(r);

        public void SetColor(Color color)
        {
            _color = color;
            if (_mat != null) _mat.SetColor("_Color", DomeOutput(color));
        }

        // ドームのレイヤー。床の反射カメラ(MirrorRendererSO)は cullingMask から layer 4 (Water) を必ず外す
        // (-17 & ...) ので、ここに置くと反射に映らない（床に空の色が映り込まない）。
        // layer 0 だと反射にもドームが映り、床が空と同じ色になっていた（1.44.1 自動テストで確認）。
        private const int DomeLayer = 4;
        // 反射床に空を映すとき用（反射カメラの cullingMask に入るレイヤー）。
        private const int ReflectedDomeLayer = 0;

        // 不透明モードではアルファを 0 で書く。全画面ブルーム(PyramidBloomMainEffectSO)は
        // 画面のアルファで光らせる量を決める(Prefilter4, _alphaWeights=4)ため、アルファ 1 の背景は全面が光って白飛びする。
        // 不透明モードはブレンド One/Zero なので、アルファを 0 にしても色(RGB)はそのまま出る。
        private Color DomeOutput(Color color)
            => _transparentMode ? color : new Color(color.r, color.g, color.b, 0f);

        // 2回目の塗り（3フレーム後）で、元クワッドが再表示されていたら消し直す。
        public void Reassert()
        {
            Apply();
        }

        public void Cleanup()
        {
            foreach (var r in _hidden)
                if (r != null) r.enabled = true; // 元に戻す（シーン破棄前提だが念のため）
            _hidden.Clear();

            if (_reflectInMirror) { Camera.onPreRender -= OnCameraPreRender; _reflectInMirror = false; }
            if (_dome != null) { Object.Destroy(_dome); _dome = null; }
            if (_mat != null) { Object.Destroy(_mat); _mat = null; }
            if (_alphaClearMat != null) { Object.Destroy(_alphaClearMat); _alphaClearMat = null; }
            _camT = null;
        }

        private void Apply()
        {
            foreach (var r in _hidden)
                if (r != null && r.enabled) r.enabled = false;
        }

        private void EnsureMaterial(Color color)
        {
            _color = color;
            if (_mat != null) { _mat.SetColor("_Color", DomeOutput(color)); return; }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Plugin.DebugWarn("[SkyLight][dome] 'Hidden/Internal-Colored' not found; cannot build sky dome.");
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetColor("_Color", DomeOutput(color));
            _mat.SetInt("_Cull", (int)CullMode.Off); // 内側から見る。反射カメラは裏表が反転するので両面描く
            _mat.SetInt("_ZWrite", 0);                  // 奥のジオメトリを塞がない

            if (_transparentMode)
            {
                // 本物の半透明ブレンド。元の背景(奥)と実際に混ざる。
                _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_ZTest", (int)CompareFunction.LessEqual); // 通常の深度テスト（手前の物に隠れる）
                _mat.renderQueue = (int)RenderQueue.Transparent - 2;    // 不透明物の後、半透明物の前に描く（直後にアルファを消す）
                EnsureAlphaClearMaterial(shader);
            }
            else
            {
                _mat.SetInt("_SrcBlend", (int)BlendMode.One);
                _mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                _mat.SetInt("_ZTest", (int)CompareFunction.Always); // 常に最背面に描く（他を遮らない）
                _mat.renderQueue = (int)RenderQueue.Background;      // 最初に描く＝最背面
            }
        }

        // 半透明モードの色ドームは SrcAlpha ブレンドなので、空の画素にアルファ（Alpha=0.2 なら 0.2×0.2=0.04 以上）が残り、
        // Bloom ON で少し光る（1.40.8 Alpha=0.2 の実測：Bloom ON #455286 / OFF #3F4B7C）。
        // そこで色ドームの直後に、同じ形を Blend DstColor Zero・色 (1,1,1,0) で描く：
        //   RGB = 1 × 画面の色 = そのまま、アルファ = 0 × 画面のアルファ = 0。
        // 深度テストありなので、手前の物（ノーツ・構造物など）がある画素は触らない＝空の画素だけ光らなくなる。
        // キューは 色ドーム(Transparent-2) → これ(Transparent-1) → レーザーなど半透明物(Transparent)。
        // 半透明物より前に描くので、それらの光り方は変えない。
        private void EnsureAlphaClearMaterial(Shader shader)
        {
            if (_alphaClearMat != null) return;
            _alphaClearMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _alphaClearMat.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
            _alphaClearMat.SetInt("_SrcBlend", (int)BlendMode.DstColor);
            _alphaClearMat.SetInt("_DstBlend", (int)BlendMode.Zero);
            _alphaClearMat.SetInt("_Cull", (int)CullMode.Front);
            _alphaClearMat.SetInt("_ZWrite", 0);
            _alphaClearMat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            _alphaClearMat.renderQueue = (int)RenderQueue.Transparent - 1;
        }

        private void EnsureDome(int layer, float scale)
        {
            if (_mat == null) return;
            if (_dome == null)
            {
                // 球体だとBloom ONのとき全面が一様な輝度になり白飛びしやすいため、
                // 立方体の部屋（内側から見た6面）に変更。単色パネルのまま面ごとに区切られる。
                _dome = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _dome.name = "SkyLightBackdrop";
                _dome.hideFlags = HideFlags.HideAndDontSave;
                var col = _dome.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                var mr = _dome.GetComponent<MeshRenderer>();
                // 半透明モードは「アルファを消す描画」と「色の描画」の2つ。メッシュが1つでも材質を2つ並べると同じ形を2回描く。
                mr.sharedMaterials = _alphaClearMat != null ? new[] { _alphaClearMat, _mat } : new[] { _mat };
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            _dome.layer = layer;
            if (_camT != null)
            {
                _dome.transform.SetParent(_camT, false);
                _dome.transform.localPosition = Vector3.zero;
                _dome.transform.localRotation = Quaternion.identity;
            }
            _dome.transform.localScale = Vector3.one * scale;
        }
    }
}
