using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkyLight.Controllers
{
    // ヒント文字列（; 区切り）と、オブジェクトのパスの共通処理。
    internal static class HintUtil
    {
        // "a; b;;c" → ["a", "b", "c"]（空は捨てる）
        public static string[] Split(string? s)
            => (s ?? string.Empty).Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        // Environment/Car4/Body のような、ルートからの階層パス。
        public static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Push(cur.name);
            return string.Join("/", stack);
        }
    }
}
