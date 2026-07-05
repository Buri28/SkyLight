using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyLight.Controllers.Gameplay
{
    // BS の背景クワッド(BloomSkyboxQuad)はブルームシステムから名前で直接参照され、専用パスで
    // 描かれるため、色を塗ると必ず白飛びする。そこで:
    //   (1) その背景クワッドを非表示にし、
    //   (2) ブルームに一切関与しない「自前の空ドーム」を通常レイヤー(床と同じ)に新規生成してカメラに被せる。
    // 自前ドームはブルームシステムに参照されない普通のジオメトリなので、明るい色でも白飛びしない。
    //
    // alpha<1 のときは上記の「不透明置き換え」ではなく、元の背景を隠さずに残したまま
    // 本物の半透明ブレンド(SrcAlpha/OneMinusSrcAlpha, Transparentキュー)を重ねる「透過モード」になる。
    // BloomSkyboxQuad 自体は塗らない別オブジェクトなので、Bloomの専用パスには引っかからない。
    //
    // ドームの半径(scale)は、床(TrackMirror)など奥行きのある実オブジェクトより必ず大きくすること。
    // 小さいと、それらの奥側だけドームの外＝元の(真っ黒な)背景が透けて2色に見える。
    internal class SkyBackdrop
    {
        private GameObject? _dome;
        private Material? _mat;
        private readonly List<Renderer> _hidden = new(); // 非表示にした元の背景クワッド（不透明モードのみ使用）
        private Transform? _camT;
        private bool _logged;
        private bool _transparentMode; // alpha<1 のとき true。元背景を隠さず半透明ブレンドする。

        public void Build(string hideHints, Color color, float scale)
        {
            // 実描画している MainCamera を探してドームの親にする
            var cam = Camera.allCameras.FirstOrDefault(c =>
                c.CompareTag("MainCamera") && c.isActiveAndEnabled && c.targetTexture == null) ?? Camera.main;
            _camT = cam != null ? cam.transform : null;

            _transparentMode = color.a < 0.999f;

            var all = Object.FindObjectsOfType<Renderer>();

            // 不透明モードのみ: 元の背景クワッドを収集（毎フレーム非表示にする）。ドームが背景を置き換える。
            // 透過モードでは元の背景を残し、その上に半透明の色を重ねるので隠さない。
            _hidden.Clear();
            if (!_transparentMode)
            {
                var hints = hideHints.Split(';').Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();
                foreach (var r in all)
                {
                    if (r.sharedMaterials.Any(m => m != null && m.shader != null &&
                            hints.Any(h => m.shader.name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)))
                        _hidden.Add(r);
                }
            }

            EnsureMaterial(color);
            if (_transparentMode)
            {
                // 透過モードは実際に奥の景色とブレンドする必要があるので、立体のドームが要る。
                EnsureDome(0, scale);
            }
            else if (_dome != null)
            {
                // 不透明モードはカメラの backgroundColor（Controller側の FillCameraBackgrounds）だけで
                // 同じ見た目になるため、ドームの描画は不要。前回まで作っていたら片付ける。
                Object.Destroy(_dome);
                _dome = null;
            }
            Apply();

            if (!_logged)
            {
                Plugin.DebugInfo(() => $"[SkyLight][dome] camera={(cam != null ? cam.name : "null")} hiddenQuads={_hidden.Count} scale={scale} transparentMode={_transparentMode} domeMesh={_dome != null}");
                _logged = true;
            }
        }

        public void SetColor(Color color)
        {
            if (_mat != null) _mat.SetColor("_Color", color);
        }

        // 元クワッドが再表示されても毎フレーム消し、ドームのレイヤーも保つ。
        public void Reassert()
        {
            Apply();
        }

        public void Cleanup()
        {
            foreach (var r in _hidden)
                if (r != null) r.enabled = true; // 元に戻す（シーン破棄前提だが念のため）
            _hidden.Clear();

            if (_dome != null) { Object.Destroy(_dome); _dome = null; }
            if (_mat != null) { Object.Destroy(_mat); _mat = null; }
            _camT = null;
        }

        private void Apply()
        {
            foreach (var r in _hidden)
                if (r != null && r.enabled) r.enabled = false;
        }

        private void EnsureMaterial(Color color)
        {
            if (_mat != null) { _mat.SetColor("_Color", color); return; }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Plugin.Log.Warn("[SkyLight][dome] 'Hidden/Internal-Colored' not found; cannot build sky dome.");
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetColor("_Color", color);
            _mat.SetInt("_Cull", (int)CullMode.Front); // 内側から見るので前面カリング
            _mat.SetInt("_ZWrite", 0);                  // 奥のジオメトリを塞がない

            if (_transparentMode)
            {
                // 本物の半透明ブレンド。元の背景(奥)と実際に混ざる。
                _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_ZTest", (int)CompareFunction.LessEqual); // 通常の深度テスト（手前の物に隠れる）
                _mat.renderQueue = (int)RenderQueue.Transparent;        // 不透明物の後に描く
            }
            else
            {
                _mat.SetInt("_SrcBlend", (int)BlendMode.One);
                _mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                _mat.SetInt("_ZTest", (int)CompareFunction.Always); // 常に最背面に描く（他を遮らない）
                _mat.renderQueue = (int)RenderQueue.Background;      // 最初に描く＝最背面
            }
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
                mr.sharedMaterial = _mat;
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
