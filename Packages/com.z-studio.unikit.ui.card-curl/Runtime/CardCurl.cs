using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    public class CardCurl : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler {
        public enum EDragArea {
            None,
            BottomLeft,
            BottomRight,
            TopLeft,
            TopRight,
            Left,
            Right,
            Top,
            Bottom,
        }

        [Serializable]
        public class DragAnchor {
            public RectTransform bottomLeft;
            public RectTransform bottomRight;
            public RectTransform topLeft;
            public RectTransform topRight;
            public RectTransform left;
            public RectTransform right;
            public RectTransform top;
            public RectTransform bottom;
        }

        [Serializable]
        public class DragArea {
            public RectTransform bottomLeft;
            public RectTransform bottomRight;
            public RectTransform topLeft;
            public RectTransform topRight;
            public RectTransform left;
            public RectTransform right;
            public RectTransform top;
            public RectTransform bottom;
        }

        [Header("图片设置")]
        [SerializeField]
        private Sprite m_FrontImage;

        [SerializeField]
        private Sprite m_BackImage;

        [Header("复原设置")]
        [SerializeField]
        private CurlEase m_RecoveryEase = CurlEase.Linear;

        [SerializeField]
        private float m_RecoveryTime = 0.25f;

        [Header("翻页设置")]
        [Range(0f, 1f)]
        [SerializeField]
        private float m_FlipThreshold = 0.5f;

        [SerializeField]
        private CurlEase m_FlipEase = CurlEase.OutSine;

        [SerializeField]
        private float m_FlipTime = 0.25f;

        [SerializeField]
        private bool m_OnlyOnce = true;

        [Header("提示设置")]
        [SerializeField]
        private EDragArea m_StartingDragAreaTips;

        [SerializeField]
        private CurlEase m_TipsEase = CurlEase.OutSine;

        [SerializeField]
        private float m_TipsTime = 0.5f;

        [SerializeField]
        private float m_TipsOffset = 2f;

        [Header("UI组件引用")]
        [SerializeField]
        private RectTransform m_Front;

        [SerializeField]
        private RectTransform m_Back;

        [SerializeField]
        private RectTransform m_Mask;

        [SerializeField]
        private RectTransform m_Shadow;

        [SerializeField]
        private DragAnchor m_DragAnchor;

        [SerializeField]
        private DragArea m_DragArea;

        public UnityEvent onFlipComplete;

        // 所有运动状态都在 CardCurl 根节点坐标系中保存。
        // Front 位于 Back/Mask 之下，不能把它受父层级影响的世界位置作为下一帧输入。
        private RectTransform m_Rect;
        private Image m_ImgFront;
        private Image m_ImgBack;
        private EDragArea m_CurrentDragArea;
        private Vector3 m_FrontPosition;
        private Vector3 m_BeginFrontPosition;
        private Vector3 m_BeginPointerPosition;
        private float m_FrontAngle;
        private int? m_PointerId;
        private bool m_Initialized;
        private bool m_IsDragging;
        private int m_FlipCompletedCount;
        private Motion m_RecoveryTween;
        private Motion m_TipsTween;
        private Motion m_FlipTween;

        private void Awake() => Initialize();

        private void Start() {
            if (Initialize() && m_CurrentDragArea == EDragArea.None && m_FlipCompletedCount == 0) {
                ShowDragTips(m_StartingDragAreaTips);
            }
        }

        private bool Initialize() {
            if (m_Initialized) {
                return true;
            }

            m_Rect = transform as RectTransform;

            if (!m_Rect || !m_Front || !m_Back || !m_Mask || !m_Shadow || m_DragAnchor == null
                || m_DragArea == null) {
                return false;
            }

            m_ImgFront = m_Front.GetComponent<Image>();
            m_ImgBack = m_Back.GetComponent<Image>();

            if (!m_ImgFront || !m_ImgBack || !GetComponent<Image>()) {
                return false;
            }

            m_Initialized = true;
            ApplyImages();
            ResetGeometry();
            return true;
        }

        private void OnValidate() {
            m_RecoveryTime = Mathf.Max(0.001f, m_RecoveryTime);
            m_FlipTime = Mathf.Max(0.001f, m_FlipTime);
            m_TipsTime = Mathf.Max(0.001f, m_TipsTime);
            m_FlipThreshold = Mathf.Clamp01(m_FlipThreshold);

            if (Application.isPlaying) {
                return;
            }

            m_Initialized = false;
            Initialize();
        }

        private void ApplyImages() {
            var root = GetComponent<Image>();
            root.sprite = m_BackImage;
            m_ImgBack.sprite = m_BackImage;
            m_ImgFront.sprite = m_FrontImage;

            if (m_BackImage) {
                root.SetNativeSize();
                m_ImgBack.SetNativeSize();
            }

            if (m_FrontImage) {
                m_ImgFront.SetNativeSize();
            }

            float width = m_Rect.rect.width, height = m_Rect.rect.height;
            m_Mask.sizeDelta =
                new Vector2(Mathf.Max(width, height) * 2, Mathf.Sqrt(width * width + height * height) * 2);
            m_Shadow.sizeDelta = new Vector2(m_Shadow.sizeDelta.x, Mathf.Max(width, height) * 2);
        }

        public void Reset() {
            StopAnimations();
            m_IsDragging = false;
            m_PointerId = null;
            m_FlipCompletedCount = 0;

            if (!Initialize()) {
                return;
            }

            ApplyImages();
            ResetGeometry();
        }

        public void SetFrontImage(Sprite sprite) {
            m_FrontImage = sprite;
            Reset();
        }

        public void SetBackImage(Sprite sprite) {
            m_BackImage = sprite;
            Reset();
        }

        private void StopAnimations() {
            if (m_RecoveryTween.isAlive) {
                m_RecoveryTween.Stop();
            }

            if (m_TipsTween.isAlive) {
                m_TipsTween.Stop();
            }

            if (m_FlipTween.isAlive) {
                m_FlipTween.Stop();
            }
        }

        private void OnDisable() {
            StopAnimations();
            m_PointerId = null;
            m_IsDragging = false;

            if (m_Initialized) {
                ResetGeometry();
            }
        }

        private void OnDestroy() => StopAnimations();

        private Vector3 CardPosition(RectTransform anchor) => m_Rect.InverseTransformPoint(anchor.position);

        private RectTransform GetAnchor(EDragArea area) =>
            area switch {
                EDragArea.BottomLeft => m_DragAnchor.bottomLeft,
                EDragArea.BottomRight => m_DragAnchor.bottomRight,
                EDragArea.TopLeft => m_DragAnchor.topLeft,
                EDragArea.TopRight => m_DragAnchor.topRight,
                EDragArea.Left => m_DragAnchor.left,
                EDragArea.Right => m_DragAnchor.right,
                EDragArea.Top => m_DragAnchor.top,
                EDragArea.Bottom => m_DragAnchor.bottom,
                _ => null
            };

        private bool SelectArea(EDragArea area) {
            var anchor = GetAnchor(area);

            if (!anchor) {
                return false;
            }

            m_CurrentDragArea = area;
            m_FrontPosition = m_BeginFrontPosition = CardPosition(anchor);
            m_FrontAngle = 0;
            (Vector2 pivot, Vector2 attachment) = area switch {
                EDragArea.BottomLeft => (new Vector2(0, 1), new Vector2(0, 0)),
                EDragArea.BottomRight => (new Vector2(1, 1), new Vector2(1, 0)),
                EDragArea.TopLeft => (new Vector2(0, 0), new Vector2(0, 1)),
                EDragArea.TopRight => (new Vector2(1, 0), new Vector2(1, 1)),
                EDragArea.Left => (new Vector2(1, 0.5f), new Vector2(0, 0.5f)),
                EDragArea.Right => (new Vector2(0, 0.5f), new Vector2(1, 0.5f)),
                EDragArea.Top => (new Vector2(0.5f, 0), new Vector2(0.5f, 1)),
                _ => (new Vector2(0.5f, 1), new Vector2(0.5f, 0))
            };
            m_Front.pivot = pivot;
            m_Front.anchorMin = m_Front.anchorMax = attachment;
            RefreshDragTransform();
            return true;
        }

        private void ResetGeometry() {
            if (!SelectArea(EDragArea.BottomLeft)) {
                return;
            }

            m_CurrentDragArea = EDragArea.None;
        }

        // 根据根节点局部姿态提交到现有 Mask → Back → Front → Shadow 层级。
        // 顺序很重要：先移动遮罩，再反向补偿 Back，最后放置 Front 和阴影。
        private void SetCardPose(RectTransform target, Vector3 position, float angle) {
            target.SetPositionAndRotation(m_Rect.TransformPoint(position),
                m_Rect.rotation * Quaternion.Euler(0, 0, angle));
        }

        private void RefreshDragTransform() {
            if (!m_Initialized) {
                return;
            }

            var anchor = GetAnchor(m_CurrentDragArea);

            if (!anchor) {
                return;
            }

            Vector3 origin = CardPosition(anchor);
            Vector3 delta = m_FrontPosition - origin;
            float theta = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            m_FrontAngle = m_CurrentDragArea switch {
                EDragArea.BottomLeft when theta > 0 && theta < 90 => (theta - 90) * 2,
                EDragArea.BottomRight when theta > 90 && theta < 180 => (theta - 90) * 2,
                EDragArea.TopLeft when theta > -90 && theta < 0 => (theta + 90) * 2,
                EDragArea.TopRight when theta > -180 && theta < -90 => (theta + 90) * 2,
                _ => 0
            };
            float maskAngle = m_CurrentDragArea switch {
                EDragArea.BottomLeft or EDragArea.BottomRight => m_FrontAngle * 0.5f,
                EDragArea.TopLeft or EDragArea.TopRight => m_FrontAngle * 0.5f + 180,
                EDragArea.Left => -90,
                EDragArea.Right => 90,
                EDragArea.Top => 180,
                _ => 0
            };
            Vector3 fold = (origin + m_FrontPosition) * 0.5f;
            SetCardPose(m_Mask, fold, maskAngle);
            SetCardPose(m_Back, m_Rect.rect.center, 0);
            SetCardPose(m_Front, m_FrontPosition, m_FrontAngle);
            SetCardPose(m_Shadow, fold, maskAngle + 90);
        }

        private void LateUpdate() {
            TickMotion(ref m_RecoveryTween, Time.unscaledDeltaTime);
            TickMotion(ref m_TipsTween, Time.unscaledDeltaTime);
            TickMotion(ref m_FlipTween, Time.unscaledDeltaTime);
            RefreshDragTransform();
        }

        private void SetFrontPosition(Vector3 value) {
            m_FrontPosition = value;
            RefreshDragTransform();
        }

        // 动画只插值根节点局部数据；包本身不依赖第三方 Tween。
        private enum MotionCycle {
            Restart,
            Yoyo
        }

        private struct Motion {
            public bool isAlive;
            public Vector3 from, to;
            public float duration, elapsed;
            public CurlEase ease;
            public int cycles;
            public MotionCycle cycleMode;
            public Action completed;

            public void Stop() {
                isAlive = false;
                completed = null;
            }

            public Motion OnComplete(Action callback) {
                completed = callback;
                return this;
            }
        }

        private Motion MoveFront(Vector3 target, float duration, CurlEase ease, int cycles = 1,
            MotionCycle cycleMode = MotionCycle.Restart) =>
            new() {
                isAlive = true,
                from = m_FrontPosition,
                to = target,
                duration = Mathf.Max(0.001f, duration),
                ease = ease,
                cycles = cycles,
                cycleMode = cycleMode
            };

        private void TickMotion(ref Motion motion, float deltaTime) {
            if (!motion.isAlive) {
                return;
            }

            motion.elapsed += deltaTime;
            bool done = motion.elapsed >= motion.duration * motion.cycles;
            int cycle = Mathf.Min((int)(motion.elapsed / motion.duration), motion.cycles - 1);
            float t = done ? 1 : Mathf.Clamp01(motion.elapsed / motion.duration - cycle);

            if (motion.cycleMode == MotionCycle.Yoyo && (cycle & 1) != 0) {
                t = 1 - t;
            }

            float eased = motion.ease switch {
                CurlEase.InSine => 1 - Mathf.Cos(t * Mathf.PI * 0.5f),
                CurlEase.OutSine => Mathf.Sin(t * Mathf.PI * 0.5f),
                CurlEase.InOutSine => (1 - Mathf.Cos(t * Mathf.PI)) * 0.5f,
                _ => t
            };
            SetFrontPosition(Vector3.LerpUnclamped(motion.from, motion.to, eased));

            if (!done) {
                return;
            }

            Action completed = motion.completed;
            motion.Stop();
            // 终态和旧动画先结束，再允许回调创建下一段动画或 Reset。
            completed?.Invoke();
        }

        private Vector3 OppositePosition(EDragArea area) =>
            CardPosition(GetAnchor(area switch {
                EDragArea.BottomLeft => EDragArea.TopRight,
                EDragArea.BottomRight => EDragArea.TopLeft,
                EDragArea.TopLeft => EDragArea.BottomRight,
                EDragArea.TopRight => EDragArea.BottomLeft,
                EDragArea.Left => EDragArea.Right,
                EDragArea.Right => EDragArea.Left,
                EDragArea.Top => EDragArea.Bottom,
                _ => EDragArea.Top
            }));

        public void ShowDragTips(EDragArea dragArea) {
            if (!Initialize() || dragArea == EDragArea.None || m_FlipTween.isAlive || m_IsDragging ||
                (m_OnlyOnce && m_FlipCompletedCount > 0)) {
                return;
            }

            StopAnimations();

            if (!SelectArea(dragArea)) {
                return;
            }

            Vector3 direction = (OppositePosition(dragArea) - m_BeginFrontPosition).normalized;
            // 保留原 Overlay 提示幅度，但只转换局部长度，不混入世界方向和父级缩放。
            Canvas canvas = GetComponentInParent<Canvas>();
            CanvasScaler scaler = canvas ? canvas.rootCanvas.GetComponent<CanvasScaler>() : null;
            float offset = m_TipsOffset * (canvas && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? (scaler ? scaler.referencePixelsPerUnit : 100) : 1);
            m_TipsTween = MoveFront(m_BeginFrontPosition + direction * offset, m_TipsTime, m_TipsEase, 4,
                MotionCycle.Yoyo);
        }

        public void Flip(EDragArea dragArea) {
            if (!Initialize() || m_FlipTween.isAlive || (m_OnlyOnce && m_FlipCompletedCount > 0)
                || dragArea == EDragArea.None) {
                return;
            }

            StopAnimations();
            m_IsDragging = false;
            m_PointerId = null;

            if (!SelectArea(dragArea)) {
                return;
            }

            Vector3 topCenter = (CardPosition(m_DragAnchor.topLeft) + CardPosition(m_DragAnchor.topRight)) * 0.5f;
            Vector3 bottomCenter =
                (CardPosition(m_DragAnchor.bottomLeft) + CardPosition(m_DragAnchor.bottomRight)) * 0.5f;
            Vector3 destination = dragArea switch {
                EDragArea.BottomLeft or EDragArea.BottomRight => topCenter,
                EDragArea.TopLeft or EDragArea.TopRight => bottomCenter,
                _ => OppositePosition(dragArea)
            };
            m_FlipTween = MoveFront(Vector3.Lerp(m_BeginFrontPosition, destination, m_FlipThreshold), m_FlipTime,
                    m_FlipEase)
                .OnComplete(TriggerFlip);
        }

        private EDragArea HitArea(GameObject hit) {
            if (!hit) {
                return EDragArea.None;
            }

            if (hit == m_DragArea.bottomLeft.gameObject) {
                return EDragArea.BottomLeft;
            }

            if (hit == m_DragArea.bottomRight.gameObject) {
                return EDragArea.BottomRight;
            }

            if (hit == m_DragArea.topLeft.gameObject) {
                return EDragArea.TopLeft;
            }

            if (hit == m_DragArea.topRight.gameObject) {
                return EDragArea.TopRight;
            }

            if (hit == m_DragArea.left.gameObject) {
                return EDragArea.Left;
            }

            if (hit == m_DragArea.right.gameObject) {
                return EDragArea.Right;
            }

            if (hit == m_DragArea.top.gameObject) {
                return EDragArea.Top;
            }

            if (hit == m_DragArea.bottom.gameObject) {
                return EDragArea.Bottom;
            }

            return EDragArea.None;
        }

        private bool PointerPosition(PointerEventData data, out Vector3 position) {
            position = default;
            // 非等比父级叠加 X/Y 倾斜时，rotation * forward 不是实际平面法线。
            // 从完整变换矩阵的两条切线构造平面，再将命中点转回牌局部空间。
            Vector3 origin = m_Rect.TransformPoint(Vector3.zero);
            Vector3 normal = Vector3.Cross(m_Rect.TransformVector(Vector3.right), m_Rect.TransformVector(Vector3.up));

            if (normal.sqrMagnitude < 1e-12f) {
                return false;
            }

            Camera camera = data.pressEventCamera;
            Ray ray = camera ? camera.ScreenPointToRay(data.position)
                : new Ray(new Vector3(data.position.x, data.position.y, -100), Vector3.forward);

            if (!new Plane(normal, origin).Raycast(ray, out float distance)) {
                return false;
            }

            position = m_Rect.InverseTransformPoint(ray.GetPoint(distance));
            position.z = 0;
            return true;
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (!Initialize() || m_PointerId.HasValue || m_FlipTween.isAlive ||
                eventData.button != PointerEventData.InputButton.Left
                || (m_OnlyOnce && m_FlipCompletedCount > 0)) {
                return;
            }

            EDragArea area = HitArea(eventData.pointerCurrentRaycast.gameObject
                ? eventData.pointerCurrentRaycast.gameObject : eventData.pointerEnter);

            if (area == EDragArea.None || !PointerPosition(eventData, out var position)) {
                return;
            }

            StopAnimations();

            if (!SelectArea(area)) {
                return;
            }

            m_PointerId = eventData.pointerId;
            m_BeginPointerPosition = position;
            m_IsDragging = true;
        }

        private Vector3 ConstrainDelta(Vector3 delta) {
            if (m_CurrentDragArea is EDragArea.Left or EDragArea.Right) {
                delta.y = 0;
            }

            if (m_CurrentDragArea is EDragArea.Top or EDragArea.Bottom) {
                delta.x = 0;
            }

            delta.z = 0;
            return IsValidDrag(delta) ? delta : Vector3.zero;
        }

        private bool IsValidDrag(Vector3 delta) =>
            m_CurrentDragArea switch {
                EDragArea.BottomLeft => delta.x >= 0 && delta.y >= 0,
                EDragArea.BottomRight => delta.x <= 0 && delta.y >= 0,
                EDragArea.TopLeft => delta.x >= 0 && delta.y <= 0,
                EDragArea.TopRight => delta.x <= 0 && delta.y <= 0,
                EDragArea.Left => delta.x >= 0,
                EDragArea.Right => delta.x <= 0,
                EDragArea.Top => delta.y <= 0,
                EDragArea.Bottom => delta.y >= 0,
                _ => false
            };

        public void OnDrag(PointerEventData eventData) {
            if (!m_IsDragging || m_PointerId != eventData.pointerId || m_FlipTween.isAlive
                || !PointerPosition(eventData, out var position)) {
                return;
            }

            SetFrontPosition(m_BeginFrontPosition + ConstrainDelta(position - m_BeginPointerPosition));
        }

        public void OnEndDrag(PointerEventData eventData) {
            if (!m_IsDragging || m_PointerId != eventData.pointerId) {
                return;
            }

            OnDrag(eventData);
            m_IsDragging = false;
            m_PointerId = null;
            float total = Vector3.Distance(m_BeginFrontPosition, GetFlipTargetPosition());
            float progress = total > 0.0001f ? Vector3.Distance(m_BeginFrontPosition, m_FrontPosition) / total : 0;

            if (progress >= m_FlipThreshold) {
                TriggerFlip();
            } else {
                m_RecoveryTween = MoveFront(m_BeginFrontPosition, m_RecoveryTime, m_RecoveryEase);
            }
        }

        public void OnPointerUp(PointerEventData eventData) => OnEndDrag(eventData);

        private Vector3 GetFlipTargetPosition() {
            float angle = Mathf.Repeat(m_FrontAngle, 360);
            EDragArea target = m_CurrentDragArea switch {
                EDragArea.BottomLeft => angle < 270 ? EDragArea.BottomRight : EDragArea.TopLeft,
                EDragArea.BottomRight => angle > 90 ? EDragArea.BottomLeft : EDragArea.TopRight,
                EDragArea.TopLeft => angle > 90 ? EDragArea.TopRight : EDragArea.BottomLeft,
                EDragArea.TopRight => angle < 270 ? EDragArea.TopLeft : EDragArea.BottomRight,
                EDragArea.Left => EDragArea.Right,
                EDragArea.Right => EDragArea.Left,
                EDragArea.Top => EDragArea.Bottom,
                EDragArea.Bottom => EDragArea.Top,
                _ => EDragArea.None
            };
            return target == EDragArea.None ? m_BeginFrontPosition : CardPosition(GetAnchor(target));
        }

        private void TriggerFlip() {
            if (m_FlipTween.isAlive || (m_OnlyOnce && m_FlipCompletedCount > 0)) {
                return;
            }

            m_FlipTween = MoveFront(GetFlipTargetPosition(), m_FlipTime, m_FlipEase).OnComplete(OnFlipComplete);
        }

        private void OnFlipComplete() {
            (m_ImgFront.sprite, m_ImgBack.sprite) = (m_ImgBack.sprite, m_ImgFront.sprite);
            m_FlipCompletedCount++;
            ResetGeometry();
            
            // 最后通知，业务在回调内 Reset/换图时不会被旧动作再次覆盖。
            onFlipComplete?.Invoke();
        }
    }
}