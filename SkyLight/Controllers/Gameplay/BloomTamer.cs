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
        private int _emptyRefreshes;
        private bool _settled;     // MainEffectController が出揃ったら探索を止める
        private bool _active;
        private bool _logged;
        private bool _everApplied;
        private bool _lastDisable;

        public void Capture()
        {
            _active = true;
            Refresh(logIfChanged: true);
        }

        // 新規に見つかった MainEffectController だけ追加する（増分収集）。出揃ったら探索を停止。
        public void Refresh(bool logIfChanged = false)
        {
            if (_settled) return;
            int before = _mainEffectTargets.Count;
            foreach (var b in Resources.FindObjectsOfTypeAll<Behaviour>())
            {
                if (b == null || b.GetType().Name != "MainEffectController" || !_trackedIds.Add(b.GetInstanceID())) continue;
                _mainEffectTargets.Add((b, b.enabled));
            }

            if (_mainEffectTargets.Count != before)
            {
                _emptyRefreshes = 0;
                if (logIfChanged && !_logged)
                {
                    Plugin.DebugInfo(() => $"[SkyLight][bloomtamer] MainEffectController instances: {_mainEffectTargets.Count}");
                    _logged = true;
                }
            }
            else if (++_emptyRefreshes >= 6)
            {
                _settled = true; // 連続6回新規ゼロ＝出揃った。以降 FindObjectsOfTypeAll を回さない。
            }
        }

        // disableBloom=true なら全画面ブルームを無効化、false なら有効化（素のまま）。
        // OFF（disableBloom=true）はライトイベントで再有効化されうるので毎フレーム強制。
        // ON は通常そのままなので、OFF→ON に切り替わった初回だけ戻す（毎フレームのループを避ける）。
        public void Apply(bool disableBloom)
        {
            if (!_active) return;
            if (disableBloom)
            {
                // カメラの MainEffectController.enabled を false にする。ON/OFF は毎フレーム強制。
                foreach (var (b, _) in _mainEffectTargets)
                {
                    if (b != null)
                    {
                        b.enabled = false;  
                        Plugin.DebugLog($"[SkyLight][bloomtamer] disabling {b.name} (was enabled={b.enabled})");
                    } 
                }
            }
            else if (!_everApplied || _lastDisable)
            {
                // カメラの MainEffectController.enabled を true に戻す。ON/OFF は毎フレーム強制。
                foreach (var (b, _) in _mainEffectTargets)
                {
                    if (b != null)
                    {
                        b.enabled = true;
                        Plugin.DebugLog($"[SkyLight][bloomtamer] enabling {b.name} (was enabled={b.enabled})");
                    }
                }
            }
            _everApplied = true;
            _lastDisable = disableBloom;
        }

        public void Reassert(bool disableBloom) => Apply(disableBloom);

        public void Restore()
        {
            foreach (var (b, origEnabled) in _mainEffectTargets)
                if (b != null) b.enabled = origEnabled;
            _mainEffectTargets.Clear();
            _trackedIds.Clear();
            _emptyRefreshes = 0;
            _settled = false;
            _everApplied = false;
            _active = false;
        }
    }
}
