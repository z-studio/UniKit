using UnityEngine;
using UnityEngine.EventSystems;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 挂在跑马灯内容（或无缝单元）上的点击转发器，
    /// 收到点击后回调到所属 <see cref="Marquee"/>。
    /// 需要内容上的 Graphic（Image/Text）启用 raycastTarget，且 Canvas 含 GraphicRaycaster。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarqueeClickRelay : MonoBehaviour, IPointerClickHandler {
        private Marquee m_Owner;
        private MarqueeItemData m_Item;
        private int m_Index;

        public void Bind(Marquee owner, MarqueeItemData item, int index) {
            m_Owner = owner;
            m_Item = item;
            m_Index = index;
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (m_Owner != null) {
                m_Owner.NotifyItemClicked(m_Item, m_Index);
            }
        }
    }
}
