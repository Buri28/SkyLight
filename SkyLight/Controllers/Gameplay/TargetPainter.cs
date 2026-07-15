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
        private readonly HashSet<int> _disabledMirrorIds = new(); // _disabledMirrors の重複防止（線形検索の回避）
        private readonly List<(Renderer renderer, bool origEnabled)> _hiddenRenderers = new();
        private readonly HashSet<int> _hiddenIds = new(); // _hiddenRenderers の重複防止（線形検索の回避）
        private readonly HashSet<Transform> _bloomScannedRoots = new(); // BloomPrePass探索済みのサブツリー根（重複走査防止）
        // 透明度だけモード（colorize=false）：元マテリアルを複製し、半透明化して色は元のまま残す。
        private readonly List<(Renderer renderer, Material[] clones)> _cloned = new();
        private Material? _mat;
        private bool _colorize = true;
        private bool _assigned;        // 単色マテリアルを各レンダラーへ代入済みか
        private bool _clonesAssigned;  // 複製マテリアルを代入済みか
        private Color _lastColor = new Color(-1f, -1f, -1f, -1f); // 直近に適用した色（変化なしなら何もしない）
        private readonly bool _alwaysReassign; // 毎フレーム再代入する（ミラー等が戻してくる対象向け。対象数が少ない床用）
        private readonly bool _excludeFloorLike; // 構造物判定から床状の広い平面を除外する
        private readonly bool _writeDepth; // 床のように後ろへ色が抜けないよう深度を書き込む
        private readonly HashSet<int> _ids = new(); // 収集済みレンダラーの InstanceID（増分収集の重複防止）
        private bool _refreshSettled;  // Refresh は初回の1回だけ。以降は探索(FindObjectsOfType)を一切行わない
        private bool _logged;
        private bool _hiding;          // SetVisible(false)で非表示状態にしているか（RefreshByLayerが新規分も揃えるため）

        public TargetPainter(string tag, bool alwaysReassign = false, bool excludeFloorLike = false, bool writeDepth = false)
        {
            _tag = tag;
            _alwaysReassign = alwaysReassign;
            _excludeFloorLike = excludeFloorLike;
            _writeDepth = writeDepth;
        }

        // hints / excludeHints: ';' 区切り。名前 または シェーダー名に部分一致で対象/除外。
        // excludeOverrideHints: 除外の例外。除外に一致しても、こちらに一致すれば対象に戻す。
        // colorize=true: 単色（rgba）で塗る。false: 元の色を残し透明度(rgba.a)だけ下げる。
        // scan: ApplyMode 側で1回だけ取得した共有スキャン（各ペインターが個別に全走査しないため）。
        public void Collect(RendererScan scan, string hints, string excludeHints, bool disableMirror, bool colorize = true, string excludeOverrideHints = "")
        {
            _colorize = colorize;
            _painted.Clear();
            _ids.Clear();
            _assigned = false;
            _clonesAssigned = false;
            _lastColor = new Color(-1f, -1f, -1f, -1f);
            _refreshSettled = false;

            var hintArr = Split(hints);
            var exArr = Split(excludeHints);
            var exOverrideArr = Split(excludeOverrideHints);
            if (hintArr.Length == 0) { LogOnce(); return; }

            foreach (var e in scan.Entries)
            {
                if (e.Renderer == null || !Matches(e, hintArr, exArr, exOverrideArr)) continue;
                _ids.Add(e.Renderer.GetInstanceID());
                _painted.Add((e.Renderer, e.SharedMaterials));
                DisableMirrorsOn(e.Renderer, disableMirror);
                DisableBloomPrePassOn(e.Renderer);
            }

            LogOnce();
        }

        // 指定レイヤーのレンダラーを丸ごと収集する（色は塗らず、表示/非表示の切り替え専用）。
        // cullingMaskによるレイヤー除外がBloom等の専用パスに効かない場合の保険として、
        // Rendererコンポーネント自体を直接無効化するために使う。
        public void CollectByLayer(RendererScan scan, int layer)
        {
            _painted.Clear();
            _ids.Clear();

            foreach (var e in scan.Entries)
            {
                if (e.Renderer == null || e.Renderer.gameObject.layer != layer) continue;
                _ids.Add(e.Renderer.GetInstanceID());
                _painted.Add((e.Renderer, e.SharedMaterials));
                DisableBloomPrePassOn(e.Renderer);
            }

            LogOnce();
        }

        // ライトショー演出等で後から対象レイヤーへ切り替わるレンダラーを増分で拾う。
        // 既に非表示中(SetVisible(false)済み)なら、新規追加分もその場で即座に非表示化する。
        public void RefreshByLayer(RendererScan scan, int layer)
        {
            if (_refreshSettled) return;
            foreach (var e in scan.Entries)
            {
                var r = e.Renderer;
                if (r == null || r.gameObject.layer != layer) continue;
                if (!_ids.Add(r.GetInstanceID())) continue;
                _painted.Add((r, e.SharedMaterials));
                DisableBloomPrePassOn(r);
                if (_hiding && r.enabled)
                {
                    if (_hiddenIds.Add(r.GetInstanceID()))
                        _hiddenRenderers.Add((r, true));
                    r.enabled = false;
                }
            }

            // 探索は初回の1回だけ。以降は完全に何もしない。
            _refreshSettled = true;
        }

        // BakedBloom(Parametric3SliceSprite)の親には TubeBloomPrePassLight(WithId) が付いており、
        // Rendererのenabledとは無関係に専用の描画パスでネオン管の光を描いている。
        // これがRenderer無効化だけでは消えない「残光」の正体なので、コンポーネント自体を無効化する。
        // 紐づき先は親1階層とは限らないため、自分自身・全祖先・子孫までまとめて探す。
        // Destroy は同じGameObject上の別スクリプトが破棄後の参照で毎フレーム例外を吐いた前例があるため使わない。
        private void DisableBloomPrePassOn(Renderer r)
        {
            // 同じ親を持つレンダラーが多数マッチする環境（ネオン管群など）では全く同じ範囲を
            // 何度も走査することになるため、走査済みのサブツリー根を覚えてスキップする。
            var subtree = r.transform.parent != null ? r.transform.parent : r.transform;
            if (!_bloomScannedRoots.Add(subtree)) return;

            DisableBloomPrePassIn(r.GetComponentsInParent<Behaviour>(true)); // 自分自身＋全祖先
            // 親配下ごと探す（自分自身＋子孫に加え、兄弟とその子孫に付くライトも拾う）。
            DisableBloomPrePassIn(subtree.GetComponentsInChildren<Behaviour>(true));
        }

        private void DisableBloomPrePassIn(Behaviour[] comps)
        {
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                if (comp.GetType().Name.IndexOf("BloomPrePassLight", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!_disabledMirrorIds.Add(comp.GetInstanceID())) continue;
                _disabledMirrors.Add((comp, comp.enabled));
                comp.enabled = false;
            }
        }

        // 後から出現した対象を増分で追加する（リング等は再生開始から少し遅れて現れるため）。
        // 実行は Collect 後の1回だけ。以降は完全に何もしない。
        public void Refresh(RendererScan scan, string hints, string excludeHints, bool disableMirror, string excludeOverrideHints = "")
        {
            if (_refreshSettled) return; // 2回目以降は探索しない
            var hintArr = Split(hints);
            if (hintArr.Length == 0) return;
            var exArr = Split(excludeHints);
            var exOverrideArr = Split(excludeOverrideHints);

            int before = _painted.Count;
            foreach (var e in scan.Entries)
            {
                var r = e.Renderer;
                if (r == null || !Matches(e, hintArr, exArr, exOverrideArr)) continue;
                if (!_ids.Add(r.GetInstanceID())) continue; // 既知はスキップ
                _painted.Add((r, e.SharedMaterials));
                DisableMirrorsOn(r, disableMirror);
                DisableBloomPrePassOn(r);
                // 非表示中(SetVisible(false)済み)なら、新規に見つかった分もその場で即座に非表示化する。
                if (_hiding && r.enabled)
                {
                    if (_hiddenIds.Add(r.GetInstanceID()))
                        _hiddenRenderers.Add((r, true));
                    r.enabled = false;
                }
            }

            // 新規が増えたら塗り直し（代入し直して新規にも適用する）。
            if (_painted.Count > before)
            {
                _assigned = false;
                _clonesAssigned = false;
                Plugin.DebugInfo(() =>
                {
                    var added = _painted.Skip(before).Take(8).Select(p => p.renderer != null ? p.renderer.gameObject.name : "?");
                    return $"[SkyLight][{_tag}] refresh added {_painted.Count - before} (total {_painted.Count}) names=[{string.Join(", ", added)}]";
                });
            }

            // 探索は Collect 後の1回だけ。以降は完全に何もしない。
            _refreshSettled = true;
        }

        // メニュー系オブジェクトの除外は RendererScan.Capture が収集時点で済ませている。
        // 名前/パス/シェーダー名はスキャン時にキャッシュ済みのものを使い、ここでは一切アロケーションしない。
        private bool Matches(in RendererScan.Entry e, string[] hintArr, string[] exArr, string[] exOverrideArr)
        {
            if (!AnyHit(e, hintArr)) return false;
            bool excluded = AnyHit(e, exArr);
            // 除外の例外：除外に一致しても、override に一致すれば対象へ戻す（例: Mirror除外中の DiamondMirror）。
            if (excluded && exOverrideArr.Length > 0)
                excluded = !AnyHit(e, exOverrideArr);
            if (excluded) return false;
            if (_excludeFloorLike && IsFloorLike(e.Renderer)) return false;
            return true;
        }

        private static bool AnyHit(in RendererScan.Entry e, string[] hints)
        {
            foreach (var h in hints)
            {
                if (e.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (e.Path.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                foreach (var s in e.ShaderNames)
                    if (s.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void DisableMirrorsOn(Renderer r, bool disableMirror)
        {
            if (!disableMirror) return;
            foreach (var comp in r.GetComponents<Behaviour>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!_disabledMirrorIds.Add(comp.GetInstanceID())) continue;
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
                ApplyKeepOriginal(rgba, tint: false);
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

        // レーザー/ネオンは譜面のライトショーに連動してゲーム側が毎フレーム enabled を操作することがあるため、
        // 1回の無効化だけでは点灯イベントの度に復活してしまう。非表示にしている間、毎フレーム軽量に
        // enabled=false へ戻す（既に false なら何もしないので、点灯していない限りほぼゼロコスト）。
        public void ReassertHidden()
        {
            if (_hiddenRenderers.Count == 0) return;
            foreach (var (renderer, _) in _hiddenRenderers)
                if (renderer != null && renderer.enabled) renderer.enabled = false;
        }

        // 診断用: 収集済みのうち、まだ enabled=true のままのレンダラー数（0でなければ非表示化漏れ）。
        public int DebugCountEnabled() => _painted.Count(p => p.renderer != null && p.renderer.enabled);

        // visible=false は Renderer を無効化する。
        // 破棄(Destroy)も試したが、Spectrogram等 同じGameObject上のスクリプトが破棄後のRendererを
        // 参照し続けて毎フレーム例外を吐く事例があったため、安全な無効化のみに戻した。
        public void SetVisible(bool visible)
        {
            _hiding = !visible;
            if (visible)
            {
                foreach (var (renderer, origEnabled) in _hiddenRenderers)
                    if (renderer != null) renderer.enabled = origEnabled;
                _hiddenRenderers.Clear();
                _hiddenIds.Clear();
                return;
            }

            foreach (var (renderer, _) in _painted)
            {
                if (renderer == null) continue;
                if (!_hiddenIds.Add(renderer.GetInstanceID())) continue;
                _hiddenRenderers.Add((renderer, renderer.enabled));
                renderer.enabled = false;
            }
        }

        // 元の色・テクスチャを残したまま、マテリアルを複製して半透明化する（透明度だけモード）。
        // シェーダーがアルファ合成に対応していれば透ける（対応しないシェーダーでは効果が出ないことがある）。
        private void ApplyKeepOriginal(Color rgba, bool tint)
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
                    if (tint)
                    {
                        col = rgba;
                    }
                    else
                    {
                        col.a = Mathf.Clamp01(rgba.a);
                    }
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

            foreach (var (renderer, origEnabled) in _hiddenRenderers)
                if (renderer != null) renderer.enabled = origEnabled;
            _hiddenRenderers.Clear();
            _hiddenIds.Clear();

            foreach (var (renderer, clones) in _cloned)
                foreach (var c in clones)
                    if (c != null) UnityEngine.Object.Destroy(c);
            _cloned.Clear();

            foreach (var (b, origEnabled) in _disabledMirrors)
                if (b != null) b.enabled = origEnabled;
            _disabledMirrors.Clear();
            _disabledMirrorIds.Clear();
            _bloomScannedRoots.Clear();

            if (_mat != null) { UnityEngine.Object.Destroy(_mat); _mat = null; }
            _ids.Clear();
            _assigned = false;
            _clonesAssigned = false;
            _lastColor = new Color(-1f, -1f, -1f, -1f);
            _refreshSettled = false;
            _logged = false;
            _hiding = false;
        }

        private void EnsureMaterial(Color rgba)
        {
            // 半透明(alpha<1)のときに深度を書き込むと、色は透けて見えても奥のオブジェクト（他Mod製の
            // カウンター等）を不透明として遮ってしまう。深度書き込みは完全不透明のときだけ行う。
            bool writeDepthNow = _writeDepth && rgba.a >= 0.999f;

            if (_mat != null)
            {
                _mat.SetColor("_Color", rgba);
                _mat.SetInt("_ZWrite", writeDepthNow ? 1 : 0);
                return;
            }

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
            _mat.SetInt("_ZWrite", writeDepthNow ? 1 : 0);
            _mat.renderQueue = (int)RenderQueue.Transparent;
        }

        private void LogOnce()
        {
            if (_logged) return;
            Plugin.DebugInfo(() =>
            {
                var names = string.Join(", ", _painted.Take(12).Select(p =>
                {
                    if (p.renderer == null) return "?";
                    var sh = p.renderer.sharedMaterial != null && p.renderer.sharedMaterial.shader != null ? p.renderer.sharedMaterial.shader.name : "?";
                    return $"{GetPath(p.renderer.transform)}<{sh}>";
                }));
                return $"[SkyLight][{_tag}] painted renderers found: {_painted.Count}, mirrors disabled: {_disabledMirrors.Count} names=[{names}]";
            });
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

        private static bool IsFloorLike(Renderer r)
        {
            var size = r.bounds.size;
            return size.y < 2f && (size.x > 3f || size.z > 3f);
        }
    }
}
