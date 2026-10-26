using System.Collections.Generic;
using UnityEngine;

namespace SkyLight.Controllers.Gameplay
{
    // 全画面ブルーム(MainEffectController)の ON/OFF だけを担う。
    // disableBloom=true で MainEffectController.enabled=false にする（代償としてノーツ/レーザーの発光も消える）。
    // disableBloom=false では触った分だけ元の状態に戻す（元から無効だったものは有効にしない）。
    // 対象は曲の読み込み時（Capture）に1回だけ集める。
    internal class BloomTamer
    {
        private readonly List<(Behaviour b, bool origEnabled)> _mainEffectTargets = new();
        private bool _active;
        private bool _logged;
        private bool _disabledByUs; // 今こちらが無効化しているか

        public void Capture()
        {
            _active = true;
            _mainEffectTargets.Clear();
            foreach (var b in Resources.FindObjectsOfTypeAll<Behaviour>())
                if (b != null && b.GetType().Name == "MainEffectController")
                    _mainEffectTargets.Add((b, b.enabled));

            if (!_logged)
            {
                Plugin.DebugInfo(() => $"[SkyLight][bloomtamer] MainEffectController instances: {_mainEffectTargets.Count}");
                _logged = true;
            }
        }

        // 毎フレーム呼ばれる。OFF はライト演出等で再有効化されうるので、enabled を見て必要なときだけ false にする。
        // OFF→ON に切り替わったときだけ元の状態に戻す（毎フレームのループを避ける）。
        public void Reassert(bool disableBloom)
        {
            if (!_active) return;
            if (disableBloom)
            {
                foreach (var (b, _) in _mainEffectTargets)
                    if (b != null && b.enabled) b.enabled = false;
                _disabledByUs = true;
            }
            else if (_disabledByUs)
            {
                RestoreEnabled();
                _disabledByUs = false;
            }
        }

        public void Restore()
        {
            RestoreEnabled();
            _mainEffectTargets.Clear();
            _disabledByUs = false;
            _active = false;
        }

        private void RestoreEnabled()
        {
            foreach (var (b, origEnabled) in _mainEffectTargets)
                if (b != null && b.enabled != origEnabled) b.enabled = origEnabled;
        }
    }
}
