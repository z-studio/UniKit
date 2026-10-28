using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>示例拥有生成的控制面板与事件订阅；业务场景不自动创建 UI。</summary>
    public sealed class CurlDemo : MonoBehaviour {
        private static readonly (string label, CardCurl.DragArea area)[] s_Directions = {
            ("BL", CardCurl.DragArea.BottomLeft), ("BR", CardCurl.DragArea.BottomRight),
            ("TL", CardCurl.DragArea.TopLeft), ("TR", CardCurl.DragArea.TopRight), ("Left", CardCurl.DragArea.Left),
            ("Right", CardCurl.DragArea.Right), ("Top", CardCurl.DragArea.Top), ("Bottom", CardCurl.DragArea.Bottom)
        };

        [SerializeField]
        private CardCurl m_Card;

        private GameObject m_Panel;
        private Text m_Label, m_Status;
        private CanvasGroup m_Controls;
        private int m_Completions;
        private bool m_WasBusy, m_WasInteractable, m_WasAvailable;
        private string m_Message = "Drag an edge or corner";

        private void Awake() {
            if (!m_Card) {
                m_Card = FindFirstObjectByType<CardCurl>();
            }

            var canvas = m_Card ? m_Card.GetComponentInParent<Canvas>() : null;

            if (!canvas) {
                Debug.LogError("CurlDemo requires a CardCurl inside a Canvas.", this);
                enabled = false;
                return;
            }

            m_Panel = new GameObject("Card Curl controls", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            m_Panel.transform.SetParent(canvas.rootCanvas.transform, false);
           
            var rect = (RectTransform)m_Panel.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            m_Panel.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.12f, 0.95f);
           
            var layout = m_Panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            m_Panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_Controls = m_Panel.GetComponent<CanvasGroup>();
            m_Label = Label(rect, 30);
            m_Status = Label(rect, 26);
            m_Status.fontSize = 20;

            var row = Row(rect);
            Button(row, "-30°", () => Rotate(-30));
            Button(row, "+30°", () => Rotate(30));
            Button(row, "0°", () => {
                m_Card.transform.localRotation = Quaternion.identity;
                RefreshLabel();
            });
            Button(row, "Reset", ResetDemo);
            Button(row, "Hint", ShowHint);

            for (var i = 0; i < s_Directions.Length; i++) {
                if (i % 4 == 0) {
                    row = Row(rect);
                }

                var direction = s_Directions[i];
                Button(row, direction.label, () => {
                    m_Card.Reset();
                    m_Message = m_Card.TryFlip(direction.area) ? "Flip " + direction.label : "Flip unavailable";
                    RefreshLabel();
                });
            }

            RefreshLabel();
        }

        private void OnEnable() {
            if (!m_Panel || !m_Card) {
                return;
            }

            m_Card.onFlipComplete.AddListener(OnFlipComplete);
            m_Panel.SetActive(true);
            RefreshLabel();
        }

        private void OnDisable() {
            if (m_Card) {
                m_Card.onFlipComplete.RemoveListener(OnFlipComplete);
            }

            if (m_Panel) {
                m_Panel.SetActive(false);
            }
        }

        private void OnDestroy() {
            if (m_Panel) {
                Destroy(m_Panel);
            }
        }

        private void Update() {
            if (m_Label && (m_WasAvailable != (m_Card && m_Card.isActiveAndEnabled) ||
                            (m_Card && (m_WasBusy != m_Card.IsBusy || m_WasInteractable != m_Card.IsInteractable)))) {
                RefreshLabel();
            }
        }

        private void OnFlipComplete() {
            m_Completions++;
            m_Message = "Flip completed";
            RefreshLabel();
        }

        private void ResetDemo() {
            m_Card.Reset();
            m_Card.transform.localRotation = Quaternion.identity;
            m_Completions = 0;
            m_Message = "Reset complete";
            RefreshLabel();
        }

        private void ShowHint() {
            // 示例允许反复演示，包括 Only Once 已完成的牌，并保留旋转角度。
            m_Card.Reset();
            m_Message = m_Card.TryShowDragTips(CardCurl.DragArea.BottomLeft) ? "Hint: bottom left" : "Hint unavailable";
            RefreshLabel();
        }

        private void Rotate(float delta) {
            m_Card.transform.localRotation *= Quaternion.Euler(0, 0, delta);
            RefreshLabel();
        }

        private void RefreshLabel() {
            m_WasAvailable = m_Card && m_Card.isActiveAndEnabled;
            m_WasBusy = m_Card && m_Card.IsBusy;
            m_WasInteractable = m_Card && m_Card.IsInteractable;
            m_Controls.interactable = m_WasAvailable;
            m_Label.text = m_Card
                ? $"Card Curl   Rotation {m_Card.transform.localEulerAngles.z:0}°   Completed {m_Completions}"
                : "Card unavailable";
            string state = !m_WasAvailable
                ? "Unavailable"
                : m_WasBusy
                    ? "Busy"
                    : m_WasInteractable
                        ? "Ready"
                        : "Reset to replay";
            m_Status.text = state + " | " + m_Message;
        }

        private static RectTransform Row(Transform parent) {
            var go = new GameObject("Button row", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
           
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            go.GetComponent<LayoutElement>().preferredHeight = 48;
            return (RectTransform)go.transform;
        }

        private static Text Label(Transform parent, float height) {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
           
            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 24;
         
            var element = go.GetComponent<LayoutElement>();
            element.minWidth = 0;
            element.preferredHeight = height;
            return label;
        }

        private static void Button(Transform parent, string text, UnityAction action) {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().flexibleWidth = 1;
            go.GetComponent<Image>().color = new Color(0.18f, 0.27f, 0.32f);
            go.GetComponent<Button>().onClick.AddListener(action);
           
            var label = Label(go.transform, 48);
            label.text = text;
            label.resizeTextMaxSize = 21;
           
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4, 2);
            rect.offsetMax = new Vector2(-4, -2);
        }
    }
}