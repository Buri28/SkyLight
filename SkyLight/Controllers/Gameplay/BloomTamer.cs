using System.Collections.Generic;
using UnityEngine;

namespace SkyLight.Controllers.Gameplay
{
    // 全画面ブルーム(MainEffectController)の ON/OFF だけを担う。
    // disableBloom=true で MainEffectController.enabled=false にし、明るい背景の白飛びを止める
    //（代償としてノーツ/レーザーの発光も消える）。disableBloom=false で素のブルームに戻す。
    //
    // MainEffectController はクオリティ設定確定後に実体が遅れて生成・差し替わることがあるため、
    // Refresh() で新規インスタンスだけを増分収集し、定期的に呼び直す。
    internal class BloomTamer
    {
        private readonly List<(Behaviour b, bool origEnabled)> _mainEffectTargets = new();
        private readonly HashSet<int> _trackedIds = new();
        private bool _active;
        private bool _logged;

        public void Capture()
        {
            _active = true;
            Refresh(logIfChanged: true);
        }

        // 新規に見つかった MainEffectController だけ追加する（増分収集）。
        public void Refresh(bool logIfChanged = false)
        {
            int before = _mainEffectTargets.Count;
            foreach (var b in Resources.FindObjectsOfTypeAll<Behaviour>())
            {
                if (b == null || b.GetType().Name != "MainEffectController" || !_trackedIds.Add(b.GetInstanceID())) continue;
                _mainEffectTargets.Add((b, b.enabled));
            }

            if (logIfChanged && !_logged && _mainEffectTargets.Count != before)
            {
                Plugin.Log.Info($"[SkyLight][bloomtamer] MainEffectController instances: {_mainEffectTargets.Count}");
                _logged = true;
            }
        }

        // disableBloom=true なら全画面ブルームを無効化、false なら有効化（素のまま）。
        public void Apply(bool disableBloom)
        {
            if (!_active) return;
            foreach (var (b, _) in _mainEffectTargets)
                if (b != null) b.enabled = !disableBloom;
        }

        public void Reassert(bool disableBloom) => Apply(disableBloom);

        public void Restore()
        {
            foreach (var (b, origEnabled) in _mainEffectTargets)
                if (b != null) b.enabled = origEnabled;
            _mainEffectTargets.Clear();
            _trackedIds.Clear();
            _active = false;
        }
    }
}
