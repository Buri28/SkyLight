using UnityEngine;

namespace SkyLight.AutoTest
{
    /// <summary>自動テストのコルーチンを動かすための常駐オブジェクト（シーンをまたいで残る）。</summary>
    internal class AutoTestRunner : MonoBehaviour
    {
        private static AutoTestRunner? _instance;

        public static AutoTestRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SkyLightAutoTestRunner");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<AutoTestRunner>();
                }
                return _instance;
            }
        }
    }
}
