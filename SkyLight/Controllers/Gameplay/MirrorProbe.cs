using System.Reflection;
using UnityEngine;

namespace SkyLight.Controllers.Gameplay
{
    // ゲームのミラー画質がオン（床が反射する）かどうかを調べる。
    // MirrorRendererSO.RenderMirrorTexture は _reflectLayers が 0 のとき null を返し、そのとき Mirror は床を
    // 反射しない材質(_noMirrorMaterial)に切り替える（1.40.8 / 1.44.1 をデコンパイルして確認）。
    // _reflectLayers は SettingsApplicatorSO が画質設定（settings.ini の quality.mirror）から入れる。
    internal static class MirrorProbe
    {
        public static bool IsReflectionOn()
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (so == null || so.GetType().Name != "MirrorRendererSO") continue;
                if (so.GetType().GetField("_reflectLayers", F)?.GetValue(so) is LayerMask mask && mask.value != 0)
                    return true;
            }
            return false; // 見つからない／全部 0 ＝反射なし（直接塗りにする）
        }
    }
}
