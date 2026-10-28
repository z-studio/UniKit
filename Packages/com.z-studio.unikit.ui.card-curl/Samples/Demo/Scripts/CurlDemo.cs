using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Samples {
    /// <summary>只在导入的咪牌 Demo 场景添加旋转/八方向按钮；业务场景不自动创建 UI。</summary>
    public sealed class CurlDemo : MonoBehaviour {
        private Text m_Label;
        private int m_Completions;

        [SerializeField]
        private CardCurl m_Card;

        private void Awake() {
            if (!m_Card) {
                m_Card = FindFirstObjectByType<CardCurl>();
            }

            if (!m_Card) {
                return;
            }

            var canvas = m_Card.GetComponentInParent<Canvas>().rootCanvas;
            var panel = new GameObject("Rotation controls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(0, 170);
            rect.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.12f, 0.95f);
            m_Label = Label(rect, "", new Vector2(0, -22), new Vector2(720, 35));
            m_Card.onFlipComplete.AddListener(() => {
                m_Completions++;
                RefreshLabel();
            });
            Button(rect, "-30°", -260, -65, () => Rotate(-30));
            Button(rect, "+30°", -130, -65, () => Rotate(30));
            Button(rect, "0°", 0, -65, () => {
                m_Card.transform.localRotation = Quaternion.identity;
                RefreshLabel();
            });
            Button(rect, "Reset", 130, -65, m_Card.Reset);
            Button(rect, "Hint", 260, -65, () => m_Card.ShowDragTips(CardCurl.EDragArea.BottomLeft));

            for (var i = 0; i < 8; i++) {
                var area = (CardCurl.EDragArea)(i + 1);
                Button(rect, new[] { "BL", "BR", "TL", "TR", "Left", "Right", "Top", "Bottom" }[i], -322 + i * 92, -125,
                    () => {
                        m_Card.Reset();
                        m_Card.Flip(area);
                    }, 86);
            }

            RefreshLabel();
        }

        private void Rotate(float delta) {
            m_Card.transform.localRotation *= Quaternion.Euler(0, 0, delta);
            RefreshLabel();
        }

        internal void RefreshLabel() =>
            m_Label.text =
                $"UI Card Curl   Rotation {m_Card.transform.localEulerAngles.z:0}°   Completed {m_Completions}";

        private static Text Label(Transform parent, string text, Vector2 position, Vector2 size) {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private static void Button(Transform parent, string text, float x, float y,
            UnityEngine.Events.UnityAction action, float width = 122) {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, 46);
            go.GetComponent<Image>().color = new Color(0.18f, 0.27f, 0.32f);
            go.GetComponent<Button>().onClick.AddListener(action);

            var label = Label(go.transform, text, new Vector2(0, -23), rect.sizeDelta);
            label.fontSize = width < 100 ? 17 : 21;
        }
    }
}