using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SkyLight.Controllers
{
    // 白飛びの主因である「メインカメラのポストプロセス・ブルーム」を特定するための調査ダンプ。
    // 実際に画面を描いている MainCamera のコンポーネント一覧と、ブルーム/ポストエフェクト系の
    // ScriptableObject / Component を列挙する。1回だけ実行する。
    internal static class BloomDiagnostics
    {
        private static bool _done;

        public static void DumpOnce()
        {
            if (_done) return;
            _done = true;

            DumpMainCameraComponents();
            DumpEffectObjects();
            DumpMainEffectBindings();
            DumpBrightRendererCandidates();
        }

        private static void DumpMainCameraComponents()
        {
            // ゲームプレイ中に実描画している MainCamera（VRGameCore 配下）を狙う
            var cam = Camera.allCameras.FirstOrDefault(c =>
                c.CompareTag("MainCamera") && c.isActiveAndEnabled && c.targetTexture == null);
            cam ??= Camera.main;

            var sb = new StringBuilder();
            if (cam == null)
            {
                sb.AppendLine("[SkyLight][bloom] No active MainCamera found.");
                Plugin.Log.Info(sb.ToString());
                return;
            }

            sb.AppendLine($"[SkyLight][bloom] MainCamera='{cam.name}' allowHDR={cam.allowHDR} components:");
            foreach (var comp in cam.GetComponents<Component>())
                sb.AppendLine($"    {comp.GetType().FullName}");
            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpEffectObjects()
        {
            string[] keys = { "bloom", "maineffect", "tonemap", "postprocess", "colorgrad", "smaa", "fxaa" };

            // Component（カメラ付随のエフェクト挙動）
            var comps = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null && keys.Any(k => m.GetType().Name.ToLowerInvariant().Contains(k)))
                .Select(m => m.GetType().FullName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            // ScriptableObject（強度などのパラメータを持つ設定アセット）
            var sos = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                .Where(s => s != null && keys.Any(k => s.GetType().Name.ToLowerInvariant().Contains(k)))
                .Select(s => s.GetType().FullName)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"[SkyLight][bloom] Effect MonoBehaviours ({comps.Count}):");
            foreach (var c in comps) sb.AppendLine($"    {c}");
            sb.AppendLine($"[SkyLight][bloom] Effect ScriptableObjects ({sos.Count}):");
            foreach (var s in sos) sb.AppendLine($"    {s}");
            Plugin.Log.Info(sb.ToString());
        }

        // MainEffectController が握っている tone/color/exposure 系の参照を出して、
        // Bloom 以外の最終合成パラメータがどこにぶら下がっているかを確認する。
        private static void DumpMainEffectBindings()
        {
            var mainEffects = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null && m.GetType().Name == "MainEffectController")
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"[SkyLight][bloom] MainEffectController bindings ({mainEffects.Count}):");
            foreach (var mainEffect in mainEffects)
            {
                sb.AppendLine($"    controller={mainEffect.GetType().FullName} gameObject='{mainEffect.gameObject.name}' enabled={mainEffect.enabled}");
                DumpObjectMembers(sb, mainEffect, indent: "      ");
            }
            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpObjectMembers(StringBuilder sb, object target, string indent)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (!ShouldLogMember(field.Name)) continue;
                AppendMemberValue(sb, indent, field.Name, field.FieldType, SafeGet(() => field.GetValue(target)));
            }

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                if (!ShouldLogMember(prop.Name)) continue;
                AppendMemberValue(sb, indent, prop.Name, prop.PropertyType, SafeGet(() => prop.GetValue(target)));
            }
        }

        private static bool ShouldLogMember(string name)
        {
            string lowered = name.ToLowerInvariant();
            return lowered.Contains("bloom")
                || lowered.Contains("color")
                || lowered.Contains("tone")
                || lowered.Contains("exposure")
                || lowered.Contains("fog")
                || lowered.Contains("effect")
                || lowered.Contains("renderer")
                || lowered.Contains("profile")
                || lowered.Contains("post");
        }

        private static object? SafeGet(Func<object?> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return $"<read failed: {ex.GetType().Name}>";
            }
        }

        private static void AppendMemberValue(StringBuilder sb, string indent, string name, Type memberType, object? value)
        {
            if (value == null)
            {
                sb.AppendLine($"{indent}{memberType.Name} {name} = null");
                return;
            }

            if (value is UnityEngine.Object unityObject)
            {
                sb.AppendLine($"{indent}{memberType.Name} {name} = {unityObject.name} ({unityObject.GetType().FullName})");
                if (unityObject is ScriptableObject so)
                    DumpNestedScriptableObject(sb, so, indent + "  ");
                return;
            }

            sb.AppendLine($"{indent}{memberType.Name} {name} = {FormatValue(value)}");
        }

        private static void DumpNestedScriptableObject(StringBuilder sb, ScriptableObject so, string indent)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in so.GetType().GetFields(flags))
            {
                if (!ShouldLogMember(field.Name)) continue;
                object? value;
                try
                {
                    value = field.GetValue(so);
                }
                catch (Exception ex)
                {
                    value = $"<read failed: {ex.GetType().Name}>";
                }

                if (value is UnityEngine.Object nestedObject)
                    sb.AppendLine($"{indent}{field.FieldType.Name} {field.Name} = {nestedObject.name} ({nestedObject.GetType().FullName})");
                else
                    sb.AppendLine($"{indent}{field.FieldType.Name} {field.Name} = {FormatValue(value)}");
            }
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "null";
            if (value is float f) return f.ToString("0.###");
            if (value is double d) return d.ToString("0.###");
            if (value is bool b) return b ? "true" : "false";
            if (value is Color c) return $"RGBA({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})";
            if (value is Vector4 v4) return $"({v4.x:0.###}, {v4.y:0.###}, {v4.z:0.###}, {v4.w:0.###})";
            if (value is Vector3 v3) return $"({v3.x:0.###}, {v3.y:0.###}, {v3.z:0.###})";
            return value.ToString() ?? "<null-string>";
        }

        // 背景そのものではなく、周囲の明るい構造物や発光材質が画面全体を白っぽく見せている可能性を調べる。
        private static void DumpBrightRendererCandidates()
        {
            var candidates = UnityEngine.Object.FindObjectsOfType<Renderer>()
                .Select(CreateRendererProbe)
                .Where(p => p != null)
                .Cast<RendererProbe>()
                .OrderByDescending(p => p.Score)
                .Take(30)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"[SkyLight][bloom] Bright renderer candidates ({candidates.Count}):");
            foreach (var p in candidates)
            {
                sb.AppendLine(
                    $"    path='{GetPath(p.Renderer.transform)}' layer={p.Renderer.gameObject.layer}({LayerMask.LayerToName(p.Renderer.gameObject.layer)}) " +
                    $"score={p.Score:0.###} area={p.Area:0.###} shader={p.ShaderName} color={p.ColorText} emission={p.EmissionText}");
            }
            Plugin.Log.Info(sb.ToString());
        }

        private static RendererProbe? CreateRendererProbe(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return null;

            var materials = renderer.sharedMaterials
                .Where(m => m != null)
                .Cast<Material>()
                .Where(m => m.shader != null)
                .ToArray();
            if (materials.Length == 0) return null;

            float bestScore = 0f;
            string shaderName = materials[0].shader.name;
            Color? bestColor = null;
            Color? bestEmission = null;

            foreach (var material in materials)
            {
                if (material == null || material.shader == null) continue;

                shaderName = material.shader.name;
                var color = ReadColor(material, "_Color") ?? ReadColor(material, "_BaseColor");
                var emission = ReadColor(material, "_EmissionColor");
                float colorLuma = color.HasValue ? MaxChannel(color.Value) : 0f;
                float emissionLuma = emission.HasValue ? MaxChannel(emission.Value) : 0f;
                float area = renderer.bounds.size.x * renderer.bounds.size.y + renderer.bounds.size.x * renderer.bounds.size.z + renderer.bounds.size.y * renderer.bounds.size.z;
                float score = (colorLuma * 0.7f + emissionLuma * 1.3f) * Mathf.Max(1f, area);

                if (score <= bestScore) continue;
                bestScore = score;
                bestColor = color;
                bestEmission = emission;
            }

            if (bestScore <= 0.5f) return null;

            return new RendererProbe(renderer, bestScore, renderer.bounds.size.x * renderer.bounds.size.z, shaderName,
                FormatColor(bestColor), FormatColor(bestEmission));
        }

        private static Color? ReadColor(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : null;
        }

        private static float MaxChannel(Color color)
        {
            return Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        }

        private static string FormatColor(Color? color)
        {
            if (!color.HasValue) return "n/a";
            var c = color.Value;
            return $"RGBA({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})";
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            return string.Join("/", stack);
        }

        private sealed class RendererProbe
        {
            public Renderer Renderer { get; }
            public float Score { get; }
            public float Area { get; }
            public string ShaderName { get; }
            public string ColorText { get; }
            public string EmissionText { get; }

            public RendererProbe(Renderer renderer, float score, float area, string shaderName, string colorText, string emissionText)
            {
                Renderer = renderer;
                Score = score;
                Area = area;
                ShaderName = shaderName;
                ColorText = colorText;
                EmissionText = emissionText;
            }
        }
    }
}
