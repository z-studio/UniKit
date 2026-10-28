using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>
    /// 示例操作入口。UI 和按钮事件保存在 Demo 场景中，可直接在 Inspector 查看与修改。
    /// 阅读顺序：订阅完成事件 → 调用牌的动作 → 更新操作说明。
    /// </summary>
    public sealed class CurlDemo : MonoBehaviour {
        [Header("场景引用")]
        [Tooltip("被演示的牌，负责拖动、动画与完成通知。")]
        [SerializeField]
        private CardCurl m_Card;

        [Tooltip("Demo controls 上的 CanvasGroup，用于启用或停止面板操作。")]
        [SerializeField]
        private CanvasGroup m_Controls;

        [Tooltip("显示角度和示例累计完成次数的 Title 文字。")]
        [SerializeField]
        private Text m_Label;

        [Tooltip("显示当前操作说明的 Status 文字。")]
        [SerializeField]
        private Text m_Status;

        private int m_Completions;
        private string m_LastStatus;

        private void Awake() {
            if (!m_Card || !m_Controls || !m_Label || !m_Status) {
                Debug.LogError("CurlDemo：请在 Inspector 绑定牌、控制面板、标题和状态文字。", this);
                enabled = false;
            }
        }

        private void OnEnable() {
            if (!m_Card || !m_Controls || !m_Label || !m_Status) {
                return;
            }

            m_Card.onFlipComplete.AddListener(OnFlipComplete);
            RefreshLabel();
        }

        private void OnDisable() {
            if (m_Card) {
                m_Card.onFlipComplete.RemoveListener(OnFlipComplete);
            }

            // 面板属于场景，禁用脚本只停止操作，不创建或销毁 UI。
            if (m_Controls) {
                m_Controls.interactable = false;
            }
        }

        private void Update() {
            // 拖动和动画由 CardCurl 自己推进；状态变化时才刷新文字。
            if (m_Status && GetStatusText() != m_LastStatus) {
                RefreshLabel();
            }
        }

        public void Rotate(float degrees) {
            if (!CanOperate()) {
                return;
            }

            m_Card.transform.localRotation *= Quaternion.Euler(0, 0, degrees);
            RefreshLabel();
        }

        public void ResetRotation() {
            if (!CanOperate()) {
                return;
            }

            m_Card.transform.localRotation = Quaternion.identity;
            RefreshLabel();
        }

        public void ResetDemo() {
            if (!CanOperate()) {
                return;
            }

            m_Card.Reset();
            m_Card.transform.localRotation = Quaternion.identity;
            m_Completions = 0;
            RefreshLabel();
        }

        public void ShowHint() {
            if (!CanOperate()) {
                return;
            }

            // Only Once 默认开启。先盖牌，保证翻开后仍能再次演示提示，并保留角度。
            m_Card.Reset();

            if (!m_Card.TryShowDragTips(CardCurl.DragArea.BottomLeft)) {
                Debug.LogWarning("CurlDemo：提示未启动，请检查 CardCurl 的 Tips Ratio 是否大于 0。", m_Card);
            }

            RefreshLabel();
        }

        // Unity 按钮不能直接配置枚举参数，使用具名入口让 Inspector 中的绑定一目了然。
        public void FlipBottomLeft() => FlipFrom(CardCurl.DragArea.BottomLeft);
        public void FlipBottomRight() => FlipFrom(CardCurl.DragArea.BottomRight);
        public void FlipTopLeft() => FlipFrom(CardCurl.DragArea.TopLeft);
        public void FlipTopRight() => FlipFrom(CardCurl.DragArea.TopRight);
        public void FlipLeft() => FlipFrom(CardCurl.DragArea.Left);
        public void FlipRight() => FlipFrom(CardCurl.DragArea.Right);
        public void FlipTop() => FlipFrom(CardCurl.DragArea.Top);
        public void FlipBottom() => FlipFrom(CardCurl.DragArea.Bottom);

        private void FlipFrom(CardCurl.DragArea area) {
            if (!CanOperate()) {
                return;
            }

            // 这些按钮用于反复演示自动翻牌，因此每次先 Reset；实际拖动没有这一步。
            m_Card.Reset();
            m_Card.Flip(area);
            RefreshLabel();
        }

        private bool CanOperate() => isActiveAndEnabled && m_Card && m_Card.isActiveAndEnabled;

        private void OnFlipComplete() {
            m_Completions++;
            RefreshLabel();
        }

        private string GetStatusText() {
            if (!m_Card || !m_Card.isActiveAndEnabled) {
                return "Card unavailable: enable the CardCurl component.";
            }

            if (m_Card.IsBusy) {
                return "Drag or animation in progress. Reset can stop it.";
            }

            if (!m_Card.IsInteractable) {
                return "Card flipped. Press Reset, Hint or an auto-flip button to replay.";
            }

            return "Ready: drag a corner or edge. The centre does not respond.";
        }

        private void RefreshLabel() {
            m_Controls.interactable = CanOperate();

            m_Label.text = m_Card
                ? $"Card Curl   Rotation {m_Card.transform.localEulerAngles.z:0}°   Completed {m_Completions}"
                : "Card unavailable";

            m_LastStatus = GetStatusText();
            m_Status.text = m_LastStatus;
        }
    }
}