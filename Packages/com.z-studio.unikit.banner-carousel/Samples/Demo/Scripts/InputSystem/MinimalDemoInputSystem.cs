using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>安装并启用新输入系统时，为最小示例提供对应的 UI 输入模块。</summary>
    internal static class MinimalDemoInputSystem {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() {
            // OnEnable 会创建并启用默认 UI actions，无需场景中额外绑定 InputActionAsset。
            MinimalDemo.ConfigureInputModule = events => events.AddComponent<InputSystemUIInputModule>();
        }
    }
}
