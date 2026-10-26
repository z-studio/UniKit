using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.LuckyWheel.Samples {
    /// <summary>UGUI 调试示例。业务决定索引，按钮只负责发起旋转和提交结果。</summary>
    public class Demo : MonoBehaviour {
        [SerializeField]
        private WheelController m_Wheel;

        [Min(1), SerializeField]
        private int m_SectorsCount = 6;

        [SerializeField]
        private int m_TargetIndex;

        [SerializeField]
        private bool m_ShowDebugPanel = true;

        [SerializeField]
        private bool m_ShowSectorNumbers = true;

        [Header("UGUI Debug Panel")]
        [SerializeField]
        private GameObject m_DebugPanel;

        [SerializeField]
        private Text m_StateLabel;

        [SerializeField]
        private Text m_ResultLabel;

        [SerializeField]
        private Text m_TargetLabel;

        [SerializeField]
        private Button m_DecreaseButton;

        [SerializeField]
        private Button m_IncreaseButton;

        [SerializeField]
        private Button m_SpinButton;

        [SerializeField]
        private Button m_BeginWaitingButton;

        [SerializeField]
        private Button m_SubmitResultButton;

        [SerializeField]
        private Button m_CancelButton;

        [SerializeField]
        private Button m_CenterSpinButton;

        [SerializeField]
        private Text[] m_SectorLabels;

        private string m_LastResult = "No result";

        private void Start() {
            m_Wheel.Initialize(m_SectorsCount);
            RefreshUI();
        }

        private void OnEnable() {
            if (!m_Wheel) {
                return;
            }

            m_Wheel.Completed += OnCompleted;
            m_Wheel.Cancelled += OnCancelled;
            m_DecreaseButton.onClick.AddListener(DecreaseTarget);
            m_IncreaseButton.onClick.AddListener(IncreaseTarget);
            m_SpinButton.onClick.AddListener(Spin);
            m_BeginWaitingButton.onClick.AddListener(BeginWaiting);
            m_SubmitResultButton.onClick.AddListener(SubmitResult);
            m_CancelButton.onClick.AddListener(Cancel);
            m_CenterSpinButton.onClick.AddListener(Spin);
            RefreshUI();
        }

        private void OnDisable() {
            if (!m_Wheel) {
                return;
            }

            m_Wheel.Completed -= OnCompleted;
            m_Wheel.Cancelled -= OnCancelled;
            m_DecreaseButton.onClick.RemoveListener(DecreaseTarget);
            m_IncreaseButton.onClick.RemoveListener(IncreaseTarget);
            m_SpinButton.onClick.RemoveListener(Spin);
            m_BeginWaitingButton.onClick.RemoveListener(BeginWaiting);
            m_SubmitResultButton.onClick.RemoveListener(SubmitResult);
            m_CancelButton.onClick.RemoveListener(Cancel);
            m_CenterSpinButton.onClick.RemoveListener(Spin);
        }

        private void OnCompleted(int index) {
            m_LastResult = $"Completed: sector {index}";
            RefreshUI();
        }

        private void OnCancelled() {
            m_LastResult = "Cancelled (no result)";
            RefreshUI();
        }

        private void DecreaseTarget() {
            m_TargetIndex--;
            RefreshUI();
        }

        private void IncreaseTarget() {
            m_TargetIndex++;
            RefreshUI();
        }

        public void Spin() {
            if (m_Wheel.TryStartSpin(m_TargetIndex)) {
                m_LastResult = "Waiting for completion";
            }

            RefreshUI();
        }

        public void BeginWaiting() {
            if (m_Wheel.BeginSpin()) {
                m_LastResult = "Waiting for server result";
            }

            RefreshUI();
        }

        public void SubmitResult() {
            m_Wheel.StopAt(m_TargetIndex);
            RefreshUI();
        }

        public void Cancel() => m_Wheel.CancelSpin();

        private void LateUpdate() {
            RefreshUI();

            if (m_Wheel.Reel is not RectTransform reel || m_SectorLabels == null || m_SectorLabels.Length == 0) {
                return;
            }

            if (m_SectorLabels.Length < m_Wheel.SectorsCount) {
                var previousCount = m_SectorLabels.Length;
                System.Array.Resize(ref m_SectorLabels, m_Wheel.SectorsCount);

                for (var index = previousCount; index < m_SectorLabels.Length; index++) {
                    m_SectorLabels[index] = Instantiate(m_SectorLabels[0], reel);
                    m_SectorLabels[index].name = $"Sector {index}";
                    m_SectorLabels[index].text = index.ToString();
                }
            }

            var radius = Mathf.Min(reel.rect.width, reel.rect.height) * 0.32f;

            for (var index = 0; index < m_SectorLabels.Length; index++) {
                var label = m_SectorLabels[index];
                label.gameObject.SetActive(m_ShowSectorNumbers && index < m_Wheel.SectorsCount);

                if (!label.gameObject.activeSelf) {
                    continue;
                }

                var radians = -m_Wheel.GetSectorAngle(index) * Mathf.Deg2Rad;
                label.rectTransform.anchoredPosition = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * radius;
                
                // 编号随扇区移动，文字保持正向；不参与射线检测，不遮挡转盘按钮。
                label.rectTransform.localRotation = Quaternion.Euler(0, 0, -reel.localEulerAngles.z);
            }
        }

        private void RefreshUI() {
            if (!m_Wheel) {
                return;
            }

            m_TargetIndex = Mathf.Clamp(m_TargetIndex, 0, m_Wheel.SectorsCount - 1);
            m_DebugPanel.SetActive(m_ShowDebugPanel);
            m_StateLabel.text = $"State: {m_Wheel.State}";
            m_ResultLabel.text = m_LastResult;
            m_TargetLabel.text = $"Target: {m_TargetIndex} / {m_Wheel.SectorsCount - 1}";
            m_DecreaseButton.interactable = m_TargetIndex > 0;
            m_IncreaseButton.interactable = m_TargetIndex < m_Wheel.SectorsCount - 1;
            
            var canStart = m_Wheel.isActiveAndEnabled && !m_Wheel.IsSpinning;
            m_SpinButton.interactable = canStart;
            m_CenterSpinButton.interactable = canStart;
            m_BeginWaitingButton.interactable = canStart;
            m_SubmitResultButton.interactable = m_Wheel.CanSubmitResult;
            m_CancelButton.interactable = m_Wheel.IsSpinning;
        }
    }
}