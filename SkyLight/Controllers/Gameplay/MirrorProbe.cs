using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SkyLight.Controllers.Gameplay
{
    // ゲームのミラー画質がオン（床が反射する）かどうかを調べる。
    // Mirror.OnWillRenderObject は、自分の _mirrorRenderer（MirrorRendererSO）の RenderMirrorTexture が null を返すと
    // 床を反射しない材質(_noMirrorMaterial)に切り替える。null になるのは _reflectLayers が 0 のとき（1.40.8 / 1.44.1 をデコンパイルして確認）。
    // _reflectLayers は SettingsApplicatorSO が画質設定（settings.ini の quality.mirror）から入れる。
    //
    // 床の Mirror が実際に使っている SO だけを見る。以前は読み込まれている MirrorRendererSO の「どれか1つ」が 0 でなければ
    // 反射ありとしていたため、ゲーム中に画質を変えたあと、床の SO は 0 なのに反射ありと判定し、
    // 床を塗らずに反射しない材質のまま残すことがあった（床が消えて見えた）。
    internal static class MirrorProbe
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static bool IsReflectionOn()
        {
            bool found = false, on = false;
            foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "Mirror") continue;
                var scene = mb.gameObject.scene.name;
                if (scene != null && scene.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                found = true;
                var so = mb.GetType().GetField("_mirrorRenderer", F)?.GetValue(mb) as ScriptableObject;
                if (so != null && ReflectLayers(so) != 0) on = true;
            }

            // 診断：読み込まれている MirrorRendererSO の全部と、その _reflectLayers。
            Plugin.DebugInfo(() => "[SkyLight][floor] mirror probe: floorMirror=" + found + " on=" + on + " SOs=[" +
                string.Join(", ", Resources.FindObjectsOfTypeAll<ScriptableObject>()
                    .Where(s => s != null && s.GetType().Name == "MirrorRendererSO")
                    .Select(s => $"{s.name}#{s.GetInstanceID()}={ReflectLayers(s)}")) + "]");

            return on; // 床に Mirror が無い／床の SO が 0 ＝反射なし（直接塗りにする）
        }

        private static int ReflectLayers(ScriptableObject so)
            => so.GetType().GetField("_reflectLayers", F)?.GetValue(so) is LayerMask mask ? mask.value : 0;
    }
}
