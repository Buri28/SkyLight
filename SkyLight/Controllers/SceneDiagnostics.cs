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
