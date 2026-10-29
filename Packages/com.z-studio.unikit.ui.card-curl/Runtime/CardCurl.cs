using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI {
    /// <summary>
    /// 根 RectTransform 定义牌的尺寸和八个交互方向；所有运动数据都在它的局部坐标系中。
    /// 输入只修改状态，LateUpdate 统一推进动画和提交几何，完成通知最后派发。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public class CardCurl : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerDownHandler,
        IPointerUpHandler, ICanvasRaycastFilter {
        public enum DragArea {
            None,
            BottomLeft,
            BottomRight,
            TopLeft,
            TopRight,
            Left,
            Right,
            Top,
            Bottom
        }

        private enum State {
            Idle,
            Dragging,
            Returning,
            Hinting,
            PreparingFlip,
            Flipping
        }

        [Header("图片设置")]
        [SerializeField]
        private Sprite m_FrontImage;

        [SerializeField]
        private Sprite m_BackImage;

        [Header("复原设置")]
        [SerializeField]
        private CurlEase m_RecoveryEase = CurlEase.Linear;

        [SerializeField, Min(0.001f)]
        private float m_RecoveryTime = 0.25f;

        [Header("翻页设置")]
        [Tooltip("松手时达到此进度则翻牌；设为 0 时允许轻触翻牌。")]
        [SerializeField, Range(0, 1)]
        private float m_FlipThreshold = 0.5f;

        [SerializeField]
        private CurlEase m_FlipEase = CurlEase.OutSine;

        [SerializeField, Min(0.001f)]
        private float m_FlipTime = 0.25f;

        [SerializeField]
        private bool m_OnlyOnce = true;

        [Header("提示设置")]
        [SerializeField]
        private DragArea m_StartingDragAreaTips;

        [SerializeField]
        private CurlEase m_TipsEase = CurlEase.OutSine;

        [SerializeField, Min(0.001f)]
        private float m_TipsTime = 0.5f;

        [Tooltip("提示距离占当前方向跨度的比例，随牌尺寸变化；最大为跨度的四分之一。")]
        [SerializeField, Range(0, 0.25f)]
        private float m_TipsRatio = 0.15f;

        [Header("UI组件引用")]
        [SerializeField]
        private RectTransform m_Mask;

        [SerializeField]
        private RectTransform m_Back;

        [SerializeField]
        private RectTransform m_Front;

        [SerializeField]
        private RectTransform m_Shadow;

        public UnityEvent onFlipComplete = new();

        [Tooltip("未翻开并完成回弹复原后触发。主动 Reset、提示结束或被打断的回弹不触发。")]
        public UnityEvent onRecoveryComplete = new();

        /// <summary>正在拖动、回弹、提示或翻牌。</summary>
        public bool IsBusy => m_State != State.Idle;

        /// <summary>可接收新的交互；提示与回弹允许被打断。</summary>
        public bool IsInteractable => m_Initialized && CanInteract;

        /// <summary>自上次 Reset 或换图以来完成的翻牌次数。</summary>
        public int FlipCompletedCount { get; private set; }

        // 与原预制体一致：四角各占宽高的 30%，中间四条边承接整边拖动。
        private const float k_HotspotSize = 0.3f;
        private RectTransform m_Rect;
        private Image m_ImgRoot, m_ImgBack, m_ImgFront;
        private State m_State;
        private DragArea m_CurrentDragArea;
        private Motion m_Motion;
        private Vector3 m_FrontPosition, m_BeginFrontPosition, m_BeginPointerPosition;
        private int? m_PointerId;
        private bool m_Initialized, m_GeometryDirty;
        private volatile bool m_ConfigurationDirty = true;
        private Rect m_LastRect;
        private Matrix4x4 m_LastMatrix;
        private string m_LastError;

        private void Awake() => Initialize();

        private void OnEnable() => m_GeometryDirty = true;

        private void Start() {
            if (Initialize() && m_State == State.Idle && FlipCompletedCount == 0) {
                ShowDragTips(m_StartingDragAreaTips);
            }
        }

        private void OnValidate() {
            // 这里只校验数据。Inspector 的预览由 Editor 在主线程调用 Reset 完成。
            m_RecoveryTime = Mathf.Max(0.001f, m_RecoveryTime);
            m_FlipTime = Mathf.Max(0.001f, m_FlipTime);
            m_TipsTime = Mathf.Max(0.001f, m_TipsTime);
            m_TipsRatio = Mathf.Clamp(m_TipsRatio, 0, 0.25f);
            m_FlipThreshold = Mathf.Clamp01(m_FlipThreshold);
            m_ConfigurationDirty = true;
        }

        /// <summary>绑定缺失或内部层级不正确时，返回可直接定位的原因。</summary>
        public bool ValidateConfiguration(out string error) {
            if (!m_Mask) {
                error = "未绑定 m_Mask。";
            } else if (!m_Back) {
                error = "未绑定 m_Back。";
            } else if (!m_Front) {
                error = "未绑定 m_Front。";
            } else if (!m_Shadow) {
                error = "未绑定 m_Shadow。";
            } else if (!m_Back.GetComponent<Image>()) {
                error = "m_Back 缺少 Image。";
            } else if (!m_Front.GetComponent<Image>()) {
                error = "m_Front 缺少 Image。";
            } else if (!m_Mask.GetComponent<Graphic>()) {
                error = "m_Mask 缺少 UGUI Graphic。";
            } else if (!m_Shadow.GetComponent<Graphic>()) {
                error = "m_Shadow 缺少 UGUI Graphic。";
            } else if (m_Mask.parent != transform || m_Back.parent != m_Mask ||
                       m_Front.parent != m_Back || m_Shadow.parent != m_Front) {
                error = "内部层级应为 CardCurl → Mask → Back → Front → Shadow。";
            } else if (!m_Mask.GetComponent<Mask>() || !m_Front.GetComponent<Mask>()) {
                error = "Mask 和 Front 均需要 UGUI Mask 组件。";
            } else {
                error = null;
                return true;
            }

            return false;
        }

        private bool Initialize() {
            if (m_ConfigurationDirty) {
                m_ConfigurationDirty = false;
                m_Initialized = false;
                CancelInteraction();
                FlipCompletedCount = 0;
            }

            if (m_Initialized) {
                return true;
            }

            if (!ValidateConfiguration(out string error)) {
                if (Application.isPlaying && error != m_LastError) {
                    Debug.LogError("CardCurl: " + error, this);
                }

                m_LastError = error;
                return false;
            }

            m_LastError = null;
            m_Rect = (RectTransform)transform;
            m_ImgRoot = GetComponent<Image>();
            m_ImgBack = m_Back.GetComponent<Image>();
            m_ImgFront = m_Front.GetComponent<Image>();
            m_Initialized = true;
            ApplyImages();
            UpdateLayout();
            ResetGeometry();
            return true;
        }

        private void ApplyImages() {
            // 图片内容与尺寸分离：不修改根节点尺寸、锚点或外部布局。
            if (m_ImgRoot.sprite != m_BackImage) {
                m_ImgRoot.sprite = m_BackImage;
            }

            if (m_ImgBack.sprite != m_BackImage) {
                m_ImgBack.sprite = m_BackImage;
            }

            if (m_ImgFront.sprite != m_FrontImage) {
                m_ImgFront.sprite = m_FrontImage;
            }
        }

        private void UpdateLayout() {
            Rect rect = m_Rect.rect;
            float max = Mathf.Max(rect.width, rect.height);
            m_Mask.sizeDelta = new Vector2(max * 2, rect.size.magnitude * 2);
            SetSize(m_Back, rect.size);
            SetSize(m_Front, rect.size);
            m_Shadow.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, max * 2);
            m_LastRect = rect;
            m_GeometryDirty = true;
        }

        private static void SetSize(RectTransform rect, Vector2 size) {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        }

        public void Reset() {
            if (!Initialize()) {
                return;
            }

            CancelInteraction();
            FlipCompletedCount = 0;
            ApplyImages();

            if (m_Rect.rect != m_LastRect) {
                UpdateLayout();
            }

            ResetGeometry();

            if (!Application.isPlaying || !isActiveAndEnabled) {
                CommitGeometry();
            }
        }

        public void SetFrontImage(Sprite sprite) {
            m_FrontImage = sprite;
            Reset();
        }

        public void SetBackImage(Sprite sprite) {
            m_BackImage = sprite;
            Reset();
        }

        public void SetImages(Sprite front, Sprite back) {
            m_FrontImage = front;
            m_BackImage = back;
            Reset();
        }

        private void CancelInteraction() {
            m_State = State.Idle;
            m_Motion = default;
            m_PointerId = null;
        }

        private void OnDisable() {
            CancelInteraction();

            if (m_Initialized) {
                ResetGeometry();

                // 单独禁用组件时 UI 仍可见，需要立即显示取消后的平铺状态。
                CommitGeometry();
            }
        }

        private void ResetGeometry() {
            SelectArea(DragArea.BottomLeft);
            m_State = State.Idle;
        }

        private bool CanInteract =>
            isActiveAndEnabled && !(m_OnlyOnce && FlipCompletedCount > 0) &&
            m_State != State.PreparingFlip && m_State != State.Flipping;

        private Vector3 CardPosition(DragArea area) {
            Rect r = m_Rect.rect;
            return area switch {
                DragArea.BottomLeft => new Vector3(r.xMin, r.yMin),
                DragArea.BottomRight => new Vector3(r.xMax, r.yMin),
                DragArea.TopLeft => new Vector3(r.xMin, r.yMax),
                DragArea.TopRight => new Vector3(r.xMax, r.yMax),
                DragArea.Left => new Vector3(r.xMin, r.center.y),
                DragArea.Right => new Vector3(r.xMax, r.center.y),
                DragArea.Top => new Vector3(r.center.x, r.yMax),
                _ => new Vector3(r.center.x, r.yMin)
            };
        }

        private static DragArea Opposite(DragArea area) =>
            area switch {
                DragArea.BottomLeft => DragArea.TopRight,
                DragArea.BottomRight => DragArea.TopLeft,
                DragArea.TopLeft => DragArea.BottomRight,
                DragArea.TopRight => DragArea.BottomLeft,
                DragArea.Left => DragArea.Right,
                DragArea.Right => DragArea.Left,
                DragArea.Top => DragArea.Bottom,
                _ => DragArea.Top
            };

        private bool SelectArea(DragArea area) {
            if (area is < DragArea.BottomLeft or > DragArea.Bottom) {
                return false;
            }

            m_CurrentDragArea = area;
            m_FrontPosition = m_BeginFrontPosition = CardPosition(area);
            m_GeometryDirty = true;
            return true;
        }

        public void ShowDragTips(DragArea dragArea) => TryShowDragTips(dragArea);

        /// <summary>无效方向、不可交互、正在拖动或提示幅度为零时返回 false。</summary>
        public bool TryShowDragTips(DragArea dragArea) {
            if (!Initialize() || !CanInteract || m_State == State.Dragging ||
                dragArea is < DragArea.BottomLeft or > DragArea.Bottom) {
                return false;
            }

            Vector3 span = CardPosition(Opposite(dragArea)) - CardPosition(dragArea);
            float ratio = Mathf.Clamp(m_TipsRatio, 0, 0.25f);

            if (ratio <= 0 || span.sqrMagnitude <= 0.0001f) {
                return false;
            }

            SelectArea(dragArea);
            StartMotion(State.Hinting, m_BeginFrontPosition + span * ratio,
                m_TipsTime, m_TipsEase, 4);
            return true;
        }

        public void Flip(DragArea dragArea) => TryFlip(dragArea);

        /// <summary>无效方向、Only Once 已完成、已禁用或正在翻牌时返回 false。</summary>
        public bool TryFlip(DragArea dragArea) {
            if (!Initialize() || !CanInteract || !SelectArea(dragArea)) {
                return false;
            }

            m_PointerId = null;
            Rect r = m_Rect.rect;

            Vector3 destination = dragArea switch {
                DragArea.BottomLeft or DragArea.BottomRight => new Vector3(r.center.x, r.yMax),
                DragArea.TopLeft or DragArea.TopRight => new Vector3(r.center.x, r.yMin),
                _ => CardPosition(Opposite(dragArea))
            };

            StartMotion(State.PreparingFlip, Vector3.Lerp(m_BeginFrontPosition, destination, m_FlipThreshold),
                m_FlipTime, m_FlipEase);
            return true;
        }

        private struct Motion {
            public Vector3 from, to;
            public float duration, elapsed;
            public CurlEase ease;
            public int cycles;
        }

        private void StartMotion(State state, Vector3 target, float duration, CurlEase ease, int cycles = 1) {
            m_State = state;
            m_Motion = new Motion {
                from = m_FrontPosition,
                to = target,
                duration = Mathf.Max(0.001f, duration),
                ease = ease,
                cycles = cycles
            };
        }

        private bool AdvanceMotion(float deltaTime) {
            m_Motion.elapsed += deltaTime;
            bool done = m_Motion.elapsed >= m_Motion.duration * m_Motion.cycles;
            int cycle = Mathf.Min((int)(m_Motion.elapsed / m_Motion.duration), m_Motion.cycles - 1);
            float t = done ? 1 : Mathf.Clamp01(m_Motion.elapsed / m_Motion.duration - cycle);

            if (m_State == State.Hinting && (cycle & 1) != 0) {
                t = 1 - t;
            }

            float eased = m_Motion.ease switch {
                CurlEase.InSine => 1 - Mathf.Cos(t * Mathf.PI * 0.5f),
                CurlEase.OutSine => Mathf.Sin(t * Mathf.PI * 0.5f),
                CurlEase.InOutSine => (1 - Mathf.Cos(t * Mathf.PI)) * 0.5f,
                _ => t
            };

            m_FrontPosition = Vector3.LerpUnclamped(m_Motion.from, m_Motion.to, eased);
            m_GeometryDirty = true;
            return done;
        }

        private void LateUpdate() {
            if (m_ConfigurationDirty && !Initialize()) {
                return;
            }

            if (!m_Initialized) {
                return;
            }

            if (m_Rect.rect != m_LastRect) {
                if (m_State is State.PreparingFlip or State.Flipping) {
                    // 已提交的翻牌继续完成；把动画通道重映射到新尺寸，保留时钟。
                    m_FrontPosition = ResizePoint(m_FrontPosition);
                    m_Motion.from = ResizePoint(m_Motion.from);
                    m_Motion.to = ResizePoint(m_Motion.to);
                    m_BeginFrontPosition = CardPosition(m_CurrentDragArea);
                    UpdateLayout();
                } else {
                    // 未提交的触点/提示不再适用于新布局，释放后重新开始。
                    CancelInteraction();
                    UpdateLayout();
                    ResetGeometry();
                }
            }

            var completed = false;
            var recovered = false;

            if (m_State is State.Returning or State.Hinting or State.PreparingFlip or State.Flipping) {
                if (AdvanceMotion(Time.unscaledDeltaTime)) {
                    if (m_State == State.PreparingFlip) {
                        BeginFlip();
                    } else {
                        completed = m_State == State.Flipping;
                        recovered = m_State == State.Returning;

                        if (completed) {
                            (m_ImgFront.sprite, m_ImgBack.sprite) = (m_ImgBack.sprite, m_ImgFront.sprite);
                            FlipCompletedCount++;
                        }

                        ResetGeometry();
                    }
                }
            }

            if (m_GeometryDirty || m_Rect.localToWorldMatrix != m_LastMatrix) {
                CommitGeometry();
            }

            // 状态与画面先完成，再允许业务在回调内 Reset、换图或销毁物体。
            if (completed) {
                onFlipComplete?.Invoke();
            } else if (recovered) {
                onRecoveryComplete?.Invoke();
            }
        }

        private Vector3 ResizePoint(Vector3 point) {
            Rect next = m_Rect.rect;
            float u = m_LastRect.width > 0 ? (point.x - m_LastRect.xMin) / m_LastRect.width : 0;
            float v = m_LastRect.height > 0 ? (point.y - m_LastRect.yMin) / m_LastRect.height : 0;
            return new Vector3(next.xMin + u * next.width, next.yMin + v * next.height);
        }

        private float CurlAngle() {
            Vector3 delta = m_FrontPosition - CardPosition(m_CurrentDragArea);
            float theta = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            return m_CurrentDragArea switch {
                DragArea.BottomLeft when theta is > 0 and < 90 => (theta - 90) * 2,
                DragArea.BottomRight when theta is > 90 and < 180 => (theta - 90) * 2,
                DragArea.TopLeft when theta is > -90 and < 0 => (theta + 90) * 2,
                DragArea.TopRight when theta is > -180 and < -90 => (theta + 90) * 2,
                _ => 0
            };
        }

        private Vector3 GetFlipTargetPosition() {
            float angle = Mathf.Repeat(CurlAngle(), 360);

            var target = m_CurrentDragArea switch {
                DragArea.BottomLeft => angle < 270 ? DragArea.BottomRight : DragArea.TopLeft,
                DragArea.BottomRight => angle > 90 ? DragArea.BottomLeft : DragArea.TopRight,
                DragArea.TopLeft => angle > 90 ? DragArea.TopRight : DragArea.BottomLeft,
                DragArea.TopRight => angle < 270 ? DragArea.TopLeft : DragArea.BottomRight,
                _ => Opposite(m_CurrentDragArea)
            };

            return CardPosition(target);
        }

        private void BeginFlip() => StartMotion(State.Flipping, GetFlipTargetPosition(), m_FlipTime, m_FlipEase);

        private void CommitGeometry() {
            var pivot = m_CurrentDragArea switch {
                DragArea.BottomLeft => new Vector2(0, 1),
                DragArea.BottomRight => Vector2.one,
                DragArea.TopLeft => Vector2.zero,
                DragArea.TopRight => new Vector2(1, 0),
                DragArea.Left => new Vector2(1, 0.5f),
                DragArea.Right => new Vector2(0, 0.5f),
                DragArea.Top => new Vector2(0.5f, 0),
                _ => new Vector2(0.5f, 1)
            };

            var attachment = Vector2.one - pivot;

            if (m_Front.pivot != pivot) {
                m_Front.pivot = pivot;
            }

            if (m_Front.anchorMin != attachment) {
                m_Front.anchorMin = attachment;
            }

            if (m_Front.anchorMax != attachment) {
                m_Front.anchorMax = attachment;
            }

            if (m_Front.rect.size != m_Rect.rect.size) {
                SetSize(m_Front, m_Rect.rect.size);
            }

            float frontAngle = CurlAngle();

            float maskAngle = m_CurrentDragArea switch {
                DragArea.BottomLeft or DragArea.BottomRight => frontAngle * 0.5f,
                DragArea.TopLeft or DragArea.TopRight => frontAngle * 0.5f + 180,
                DragArea.Left => -90,
                DragArea.Right => 90,
                DragArea.Top => 180,
                _ => 0
            };

            Vector3 fold = (CardPosition(m_CurrentDragArea) + m_FrontPosition) * 0.5f;

            // 先动遮罩，再补偿它的子物体；不能交换提交顺序。
            SetCardPose(m_Mask, fold, maskAngle);
            SetCardPose(m_Back, m_Rect.rect.center, 0);
            SetCardPose(m_Front, m_FrontPosition, frontAngle);
            SetCardPose(m_Shadow, fold, maskAngle + 90);
            m_LastMatrix = m_Rect.localToWorldMatrix;
            m_GeometryDirty = false;
        }

        private void SetCardPose(RectTransform target, Vector3 position, float angle) =>
            target.SetPositionAndRotation(m_Rect.TransformPoint(position),
                m_Rect.rotation * Quaternion.Euler(0, 0, angle));

        private DragArea HitArea(Vector3 point) {
            Rect r = m_Rect.rect;

            if (r.width <= 0 || r.height <= 0 || !r.Contains(point)) {
                return DragArea.None;
            }

            Vector2 uv = ((Vector2)point - r.min) / r.size;
            bool left = uv.x < k_HotspotSize, right = uv.x > 1 - k_HotspotSize;
            bool bottom = uv.y < k_HotspotSize, top = uv.y > 1 - k_HotspotSize;

            if (bottom && left) {
                return DragArea.BottomLeft;
            }

            if (bottom && right) {
                return DragArea.BottomRight;
            }

            if (top && left) {
                return DragArea.TopLeft;
            }

            if (top && right) {
                return DragArea.TopRight;
            }

            if (left) {
                return DragArea.Left;
            }

            if (right) {
                return DragArea.Right;
            }

            if (top) {
                return DragArea.Top;
            }

            return bottom ? DragArea.Bottom : DragArea.None;
        }

        private bool PointerPosition(Vector2 screen, Camera camera, out Vector3 position) {
            position = default;
            Vector3 origin = m_Rect.TransformPoint(Vector3.zero);
            Vector3 normal = Vector3.Cross(m_Rect.TransformVector(Vector3.right), m_Rect.TransformVector(Vector3.up));

            if (normal.sqrMagnitude < 1e-12f) {
                return false;
            }

            var ray = camera ? camera.ScreenPointToRay(screen)
                : new Ray(new Vector3(screen.x, screen.y, -100), Vector3.forward);

            if (!new Plane(normal, origin).Raycast(ray, out float distance)) {
                return false;
            }

            position = m_Rect.InverseTransformPoint(ray.GetPoint(distance));
            position.z = 0;
            return true;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) =>
            m_Initialized && CanInteract && !m_PointerId.HasValue &&
            PointerPosition(screenPoint, eventCamera, out var point) && HitArea(point) != DragArea.None;

        public void OnPointerDown(PointerEventData data) {
            if (!Initialize() || !CanInteract || m_PointerId.HasValue
                || data.button != PointerEventData.InputButton.Left ||
                !PointerPosition(data.position, data.pressEventCamera, out var point)) {
                return;
            }

            DragArea area = HitArea(point);

            if (area == DragArea.None || !SelectArea(area)) {
                return;
            }

            m_State = State.Dragging;
            m_PointerId = data.pointerId;
            m_BeginPointerPosition = point;
        }

        public void OnDrag(PointerEventData data) {
            if (m_State != State.Dragging || m_PointerId != data.pointerId ||
                !PointerPosition(data.position, data.pressEventCamera, out var point)) {
                return;
            }

            Vector3 delta = point - m_BeginPointerPosition;

            if (m_CurrentDragArea is DragArea.Left or DragArea.Right) {
                delta.y = 0;
            }

            if (m_CurrentDragArea is DragArea.Top or DragArea.Bottom) {
                delta.x = 0;
            }

            delta.z = 0;

            bool valid = m_CurrentDragArea switch {
                DragArea.BottomLeft => delta.x >= 0 && delta.y >= 0,
                DragArea.BottomRight => delta.x <= 0 && delta.y >= 0,
                DragArea.TopLeft => delta.x >= 0 && delta.y <= 0,
                DragArea.TopRight => delta.x <= 0 && delta.y <= 0,
                DragArea.Left => delta.x >= 0,
                DragArea.Right => delta.x <= 0,
                DragArea.Top => delta.y <= 0,
                _ => delta.y >= 0
            };

            m_FrontPosition = m_BeginFrontPosition + (valid ? delta : Vector3.zero);
            m_GeometryDirty = true;
        }

        public void OnEndDrag(PointerEventData data) {
            if (m_State != State.Dragging || m_PointerId != data.pointerId) {
                return;
            }

            OnDrag(data);
            m_PointerId = null;
            float total = Vector3.Distance(m_BeginFrontPosition, GetFlipTargetPosition());
            float progress = total > 0.0001f ? Vector3.Distance(m_BeginFrontPosition, m_FrontPosition) / total : 0;

            // 阈值为零时，合法热点上的轻触松手也算一次翻牌。
            if (progress >= m_FlipThreshold) {
                BeginFlip();
            } else {
                StartMotion(State.Returning, m_BeginFrontPosition, m_RecoveryTime, m_RecoveryEase);
            }
        }

        public void OnPointerUp(PointerEventData data) => OnEndDrag(data);
    }
}
