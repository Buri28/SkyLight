using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SkyLight.Controllers
{
    // 「環境オーバーライド」設定を SkyLight 側から読み書きできないか調査するためのダンプ。
    // ソロ選曲メニュー画面のロード時に1回だけ、型名に"Environment"を含むComponent/ScriptableObject
    // を列挙し、関連フィールドを出す（BloomTamer/BloomDiagnostics と同じ「型名だけで探す」手法）。
    internal static class MenuDiagnostics
    {
        private static bool _done;

        public static void DumpOnce()
        {
            if (_done) return;
            _done = true;

            DumpEnvironmentRelatedObjects();
        }

        private static void DumpEnvironmentRelatedObjects()
        {
            var sb = new StringBuilder();

            var comps = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null && m.GetType().Name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            sb.AppendLine($"[SkyLight][menu] Environment-named MonoBehaviours ({comps.Count}):");
            foreach (var c in comps)
            {
                sb.AppendLine($"  type={c.GetType().FullName} gameObject='{c.gameObject.name}'");
                DumpFields(sb, c, "    ");
            }

            var sos = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                .Where(s => s != null && s.GetType().Name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0
                            && s.GetType().Name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            sb.AppendLine($"[SkyLight][menu] Environment+Settings ScriptableObjects ({sos.Count}):");
            foreach (var s in sos)
            {
                sb.AppendLine($"  type={s.GetType().FullName} asset='{s.name}'");
                DumpFields(sb, s, "    ");
            }

            // "MainSettingsModel"のような、環境オーバーライドを保持していそうな設定モデルも直接探す。
            var settingsModels = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(o => o != null && o.GetType().Name.IndexOf("SettingsModel", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .ToList();
            sb.AppendLine($"[SkyLight][menu] *SettingsModel objects ({settingsModels.Count}):");
            foreach (var o in settingsModels)
            {
                sb.AppendLine($"  type={o.GetType().FullName} name='{o.name}'");
                DumpFields(sb, o, "    ");
            }

            Plugin.Log.Info(sb.ToString());
        }

        private static void DumpFields(StringBuilder sb, object target, string indent)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (field.Name.IndexOf("environment", StringComparison.OrdinalIgnoreCase) < 0
                    && field.Name.IndexOf("override", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                object? value;
                try { value = field.GetValue(target); }
                catch (Exception ex) { value = $"<read failed: {ex.GetType().Name}>"; }

                if (value is UnityEngine.Object uo)
                    sb.AppendLine($"{indent}{field.FieldType.Name} {field.Name} = {uo.name} ({uo.GetType().FullName})");
                else
                    sb.AppendLine($"{indent}{field.FieldType.Name} {field.Name} = {value ?? "null"}");
            }

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                if (prop.Name.IndexOf("environment", StringComparison.OrdinalIgnoreCase) < 0
                    && prop.Name.IndexOf("override", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                object? value;
                try { value = prop.GetValue(target); }
                catch (Exception ex) { value = $"<read failed: {ex.GetType().Name}>"; }

                if (value is UnityEngine.Object uo)
                    sb.AppendLine($"{indent}{prop.PropertyType.Name} {prop.Name} = {uo.name} ({uo.GetType().FullName})");
                else
                    sb.AppendLine($"{indent}{prop.PropertyType.Name} {prop.Name} = {value ?? "null"}");
            }
        }
    }
}
