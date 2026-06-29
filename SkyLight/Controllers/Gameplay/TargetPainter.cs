using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyLight.Controllers.Gameplay
{
    // 対象レンダラー（名前/シェーダー名のヒントで一致）を「半透明の単色」へ差し替えるペインター。
    // alpha でオブジェクト自体の不透明度を決め、後ろにあるものが透けて見える（透明度=B方式）。
    // 床・構造物・リングなど用途ごとにインスタンスを使う。
    internal class TargetPainter
    {
        private readonly string _tag;
        private readonly List<(Renderer renderer, Material[] original)> _painted = new();
        // 反射床(Mirror)は描画直前に自前マテリアルへ戻すので、対象なら Mirror 系コンポーネントを無効化する。
        private readonly List<(Behaviour b, bool origEnabled)> _disabledMirrors = new();
        // 透明度だけモード（colorize=false）：元マテリアルを複製し、半透明化して色は元のまま残す。
        private readonly List<(Renderer renderer, Material[] clones)> _cloned = new();
        private Material? _mat;
        private bool _colorize = true;
        private bool _assigned;        // 単色マテリアルを各レンダラーへ代入済みか
        private bool _clonesAssigned;  // 複製マテリアルを代入済みか
        private Color _lastColor = new Color(-1f, -1f, -1f, -1f); // 直近に適用した色（変化なしなら何もしない）
        private readonly bool _alwaysReassign; // 毎フレーム再代入する（ミラー等が戻してくる対象向け。対象数が少ない床用）
        private readonly HashSet<int> _ids = new(); // 収集済みレンダラーの InstanceID（増分収集の重複防止）
        private int _emptyRefreshes;   // 連続で「新規ゼロ」だった Refresh 回数
        private bool _refreshSettled;  // 対象が出揃ったら探索(FindObjectsOfType)を止める
        private bool _logged;

        public TargetPainter(string tag, bool alwaysReassign = false) { _tag = tag; _alwaysReassign = alwaysReassign; }

        // hints / excludeHints: ';' 区切り。名前 または シェーダー名に部分一致で対象/除外。
        // colorize=true: 単色（rgba）で塗る。false: 元の色を残し透明度(rgba.a)だけ下げる。
        public void Collect(string hints, string excludeHints, bool disableMirror, bool colorize = true)
        {
            _colorize = colorize;
            _painted.Clear();
            _ids.Clear();
            _assigned = false;
            _clonesAssigned = false;
            _lastColor = new Color(-1f, -1f, -1f, -1f);
            _emptyRefreshes = 0;
            _refreshSettled = false;

            var hintArr = Split(hints);
            var exArr = Split(excludeHints);
            if (hintArr.Length == 0) { LogOnce(); return; }

            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !Matches(r, hintArr, exArr)) continue;
                _ids.Add(r.GetInstanceID());
                _painted.Add((r, r.sharedMaterials));
                DisableMirrorsOn(r, disableMirror);
            }

            LogOnce();
        }

        // 後から出現した対象を増分で追加する（リング等は再生開始から少し遅れて現れる/動くため）。
        // 既存の塗りは保持し、新規が見つかったら次の Apply で塗り直す。
        public void Refresh(string hints, string excludeHints, bool disableMirror)
        {
            if (_refreshSettled) return; // 対象が出揃ったら探索しない（FindObjectsOfType を回さない）
            var hintArr = Split(hints);
            if (hintArr.Length == 0) return;
            var exArr = Split(excludeHints);

            int before = _painted.Count;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !Matches(r, hintArr, exArr)) continue;
                if (!_ids.Add(r.GetInstanceID())) continue; // 既知はスキップ
                _painted.Add((r, r.sharedMaterials));
                DisableMirrorsOn(r, disableMirror);
            }

            // 新規が増えたら塗り直し（代入し直して新規にも適用する）。
            if (_painted.Count > before)
            {
                _assigned = false;
                _clonesAssigned = false;
                _emptyRefreshes = 0;
                var added = _painted.Skip(before).Take(8).Select(p => p.renderer != null ? p.renderer.gameObject.name : "?");
                Plugin.Log.Info($"[SkyLight][{_tag}] refresh added {_painted.Count - before} (total {_painted.Count}) names=[{string.Join(", ", added)}]");
            }
            else if (++_emptyRefreshes >= 6)
            {
                // 連続6回(=約6秒)新規ゼロなら出揃ったとみなして探索を停止（以降 FindObjectsOfType を回さない）。
                _refreshSettled = true;
            }
        }

        private bool Matches(Renderer r, string[] hintArr, string[] exArr)
        {
            string name = r.gameObject.name;
            string path = GetPath(r.transform);
            var shaders = r.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).ToArray();

            bool match = hintArr.Any(h => name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                                          || path.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                                          || shaders.Any(s => s.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0));
            if (!match) return false;
            bool excluded = exArr.Any(h => name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                                           || path.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0
                                           || shaders.Any(s => s.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0));
            return !excluded;
        }

        private void DisableMirrorsOn(Renderer r, bool disableMirror)
        {
            if (!disableMirror) return;
            foreach (var comp in r.GetComponents<Behaviour>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_disabledMirrors.Any(d => d.b == comp)) continue;
                _disabledMirrors.Add((comp, comp.enabled));
                comp.enabled = false;
            }
        }

        public void Apply(Color rgba)
        {
            // 何も変わっていなければ完全に何もしない（毎フレーム呼ばれても実質ゼロコスト）。
            // alwaysReassign（床=ミラーが戻す）と、色が変わったとき、新規収集直後だけ実処理する。
            bool done = _colorize ? _assigned : _clonesAssigned;
            if (done && !_alwaysReassign && rgba == _lastColor)
                return;
            _lastColor = rgba;

            foreach (var (b, _) in _disabledMirrors)
                if (b != null && b.enabled) b.enabled = false;

            if (!_colorize)
            {
                ApplyKeepOriginal(Mathf.Clamp01(rgba.a));
                return;
            }

            EnsureMaterial(rgba);   // 色を更新（_mat は共有なので安い）
            if (_mat == null) return;

            // マテリアル配列の代入は初回だけ（毎フレーム代入すると重い／GCが走る）。
            // ただし alwaysReassign（床＝ミラーが戻してくる）の場合は毎フレーム代入してミラーに勝つ。
            if (_assigned && !_alwaysReassign) return;
            foreach (var (renderer, original) in _painted)
            {
                if (renderer == null) continue;
                var mats = new Material[original.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _mat;
                renderer.sharedMaterials = mats;
            }
            _assigned = true;
        }

        // 元の色・テクスチャを残したまま、マテリアルを複製して半透明化する（透明度だけモード）。
        // シェーダーがアルファ合成に対応していれば透ける（対応しないシェーダーでは効果が出ないことがある）。
        private void ApplyKeepOriginal(float alpha)
        {
            // 初回だけ複製を作る。
            if (_cloned.Count == 0)
            {
                foreach (var (renderer, original) in _painted)
                {
                    if (renderer == null) continue;
                    var clones = new Material[original.Length];
                    for (int i = 0; i < original.Length; i++)
                    {
                        var src = original[i];
                        if (src == null) { clones[i] = null!; continue; }
                        var clone = new Material(src) { hideFlags = HideFlags.HideAndDontSave };
                        clone.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        clone.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        clone.SetInt("_ZWrite", 0);
                        clone.renderQueue = (int)RenderQueue.Transparent;
                        clones[i] = clone;
                    }
                    _cloned.Add((renderer, clones));
                }
            }

            // 透明度の更新（安い）。
            foreach (var (renderer, clones) in _cloned)
            {
                if (renderer == null) continue;
                foreach (var c in clones)
                {
                    if (c == null || !c.HasProperty("_Color")) continue;
                    var col = c.GetColor("_Color");
                    col.a = alpha;
                    c.SetColor("_Color", col);
                }
            }

            // 複製マテリアルの代入は初回だけ。
            if (_clonesAssigned) return;
            foreach (var (renderer, clones) in _cloned)
                if (renderer != null) renderer.sharedMaterials = clones;
            _clonesAssigned = true;
        }

        public void SetColor(Color rgba)
        {
            if (_mat != null) _mat.SetColor("_Color", rgba);
        }

        public void Restore()
        {
            foreach (var (renderer, original) in _painted)
                if (renderer != null) renderer.sharedMaterials = original;
            _painted.Clear();

            foreach (var (renderer, clones) in _cloned)
                foreach (var c in clones)
                    if (c != null) UnityEngine.Object.Destroy(c);
            _cloned.Clear();

            foreach (var (b, origEnabled) in _disabledMirrors)
                if (b != null) b.enabled = origEnabled;
            _disabledMirrors.Clear();

            if (_mat != null) { UnityEngine.Object.Destroy(_mat); _mat = null; }
            _ids.Clear();
            _assigned = false;
            _clonesAssigned = false;
            _lastColor = new Color(-1f, -1f, -1f, -1f);
            _emptyRefreshes = 0;
            _refreshSettled = false;
            _logged = false;
        }

        private void EnsureMaterial(Color rgba)
        {
            if (_mat != null) { _mat.SetColor("_Color", rgba); return; }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Plugin.Log.Warn($"[SkyLight][{_tag}] 'Hidden/Internal-Colored' not found; cannot paint.");
                return;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetColor("_Color", rgba);
            // 半透明合成（後ろが透ける）。
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = (int)RenderQueue.Transparent;
        }

        private void LogOnce()
        {
            if (_logged) return;
            var names = string.Join(", ", _painted.Take(12).Select(p =>
            {
                if (p.renderer == null) return "?";
                var sh = p.renderer.sharedMaterial != null && p.renderer.sharedMaterial.shader != null ? p.renderer.sharedMaterial.shader.name : "?";
                return $"{GetPath(p.renderer.transform)}<{sh}>";
            }));
            Plugin.Log.Info($"[SkyLight][{_tag}] painted renderers found: {_painted.Count}, mirrors disabled: {_disabledMirrors.Count} names=[{names}]");
            _logged = true;
        }

        private static string[] Split(string s)
            => (s ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            return string.Join("/", stack);
        }
    }
}
