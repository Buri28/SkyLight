using UnityEngine;
using UnityEngine.Rendering;

namespace SkyLight.Controllers.Gameplay
{
    // 半透明で塗った形の、アルファだけを 0 に戻す材質（SkyBackdrop と TargetPainter が共有する）。
    // 全画面ブルームは画面のアルファで光る量を決めるので、半透明（SrcAlpha ブレンド）で塗ると、塗った画素にアルファが残って少し光る。
    // そこで塗った直後に、同じ形を Blend DstColor Zero・色 (1,1,1,0) で描く：RGB = 1 × 画面の色 = そのまま、アルファ = 0 × 画面のアルファ = 0。
    // 深度テストありなので、手前の物がある画素は触らない。キューは 塗り(Transparent-2) → これ(Transparent-1) → レーザーなど半透明物(Transparent)。
    // cull：空ドーム（内側から見る立方体）は Front、ふつうのオブジェクトは Off。
    internal static class AlphaClearMaterial
    {
        public static Material Create(Shader shader, CullMode cull)
        {
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
            mat.SetInt("_SrcBlend", (int)BlendMode.DstColor);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_Cull", (int)cull);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            mat.renderQueue = (int)RenderQueue.Transparent - 1;
            return mat;
        }
    }
}
