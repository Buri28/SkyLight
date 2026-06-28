using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyLight.Controllers.Gameplay
{
    // FloorColor を「ミラーが反射する背景」として用意し、反射で床に FloorColor を出す。
    //   (1) FloorColor のドームを未使用レイヤーに作る
    //   (2) そのレイヤーを MirrorRendererSO._reflectLayers に追加（ミラーが反射する）
    //   (3) そのレイヤーを全カメラの cullingMask から除外（直接視には出ない＝反射カメラだけが描く）
    // ドームは反射の中で最前面(Always/Overlay)に塗りつぶすので、環境反射に負けず FloorColor が確実に出る。
    // 反射は全画面ブルームを迂回するので Bloom ON でも床は光らない。
    internal class ReflectionDome
    {
        private GameObject? _dome;
        private Material? _mat;
        private int _layer = -1;
        private readonly List<(Camera cam, int origMask)> _camMasks = new();
        private readonly List<(object so, FieldInfo field, int origValue)> _mirrors = new();
        private Camera? _parentCam;
        private int _reassertCount;
        private bool _active;
        private bool _logged;

        public void Enable(Color color, float scale)
        {
            if (_active) return;

            _layer = FindFreeLayer();
            if (_layer < 0)
            {
                Plugin.Log.Warn("[SkyLight][refdome] no free layer available.");
                return;
            }

            _parentCam = ResolveMainCamera();
            EnsureMaterial(color);
            if (_mat == null) return;
            BuildDome(scale);

            ExcludeFromAllCameras();
            PatchMirrors();

            _active = true;
            if (!_logged)
            {
                Plugin.Log.Info($"[SkyLight][refdome] enabled layer={_layer}({LayerMask.LayerToName(_layer)}) mirrorsPatched={_mirrors.Count} cameras={_camMasks.Count} scale={scale}");
                _logged = true;
            }
        }

        public void SetColor(Color color)
        {
            if (_mat != null) _mat.SetColor("_Color", color);
        }

        public void Reassert()
        {
            if (!_active) return;
            foreach (var (cam, _) in _camMasks)
                if (cam != null) cam.cullingMask &= ~(1 << _layer);
            if (++_reassertCount % 120 == 0)
            {
                ExcludeFromAllCameras();
                PatchMirrors();
            }
        }

        public void Disable()
        {
            foreach (var (so, field, origValue) in _mirrors)
                if (so != null) field.SetValue(so, (LayerMask)origValue);
            _mirrors.Clear();

            foreach (var (cam, origMask) in _camMasks)
                if (cam != null) cam.cullingMask = origMask;
            _camMasks.Clear();
            _parentCam = null;

            if (_dome != null) { Object.Destroy(_dome); _dome = null; }
            if (_mat != null) { Object.Destroy(_mat); _mat = null; }
            _layer = -1;
            _reassertCount = 0;
            _active = false;
            _logged = false;
        }

        private void ExcludeFromAllCameras()
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null) continue;
                if (!_camMasks.Any(c => c.cam == cam))
                    _camMasks.Add((cam, cam.cullingMask));
                cam.cullingMask &= ~(1 << _layer);
            }
        }

        private void PatchMirrors()
        {
            foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (so == null || so.GetType().Name != "MirrorRendererSO") continue;
                var field = so.GetType().GetField("_reflectLayers", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;

                int cur = ((LayerMask)field.GetValue(so)).value;
                if (!_mirrors.Any(m => ReferenceEquals(m.so, so)))
                    _mirrors.Add((so, field, cur));
                field.SetValue(so, (LayerMask)(cur | (1 << _layer)));
            }
        }

        private void BuildDome(float scale)
        {
            _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dome.name = "SkyLightReflectionDome";
            _dome.hideFlags = HideFlags.HideAndDontSave;
            var col = _dome.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var mr = _dome.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _dome.layer = _layer;
            if (_parentCam != null)
            {
                _dome.transform.SetParent(_parentCam.transform, false);
                _dome.transform.localPosition = Vector3.zero;
                _dome.transform.localRotation = Quaternion.identity;
            }
            _dome.transform.localScale = Vector3.one * scale;
        }

        private void EnsureMaterial(Color color)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Plugin.Log.Warn("[SkyLight][refdome] 'Hidden/Internal-Colored' not found.");
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetColor("_Color", color);
            _mat.SetInt("_SrcBlend", (int)BlendMode.One);
            _mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            _mat.SetInt("_Cull", (int)CullMode.Front);
            _mat.SetInt("_ZWrite", 0);
            // 反射の中で最前面に塗りつぶす（環境反射に負けず FloorColor を出す）。レイヤーは全カメラ除外済み。
            _mat.SetInt("_ZTest", (int)CompareFunction.Always);
            _mat.renderQueue = (int)RenderQueue.Overlay;
        }

        private static int FindFreeLayer()
        {
            for (int i = 8; i <= 31; i++)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    return i;
            return -1;
        }

        private static Camera? ResolveMainCamera()
        {
            foreach (var c in Camera.allCameras)
                if (c != null && c.CompareTag("MainCamera") && c.isActiveAndEnabled && c.targetTexture == null)
                    return c;
            return Camera.main;
        }
    }
}
