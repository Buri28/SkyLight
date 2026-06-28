using UnityEngine;

namespace SkyLight.Controllers
{
    // hex(#RRGGBB) 文字列と UnityEngine.Color の相互変換。設定値の保持/適用に使う。
    internal static class ColorUtil
    {
        // "#RRGGBB"（先頭#は任意）を Color に変換する。失敗時は引数 fallback を返す。
        public static Color ParseHex(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c))
                return c;
            return fallback;
        }

        // Color を "#RRGGBB" に変換する（アルファは無視）。
        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
