using UnityEngine;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>预制体页面内独立按钮的示例，将点击反馈转发给场景中的演示控制器。</summary>
    public sealed class DemoPageAction : MonoBehaviour {
        /// <summary>由预制体按钮的点击事件调用，实例化后沿父层级查找场景控制器。</summary>
        public void Click() {
            var demo = GetComponentInParent<Demo>();

            if (demo != null) {
                demo.PageAction();
            }
        }
    }
}