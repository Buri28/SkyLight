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
    internal class SkyBackdrop
    {
        private GameObject? _dome;
        private Material? _mat;
        private readonly List<Renderer> _hidden = new(); // 非表示にした元の背景クワッド
        private Transform? _camT;
        private bool _logged;

        public void Build(string hideHints, Color color, float scale)
        {
            // 実描画している MainCamera を探してドームの親にする
            var cam = Camera.allCameras.FirstOrDefault(c =>
                c.CompareTag("MainCamera") && c.isActiveAndEnabled && c.targetTexture == null) ?? Camera.main;
            _camT = cam != null ? cam.transform : null;

            var all = Object.FindObjectsOfType<Renderer>();

            // 元の背景クワッドを収集（毎フレーム非表示にする）。ドームが背景を置き換える。
            _hidden.Clear();
            var hints = hideHints.Split(';').Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();
            foreach (var r in all)
            {
                if (r.sharedMaterials.Any(m => m != null && m.shader != null &&
                        hints.Any(h => m.shader.name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)))
                    _hidden.Add(r);
            }

            EnsureMaterial(color);
            EnsureDome(0, scale);
            Apply();

            if (!_logged)
            {
                Plugin.DebugInfo(() => $"[SkyLight][dome] camera={(cam != null ? cam.name : "null")} hiddenQuads={_hidden.Count} scale={scale}");
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
            _mat.SetInt("_SrcBlend", (int)BlendMode.One);
            _mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            _mat.SetInt("_Cull", (int)CullMode.Front);          // 内側から見るので前面カリング
            _mat.SetInt("_ZWrite", 0);                          // 奥のジオメトリを塞がない
            _mat.SetInt("_ZTest", (int)CompareFunction.Always); // 常に最背面に描く（他を遮らない）
            _mat.renderQueue = (int)RenderQueue.Background;      // 最初に描く＝最背面
        }

        private void EnsureDome(int layer, float scale)
        {
            if (_mat == null) return;
            if (_dome == null)
            {
                _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
