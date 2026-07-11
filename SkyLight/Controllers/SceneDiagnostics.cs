using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SkyLight.Controllers
{
    // 「なぜ明るくならないのか」を実機で突き止めるための調査ダンプ。
    // 曲開始時に1回だけ、カメラ構成・シェーダー在庫・現在の RenderSettings をログへ吐く。
    internal static class SceneDiagnostics
    {
        public static void Dump()
        {
            DumpRenderSettings();
            DumpCameras();
            DumpShaderInventory();
            ProbeSkyboxShaders();
            DumpLargeFlatRenderers();
            DumpTallRenderers();
            DumpBuildingLikeRenderers();
            DumpLights();
            DumpSceneRoots();
            DumpPlatformComponents();
            DumpFarCenterRenderers();
            DumpBakedBloomComponents();
        }

        // 「Hide Objects」設定用の候補一覧。現在の全レンダラーを名前ごとに集計して出す。
        // ここに出た名前をそのまま設定の HideObjectHints（; 区切り）へコピーすれば非表示にできる。
        // 既に Hide Objects の対象/除外になっている名前や、他の設定（Bars/Neon/Ring等）で管理済みの
        // 名前には行末に印を付けて、二重登録や巻き添えに気付けるようにする。
        public static void DumpHideCandidates(Configuration.PluginConfig config)
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
            Plugin.Log.Info(sb.ToString());
        }

        // 現在ロード中の環境シーン名（例: 'BTSEnvironment'）。環境ごとにオブジェクト名が違うため、
        // どの環境でのダンプかをヘッダーに残す。見つからなければアクティブシーン名を返す。
        private static string GetEnvironmentName()
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

        private static string[] SplitHints(string? s)
            => (s ?? string.Empty).Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        // TargetPainter.Matches と同じ基準（名前/パス/シェーダー名の部分一致）。一致したヒントを返す（無ければ null）。
        private static string? FindMatch(string name, string path, string[] shaders, string[] hints)
            => hints.FirstOrDefault(h => name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0
                              || path.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0
                              || shaders.Any(s => s.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0));

        // BakedBloomはBloomSkyboxQuadと同様、Rendererを無効化しても専用スクリプトが直接描画して
        // 消えないケースを疑い、GameObject本体と親についている全コンポーネント（Rendererに限らず）を洗い出す。
        private static void DumpBakedBloomComponents()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === BakedBloom GameObject components (self + parent) ===");
            var seen = new HashSet<int>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                if (r.gameObject.name.IndexOf("BakedBloom", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!seen.Add(r.gameObject.GetInstanceID())) continue;
                if (seen.Count > 3) break; // 代表例だけで十分

                sb.AppendLine($"  '{GetPath(r.transform)}':");
                foreach (var c in r.gameObject.GetComponents<Component>())
                    sb.AppendLine($"    self:   {c.GetType().FullName}");
                if (r.transform.parent != null)
                    foreach (var c in r.transform.parent.GetComponents<Component>())
                        sb.AppendLine($"    parent: {c.GetType().FullName}");
            }
            Plugin.Log.Info(sb.ToString());
        }

        // 名前/シェーダーで絞り込まず、トラック中心軸に近く(|x|<15)、奥にある(z>40)レンダラーを
        // 全部列挙する（消失点付近に見える正体不明の光の特定用）。enabled状態も出す。
        private static void DumpFarCenterRenderers()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Far-center renderers (|x|<15, z>40) ===");
            var candidates = Object.FindObjectsOfType<Renderer>()
                .Where(r => r != null && Mathf.Abs(r.bounds.center.x) < 15f && r.bounds.center.z > 40f)
                .OrderBy(r => r.bounds.center.z);
            foreach (var r in candidates)
            {
                var b = r.bounds;
                var shaders = string.Join(",", r.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct());
                sb.AppendLine($"  '{GetPath(r.transform)}' enabled={r.enabled} activeInHierarchy={r.gameObject.activeInHierarchy} " +
                              $"layer={r.gameObject.layer}({LayerMask.LayerToName(r.gameObject.layer)}) center={b.center} size={b.size} shaders=[{shaders}]");
            }
            Plugin.Log.Info(sb.ToString());
        }

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

        // CustomPlatforms(等)はプラットフォームのルートに"PlatformDescriptor"のような
        // 名前のコンポーネントを付けるのが定番。アセンブリ参照せずに型名一致だけで探す
        // （BloomTamerが"MainEffectController"を探すのと同じ手法）。
        private static void DumpPlatformComponents()
        {
            var sb = new StringBuilder();
            var comps = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null && m.GetType().Name.IndexOf("Platform", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            sb.AppendLine($"[SkyLight][diag] === Platform-named components ({comps.Count}) ===");
            foreach (var c in comps)
                sb.AppendLine($"  type={c.GetType().FullName} gameObject='{GetPath(c.transform)}' active={c.gameObject.activeInHierarchy}");
            Plugin.Log.Info(sb.ToString());
        }

        private static string GetRootName(Transform t)
        {
            var cur = t;
            while (cur.parent != null) cur = cur.parent;
            return cur.name;
        }

        // 「ビル/柱/街」らしい名前のレンダラーを大小問わず全部列挙（黒バーの特定用）。
        private static void DumpBuildingLikeRenderers()
        {
            string[] kw = { "Building", "Construction", "Column", "City", "Skyline", "Tower", "Block", "Pillar", "Box", "Cube", "Mesh", "Structure", "Spectrogram" };
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Building-like renderers (name match) ===");
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                var path = GetPath(r.transform);
                if (!kw.Any(k => path.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                var shaders = string.Join(",", r.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct());
                var b = r.bounds;
                sb.AppendLine($"  '{path}' layer={r.gameObject.layer}({LayerMask.LayerToName(r.gameObject.layer)}) size=({b.size.x:0.#},{b.size.y:0.#},{b.size.z:0.#}) shaders=[{shaders}]");
            }
            Plugin.Log.Info(sb.ToString());
        }

        // 背の高い大きなレンダラー（横の動くビル等の特定用）。bounds の体積が大きい順に列挙。
        private static void DumpTallRenderers()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Tall renderers (top 40 by height, height>3) ===");
            var candidates = Object.FindObjectsOfType<Renderer>()
                .Where(r => r != null && r.bounds.size.y > 3f && r.bounds.size.y < 1000f)
                .OrderByDescending(r => r.bounds.size.y)
                .Take(40);
            foreach (var r in candidates)
            {
                var b = r.bounds;
                var shaders = string.Join(",", r.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct());
                sb.AppendLine($"  '{GetPath(r.transform)}' layer={r.gameObject.layer}({LayerMask.LayerToName(r.gameObject.layer)}) " +
                              $"size={b.size} shaders=[{shaders}]");
            }
            Plugin.Log.Info(sb.ToString());
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
            // 標準シェーダー(Standard 等)が多ければ ambient が効く余地あり。unlit/custom ばかりなら効かない。
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

        private static void ProbeSkyboxShaders()
        {
            // どのスカイボックス/塗り潰し用シェーダーがビルドに含まれているかを確認する。
            string[] candidates =
            {
                "Skybox/Procedural", "Skybox/Cubemap", "Skybox/6 Sided", "Skybox/Panoramic",
                "Mobile/Skybox", "Standard", "Unlit/Color", "Unlit/Texture",
                "Sprites/Default", "UI/Default", "Hidden/Internal-Colored",
            };
            var sb = new StringBuilder();
            sb.AppendLine("[SkyLight][diag] === Shader.Find probe ===");
            foreach (var name in candidates)
                sb.AppendLine($"  {(Shader.Find(name) != null ? "OK  " : "MISS")}  {name}");
            Plugin.Log.Info(sb.ToString());
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            return string.Join("/", stack);
        }
    }
}
