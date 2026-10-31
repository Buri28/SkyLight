using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SkyLight.Controllers
{
    // 実機で「なぜそう見えるのか」を調べるための診断ダンプ。
    // 診断ダンプは DebugLogging が ON のときだけ出す（呼び出し元の SkyLightController も同じ条件で呼ぶ）。
    // 曲開始時（と3フレーム後）に、カメラ構成・シェーダー在庫・RenderSettings・ライト・Hide 候補をログへ出す。
    internal static class SceneDiagnostics
    {
        public static void Dump()
        {
            if (!Plugin.IsDebugLoggingEnabled()) return;
            DumpRenderSettings();
            DumpCameras();
            DumpShaderInventory();
            DumpLargeFlatRenderers();
            DumpLights();
            DumpSceneRoots();
        }

        // 「Hide Objects」設定用の候補一覧。現在の全レンダラーを名前ごとに集計して出す。
        // ここに出た名前をそのまま設定の HideObjectHints（; 区切り）へコピーすれば非表示にできる。
        // 既に Hide Objects の対象/除外になっている名前や、他の設定（Bars/Neon/Ring等）で管理済みの
        // 名前には行末に印を付けて、二重登録や巻き添えに気付けるようにする。
        // ログへ出すのは DebugLogging が ON のときだけ。文字列は常に返す（自動テストが candidates.txt に保存する）。
        public static string DumpHideCandidates(Configuration.PluginConfig config)
        {
            var hideArr = SplitHints(config.HideObjectHints);
            var exArr = SplitHints(config.HideObjectExcludeHints);
            var barArr = SplitHints(string.IsNullOrWhiteSpace(config.BarShaderHints) ? "Spectrogram" : config.BarShaderHints);
            var barExArr = SplitHints(config.BarExcludeHints);
            var ringArr = SplitHints(string.IsNullOrWhiteSpace(config.RingShaderHints) ? "Ring" : config.RingShaderHints);
            var ringExArr = SplitHints(config.RingExcludeHints);
            var laneArr = SplitHints(config.SideLaneHints);
            var lightArr = SplitHints(config.LightHints);
            var logoArr = SplitHints(config.LogoHints);
            var otherArr = SplitHints(config.OtherStructureHints);
            var structArr = SplitHints(config.StructureShaderHints);
            var structExArr = SplitHints(config.StructureExcludeHints);
            var structExOvArr = SplitHints(config.StructureExcludeOverrideHints);
            int neonLayer = LayerMask.NameToLayer("NeonLight");

            var sb = new StringBuilder();
            var list = Object.FindObjectsOfType<Renderer>()
                .Where(r => r != null && r.gameObject.activeInHierarchy)
                .GroupBy(r => r.gameObject.name)
                .OrderBy(g => g.Key)
                .ToList();
            sb.AppendLine($"[SkyLight][diag] === Hide candidates ({list.Count} distinct names) env='{GetEnvironmentName()}' ===");
            sb.AppendLine("  copy a name below into Hide Objects (';' separated) to hide it");
            foreach (var g in list)
            {
                var rep = g.First();
                string name = g.Key;
                string path = GetPath(rep.transform);
                var shaderList = g.SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct().ToArray();
                var shaders = string.Join(",", shaderList);
                var b = rep.bounds;

                // 現在の設定でどう扱われているかの印。一致したキーワードも出す（どの設定値が効いたか逆引き用）。
                var tags = new List<string>();
                string? hit;
                if ((hit = FindMatch(name, path, shaderList, exArr)) != null) tags.Add($"EXCLUDED (HideObjectExcludeHints: '{hit}')");
                else if ((hit = FindMatch(name, path, shaderList, hideArr)) != null) tags.Add($"HIDDEN (Hide Objects: '{hit}')");
                if (rep.gameObject.layer == neonLayer) tags.Add(config.HideNeon ? "HIDDEN (Show Neon=OFF)" : "managed by Show Neon");
                string? barHit = FindMatch(name, path, shaderList, barArr);
                string? barExHit = FindMatch(name, path, shaderList, barExArr);
                if (barExHit != null) tags.Add($"EXCLUDED (BarExcludeHints: '{barExHit}')");
                else if (barHit != null) tags.Add(config.ShowBars ? $"managed by Show Bars ('{barHit}')" : $"HIDDEN (Show Bars=OFF: '{barHit}')");
                string? ringHit = FindMatch(name, path, shaderList, ringArr);
                string? ringExHit = FindMatch(name, path, shaderList, ringExArr);
                if (ringExHit != null) tags.Add($"EXCLUDED (RingExcludeHints: '{ringExHit}')");
                else if (ringHit != null) tags.Add(config.ShowRing ? $"managed by Show Ring ('{ringHit}')" : $"HIDDEN (Show Ring=OFF: '{ringHit}')");
                if ((hit = FindMatch(name, path, shaderList, laneArr)) != null)
                    tags.Add(config.ShowSideLanes ? $"managed by Side Lanes ('{hit}')" : $"HIDDEN (Side Lanes=OFF: '{hit}')");
                if ((hit = FindMatch(name, path, shaderList, lightArr)) != null)
                    tags.Add(config.ShowLights ? $"managed by Show Lights ('{hit}')" : $"HIDDEN (Show Lights=OFF: '{hit}')");
                if ((hit = FindMatch(name, path, shaderList, logoArr)) != null)
                    tags.Add(config.ShowLogo ? $"managed by Show Logo ('{hit}')" : $"HIDDEN (Show Logo=OFF: '{hit}')");
                if ((hit = FindMatch(name, path, shaderList, otherArr)) != null)
                    tags.Add(config.ShowOtherStructures ? $"managed by Other Structures ('{hit}')" : $"HIDDEN (Other Structures=OFF: '{hit}')");
                string? structHit = FindMatch(name, path, shaderList, structArr);
                string? structExHit = FindMatch(name, path, shaderList, structExArr);
                string? structExOvHit = FindMatch(name, path, shaderList, structExOvArr);
                // 除外に一致しても override に一致すれば対象に戻る（TargetPainter.Matches と同じ判定）。
                if (structExHit != null && structExOvHit != null)
                    tags.Add(config.ShowStructures
                        ? $"managed by Structures (excluded by '{structExHit}' but overridden by StructureExcludeOverrideHints: '{structExOvHit}')"
                        : $"HIDDEN (Show Structures=OFF, overridden by StructureExcludeOverrideHints: '{structExOvHit}')");
                else if (structExHit != null) tags.Add($"EXCLUDED (StructureExcludeHints: '{structExHit}')");
                else if (structHit != null)
                    tags.Add(config.ShowStructures ? $"managed by Structures ('{structHit}')" : $"HIDDEN (Show Structures=OFF: '{structHit}')");
                // どの設定にも該当しない＝現在表示中のオブジェクトは、隠す候補として「Show」印を付ける。
                if (tags.Count == 0 && g.Any(r => r.enabled))
                    tags.Add("SHOW");
                string tag = tags.Count > 0 ? $"  <== {string.Join(" / ", tags)}" : "";

                sb.AppendLine($"  name='{name}' count={g.Count()} layer={rep.gameObject.layer}({LayerMask.LayerToName(rep.gameObject.layer)}) " +
                              $"path='{path}' size=({b.size.x:0.#},{b.size.y:0.#},{b.size.z:0.#}) shaders=[{shaders}]{tag}");
            }
            var text = sb.ToString();
            if (Plugin.IsDebugLoggingEnabled()) Plugin.Log.Info(text);
            return text;
        }

        // 診断: 指定シェーダー名を含むレンダラーの、シェーダーの色・数値プロパティと、MaterialPropertyBlock で上書きされている値。
        // （バー＝Custom/Spectrogram の色を変えられるか調べるため）
        public static void DumpShaderProperties(string shaderHint)
        {
            if (!Plugin.IsDebugLoggingEnabled()) return;
            var sb = new StringBuilder();
            sb.AppendLine($"[SkyLight][diag] === Shader properties ('{shaderHint}') ===");
            var block = new MaterialPropertyBlock();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name.IndexOf(shaderHint, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                r.GetPropertyBlock(block);
                sb.AppendLine($"  '{GetPath(r.transform)}' shader={m.shader.name} mat={m.name} hasBlock={!block.isEmpty} keywords=[{string.Join(",", m.shaderKeywords)}]");
                for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                {
                    var n = m.shader.GetPropertyName(i);
                    var t = m.shader.GetPropertyType(i);
                    string val = t switch
                    {
                        UnityEngine.Rendering.ShaderPropertyType.Color => m.GetColor(n).ToString(),
                        UnityEngine.Rendering.ShaderPropertyType.Vector => m.GetVector(n).ToString(),
                        UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range => m.GetFloat(n).ToString("0.###"),
                        _ => t.ToString(),
                    };
                    string inBlock = (t == UnityEngine.Rendering.ShaderPropertyType.Color || t == UnityEngine.Rendering.ShaderPropertyType.Vector) && block.HasVector(n)
                        ? $" block={block.GetVector(n)}" : (block.HasFloat(n) ? $" block={block.GetFloat(n):0.###}" : "");
                    sb.AppendLine($"    {n} ({t}) = {val}{inBlock}");
                }
                break; // 1つで十分
            }
            Plugin.Log.Info(sb.ToString());
        }

        // 現在ロード中の環境シーン名（例: 'BTSEnvironment'）。環境ごとにオブジェクト名が違う。
        // HideObjects.json の環境名にも使う（診断ではなく本番で使うので、ここに置いてはいるが消さないこと）。
        // 見つからなければアクティブシーン名を返す。
        internal static string GetEnvironmentName()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name != null &&
                    s.name.IndexOf("Environment", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    s.name.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) < 0)
                    return s.name;
            }
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        private static string[] SplitHints(string? s) => HintUtil.Split(s);

        // TargetPainter.Matches と同じ基準（名前/パス/シェーダー名の部分一致）。一致したヒントを返す（無ければ null）。
        private static string? FindMatch(string name, string path, string[] shaders, string[] hints)
            => hints.FirstOrDefault(h => name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0
                              || path.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0
                              || shaders.Any(s => s.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0));

        // カスタムプラットフォームは通常シーンの別ルート(GameObjectの最上位祖先)にまるごとぶら下がる。
        // ルート名ごとにRendererを集計すれば、どのルートがプラットフォーム本体かひと目で分かる。
        private static void DumpSceneRoots()
        {
            var sb = new StringBuilder();
            var groups = Object.FindObjectsOfType<Renderer>()
                .Where(r => r != null)
                .GroupBy(r => GetRootName(r.transform))
                .OrderByDescending(g => g.Count())
                .ToList();
            sb.AppendLine($"[SkyLight][diag] === Renderer roots ({groups.Count} distinct) ===");
            foreach (var g in groups)
                sb.AppendLine($"  '{g.Key}' renderers={g.Count()}");
            Plugin.Log.Info(sb.ToString());
        }

        private static string GetRootName(Transform t)
        {
            var cur = t;
            while (cur.parent != null) cur = cur.parent;
            return cur.name;
        }

        // シーン内のライト（ノーツを照らす光源の特定用）。Directional/Point/Spot を色・強度・位置つきで列挙。
        private static void DumpLights()
        {
            var sb = new StringBuilder();
            var lights = Object.FindObjectsOfType<Light>();
            sb.AppendLine($"[SkyLight][diag] === Lights ({lights.Length}) ===");
            foreach (var l in lights.OrderByDescending(l => l.intensity))
            {
                if (l == null) continue;
                var c = l.color;
                sb.AppendLine($"  '{GetPath(l.transform)}' type={l.type} enabled={l.enabled} intensity={l.intensity:0.##} " +
                              $"range={l.range:0.#} color=RGBA({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##}) " +
                              $"layer={l.gameObject.layer}({LayerMask.LayerToName(l.gameObject.layer)}) cullingMask=0x{l.cullingMask:X}");
            }
            Plugin.Log.Info(sb.ToString());
        }

        // CustomPlatforms 環境では本物の床(Mirror)がプラットフォーム自身のメッシュに隠れて見えないことがあるため、
        // 「床らしい」レンダラー（Y方向に薄く、XZ方向に広い）を bounds から推定して列挙する。
        // 名前一致(FloorPaintShaderHint)が効かない環境で、本当に塗るべきオブジェクト名を特定するための調査用。
        private static void DumpLargeFlatRenderers()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Floor candidates (flat & wide renderers, top 20 by XZ area) ===");
            var candidates = Object.FindObjectsOfType<Renderer>()
                .Select(r => (r, b: r.bounds))
                .Where(t => t.b.size.y < 2f && (t.b.size.x > 3f || t.b.size.z > 3f))
                .OrderByDescending(t => t.b.size.x * t.b.size.z)
                .Take(20);
            foreach (var (r, b) in candidates)
            {
                var shaders = string.Join(",", r.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct());
                sb.AppendLine($"  '{GetPath(r.transform)}' layer={r.gameObject.layer}({LayerMask.LayerToName(r.gameObject.layer)}) " +
                               $"center={b.center} size={b.size} shaders=[{shaders}]");
            }
            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpRenderSettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === RenderSettings ===");
            sb.AppendLine($"  ambientMode={RenderSettings.ambientMode} ambientLight={RenderSettings.ambientLight} ambientIntensity={RenderSettings.ambientIntensity}");
            sb.AppendLine($"  fog={RenderSettings.fog} fogColor={RenderSettings.fogColor} fogMode={RenderSettings.fogMode}");
            sb.AppendLine($"  skybox={(RenderSettings.skybox == null ? "null" : RenderSettings.skybox.shader.name)}");
            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpCameras()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Cameras (all, incl. inactive) ===");
            // 非アクティブ含め全カメラを列挙（デスクトップ/Smooth/VR/CameraPlus などを把握する）
            var cams = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (var cam in cams.OrderByDescending(c => c.depth))
            {
                bool sceneObj = cam.gameObject.scene.IsValid(); // プレハブ資産でなく実シーン上か
                sb.AppendLine(
                    $"  '{GetPath(cam.transform)}' enabled={cam.enabled} activeInHierarchy={cam.gameObject.activeInHierarchy} " +
                    $"sceneObj={sceneObj} clearFlags={cam.clearFlags} bg={cam.backgroundColor} depth={cam.depth} " +
                    $"tag={cam.tag} targetTex={(cam.targetTexture != null)} cullingMask=0x{cam.cullingMask:X}");
            }
            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpShaderInventory()
        {
            // アクティブな Renderer が使っているシェーダー名を集計する。
            var counts = new Dictionary<string, int>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    string name = m.shader.name;
                    counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[SkyLight][diag] === Shader inventory (active renderers, {counts.Count} distinct) ===");
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(40))
                sb.AppendLine($"  {kv.Value,4}x  {kv.Key}");
            Plugin.Log.Info(sb.ToString());
        }

        private static string GetPath(Transform t) => HintUtil.GetPath(t);
    }
}
