using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>页面可选的生命周期扩展，轮播会收集并调用页面子层级中的组件（包括未激活节点）。</summary>
    public abstract class BannerPageBehaviour : MonoBehaviour {
        /// <summary>
        /// 页面选中后提供建议的剩余停留时间，单位与轮播的时间模式一致。
        /// 返回 false 时不参与自动时长计算；多个组件取最大值，显式页面 Duration 优先。
        /// </summary>
        public virtual bool TryGetRemainingDuration(bool useUnscaledTime, out float duration) {
            duration = 0f;
            return false;
        }

        /// <summary>页面开始显示时调用，包括拖拽过程中出现的候选页预览。</summary>
        public virtual void OnPageShown() {
        }

        /// <summary>页面停稳并正式选中时调用，可在此开始或重播演出；轮播重新启用时也会调用。</summary>
        public virtual void OnPageSelected() {
        }

        /// <summary>页面隐藏前调用，包括切出、取消预览、重建内容或禁用轮播，可在此暂停演出。</summary>
        public virtual void OnPageHidden() {
        }
    }
}
