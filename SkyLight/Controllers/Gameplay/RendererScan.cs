using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyLight.Controllers.Gameplay
{
    // シーン内レンダラーの一括スキャン結果。FindObjectsOfType と、レンダラーごとの
    // 名前/パス/シェーダー名/sharedMaterials の取得（いずれも呼ぶたびにコストがかかる）を
    // 1回だけ行い、複数の TargetPainter・SkyBackdrop で共有する。
    // メニュー系シーンのオブジェクトは収集時点で除外済み（各利用側での判定は不要）。
    internal sealed class RendererScan
    {
        internal readonly struct Entry
        {
            public readonly Renderer Renderer;
            public readonly string Name;
            public readonly string Path;
            public readonly string[] ShaderNames;
            public readonly Material[] SharedMaterials; // 取得時点のスナップショット（原状復帰用に共有可）

            public Entry(Renderer r, string name, string path, string[] shaderNames, Material[] sharedMaterials)
            {
                Renderer = r;
                Name = name;
                Path = path;
                ShaderNames = shaderNames;
                SharedMaterials = sharedMaterials;
            }
        }

        public readonly List<Entry> Entries = new();

        public static RendererScan Capture()
        {
            var scan = new RendererScan();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || IsMenuObject(r)) continue;

                var mats = r.sharedMaterials; // プロパティは呼ぶたび配列コピーなので1回だけ
                int shaderCount = 0;
                foreach (var m in mats)
                    if (m != null && m.shader != null) shaderCount++;
                var shaders = new string[shaderCount];
                int si = 0;
                foreach (var m in mats)
                    if (m != null && m.shader != null) shaders[si++] = m.shader.name;

                scan.Entries.Add(new Entry(r, r.gameObject.name, GetPath(r.transform), shaders, mats));
            }
            return scan;
        }

        // メニュー系シーン（MenuCore / MenuEnvironment / MenuViewControllers / MainMenu 等）に属する
        // オブジェクトは触らない。プレイ中もこれらのシーンはロードされたまま残っており、
        // 巻き添えで非表示・材質差し替えするとメニューへ戻った際の表示（スコアボードの文字等）が壊れる。
        private static bool IsMenuObject(Renderer r)
        {
            var sceneName = r.gameObject.scene.name;
            return sceneName != null && sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            return string.Join("/", stack);
        }
    }
}
