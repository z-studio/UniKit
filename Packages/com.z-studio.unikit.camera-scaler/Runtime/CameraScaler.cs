using UnityEngine;
using UnityEngine.Rendering;

namespace ZStudio.UniKit {
    /// <summary>
    /// 根据参考分辨率和当前相机宽高比，自动调整正交相机的 Size 或透视相机的垂直 FOV。
    /// </summary>
    /// <remarks>
    /// 设计基准是组件上的参考 Size/FOV，而不是 Camera 在 Awake 时的瞬时值。
    /// 编辑和运行模式下启用即适配；禁用或移除组件时自动恢复接管前的相机参数。
    /// 运行期间应通过 <see cref="CameraZoom"/> 缩放，不要直接修改 Camera 的 Size/FOV。
    /// 所有公开 API 都必须在 Unity 主线程调用。
    /// </remarks>
    [AddComponentMenu("Layout/ZStudio/Camera Scaler")]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CameraScaler : MonoBehaviour {
        // 标记设计参考值是否已采集，防止每次加载都用相机当前结果覆盖配置。
        private const int k_CurrentSerializedVersion = 1;

        [Tooltip("设计内容时使用的参考分辨率，宽和高必须大于 0。")] [SerializeField]
        private Vector2 m_ReferenceResolution =
            new(CameraScalerMath.DefaultReferenceWidth, CameraScalerMath.DefaultReferenceHeight);

        [Tooltip("屏幕宽高比变化时采用的相机适配策略。")] [SerializeField]
        private ScaleMode m_ScaleMode = ScaleMode.ConstantWidth;

        [Tooltip("宽度匹配和高度匹配之间的插值权重：0 为宽度，1 为高度。")] [Range(0f, 1f)] [SerializeField]
        private float m_MatchWidthOrHeight = 0.5f;

        [Tooltip("参考分辨率下的正交相机垂直半尺寸。运行时以此为基准，而不是 Camera 上的当前 Size。")] [SerializeField]
        private float m_ReferenceOrthographicSize = CameraScalerMath.DefaultOrthographicSize;

        [Tooltip("参考分辨率下的透视相机垂直视野角。运行时以此为基准，而不是 Camera 上的当前 FOV。")] [Range(1f, 179f)] [SerializeField]
        private float m_ReferenceFieldOfView = CameraScalerMath.DefaultFieldOfView;

        [Tooltip("相对于基准投影视野的缩放倍率。1 表示原始视野，大于 1 表示放大。")] [Min(0.0001f)] [SerializeField]
        private float m_CameraZoom = 1f;

        [Tooltip("将适配结果写入 Camera 的时机。与 Cinemachine 等系统冲突时，可改为 Late Update 或 On Pre Cull。")] [SerializeField]
        private ApplyTiming m_ApplyTiming = ApplyTiming.Update;

        // 原始参数只用于恢复，与用户可调整的参考 Size/FOV 分开保存。
        // 快照随组件序列化，避免场景/Prefab 重载后把适配结果再次作为原始值。
        [SerializeField, HideInInspector] private bool m_HasOriginalProjection;

        [SerializeField, HideInInspector] private float m_OriginalSize;

        [SerializeField, HideInInspector] private float m_OriginalFov;

        [SerializeField, HideInInspector] private int m_SerializedVersion;

        private Camera m_Camera;
        private float m_TargetAspect;
        private float m_HorizontalFov;
        private bool m_IsInitialized;
        private bool m_HasApplied;
        
        // 缓存的是当前投影类型的最终结果：正交为垂直半尺寸，透视为垂直视野角（度）。
        private float m_AppliedProjection;

        // 上次计算的输入；它们只决定是否重算，不代表 Camera 的实际值没有被外部修改。
        private float m_PreviousUpdateAspect;
        private ScaleMode m_PreviousUpdateMode;
        private float m_PreviousUpdateMatch;
        private Vector2 m_PreviousReferenceResolution;
        private float m_PreviousReferenceSize;
        private float m_PreviousReferenceFov;
        private float m_PreviousCameraZoom;
        private bool m_PreviousOrthographic;

        /// <summary>当前参考分辨率。非法分量会被修正为 1。</summary>
        public Vector2 ReferenceResolution {
            get => m_ReferenceResolution;
            set {
                Vector2 sanitized = CameraScalerMath.SanitizeReferenceResolution(value);

                if (CameraScalerMath.Approximately(m_ReferenceResolution, sanitized)) {
                    return;
                }

                m_ReferenceResolution = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>当前相机适配策略。</summary>
        public ScaleMode ScaleMode {
            get => m_ScaleMode;
            set {
                if (!CameraScalerMath.IsValidScaleMode(value)) {
                    Debug.LogError($"无效的 CameraScaler 工作模式：{value}。", this);
                    return;
                }

                if (m_ScaleMode == value) {
                    return;
                }

                m_ScaleMode = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>宽度与高度的匹配权重。赋值会被限制到 0～1。</summary>
        public float MatchWidthOrHeight {
            get => m_MatchWidthOrHeight;
            set {
                float sanitized = CameraScalerMath.SanitizeMatch(value);

                if (Mathf.Approximately(m_MatchWidthOrHeight, sanitized)) {
                    return;
                }

                m_MatchWidthOrHeight = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下的正交相机垂直半尺寸。非法值会被修正为 5。</summary>
        public float ReferenceOrthographicSize {
            get => m_ReferenceOrthographicSize;
            set {
                float sanitized = CameraScalerMath.SanitizeOrthographicSize(value);

                if (Mathf.Approximately(m_ReferenceOrthographicSize, sanitized)) {
                    return;
                }

                m_ReferenceOrthographicSize = sanitized;
                MarkBaselineSerialized();
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下的透视相机垂直视野角。非法值会被限制到 1～179。</summary>
        public float ReferenceFieldOfView {
            get => m_ReferenceFieldOfView;
            set {
                float sanitized = CameraScalerMath.SanitizeFieldOfView(value);

                if (Mathf.Approximately(m_ReferenceFieldOfView, sanitized)) {
                    return;
                }

                m_ReferenceFieldOfView = sanitized;
                MarkBaselineSerialized();
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下、未应用 <see cref="CameraZoom"/> 时的正交相机水平半尺寸。</summary>
        public float HorizontalSize {
            get {
                EnsureInitialized();
                UpdateReferenceData();
                return m_ReferenceOrthographicSize * m_TargetAspect;
            }
        }

        /// <summary>参考分辨率下、未应用 <see cref="CameraZoom"/> 时的水平视野角。</summary>
        public float HorizontalFov {
            get {
                EnsureInitialized();
                UpdateReferenceData();
                return m_HorizontalFov;
            }
        }

        /// <summary>
        /// 相对于初始投影视野的缩放倍率。1 表示原始视野，大于 1 表示放大。
        /// </summary>
        /// <remarks>
        /// 透视相机按投影平面比例缩放，而不是直接将角度相除。非法值会被拒绝并保留原值。
        /// </remarks>
        public float CameraZoom {
            get => m_CameraZoom;
            set {
                if (!CameraScalerMath.IsFinitePositive(value)) {
                    Debug.LogError($"CameraZoom 必须是大于 0 的有限值，当前输入：{value}。", this);
                    return;
                }

                if (Mathf.Approximately(m_CameraZoom, value)) {
                    return;
                }

                m_CameraZoom = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>运行模式下每帧应用结果的时机；首次启用和公开属性赋值仍会立即刷新。</summary>
        public ApplyTiming ApplyTiming {
            get => m_ApplyTiming;
            set {
                if (!CameraScalerMath.IsValidApplyTiming(value)) {
                    Debug.LogError($"无效的 CameraScaler 写入时机：{value}。", this);
                    return;
                }

                if (m_ApplyTiming == value) {
                    return;
                }

                m_ApplyTiming = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>缓存参考数据，首次适配改由 OnEnable 完成，避免与 OnEnable 重复写入。</summary>
        private void Awake() {
            EnsureInitialized();
        }

        /// <summary>启用后立即应用适配，编辑模式下也自动生效。</summary>
        private void OnEnable() {
            EnsureInitialized();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= UpdateEditorPreview;
            UnityEditor.EditorApplication.update += UpdateEditorPreview;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            UpdateEditorPreview();
#endif
            RefreshCamera(true);
        }

        /// <summary>仅在影响适配结果的输入发生变化时重新计算相机参数。</summary>
        private void Update() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == ApplyTiming.Update) {
                RefreshCamera(false);
            }
        }

        // LateUpdate 内仍受脚本执行顺序约束，不能仅靠回调名称保证最后写入。
        private void LateUpdate() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == ApplyTiming.LateUpdate) {
                RefreshCamera(false);
            }
        }

        /// <summary>Built-in 管线在剔除前应用结果，确保可见性判断使用适配后的投影。</summary>
        private void OnPreCull() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == ApplyTiming.OnPreCull) {
                RefreshCamera(false);
            }
        }

        /// <summary>SRP 对应的渲染前入口；事件覆盖所有相机，因此必须筛选当前组件的相机。</summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) {
            if (camera == m_Camera && Application.IsPlaying(gameObject) && m_ApplyTiming == ApplyTiming.OnPreCull) {
                RefreshCamera(false);
            }
        }

        /// <summary>退订全局回调并归还原始参数，禁用期间不再驱动相机。</summary>
        private void OnDisable() {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= UpdateEditorPreview;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            RestoreOriginalProjection();
        }

        // 恢复方法可重复调用，销毁时作为兜底，不会覆盖 OnDisable 之后的新状态。
        private void OnDestroy() {
            RestoreOriginalProjection();
        }

#if UNITY_EDITOR
        // 在模式切换边界先恢复再接管；关闭 Domain/Scene Reload 时也不能依赖重新 Awake。
        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state) {
            switch (state) {
                case UnityEditor.PlayModeStateChange.ExitingEditMode or UnityEditor.PlayModeStateChange.ExitingPlayMode:
                    RestoreOriginalProjection();
                    break;
                case UnityEditor.PlayModeStateChange.EnteredEditMode:
                    UpdateEditorPreview();
                    break;
                case UnityEditor.PlayModeStateChange.EnteredPlayMode:
                    RefreshCamera(true);
                    break;
            }
        }

        // 编辑模式的 MonoBehaviour.Update 不会稳定逐帧调用；通过编辑器更新跟踪 Game View 比例。
        // 此入口不受运行时 ApplyTiming 影响，也不要求 Inspector 保持选中。
        private void UpdateEditorPreview() {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            if (!isActiveAndEnabled) {
                return;
            }

            EnsureInitialized();
            float previousSize = m_Camera.orthographicSize;
            float previousFov = m_Camera.fieldOfView;
            RefreshCamera(false);

            // 仅在输出变化时请求刷新视图，避免编辑器空闲时不断触发额外重绘。
            if (!Mathf.Approximately(previousSize, m_Camera.orthographicSize)
                || !Mathf.Approximately(previousFov, m_Camera.fieldOfView)) {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        }
#endif

        /// <summary>每次接管只记录一次原值；后续适配不能覆盖恢复快照。</summary>
        private void CaptureOriginalProjectionIfNeeded() {
            if (m_HasOriginalProjection) {
                return;
            }

            m_OriginalSize = m_Camera.orthographicSize;
            m_OriginalFov = m_Camera.fieldOfView;
            m_HasOriginalProjection = true;
        }

        /// <summary>恢复接管前的两种投影参数，并允许下次启用时重新采集。</summary>
        private void RestoreOriginalProjection() {
            if (!m_HasOriginalProjection) {
                return;
            }

            if (m_Camera == null) {
                m_Camera = GetComponent<Camera>();
            }

            if (m_Camera != null) {
                m_Camera.orthographicSize = m_OriginalSize;
                m_Camera.fieldOfView = m_OriginalFov;
            }

            m_HasOriginalProjection = false;
            m_HasApplied = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        /// <summary>在 Inspector 修改数据时立即修正非法序列化值。</summary>
        private void OnValidate() {
            SanitizeSerializedFields();

            // OnValidate 可能在加载线程执行；相机读写留到后续主线程刷新。
            m_HasApplied = false;
        }

        /// <summary>编辑器添加或重置组件时，从未适配的相机参数建立设计基准。</summary>
        private void Reset() {
            m_Camera = GetComponent<Camera>();
            CopyBaselineFromCamera();
        }

        /// <summary>
        /// 立即按照相机的当前宽高比和投影类型重新应用适配，无需等待下一次更新回调。
        /// 脚本修改 Camera.aspect 或投影类型后，需要立即读取适配结果时可调用。
        /// 组件禁用或对象未激活时不会写入相机参数。
        /// 此方法不改变设计基准；调整设计视野请设置 <see cref="ReferenceOrthographicSize"/>
        /// 或 <see cref="ReferenceFieldOfView"/>。
        /// </summary>
        public void Refresh() {
            EnsureInitialized();
            RefreshCamera(true);
        }

        /// <summary>允许公开 API 早于 Awake 调用；这里只准备数据，不接管或改写相机。</summary>
        private void EnsureInitialized() {
            if (m_IsInitialized) {
                return;
            }

            SanitizeSerializedFields();
            m_Camera = GetComponent<Camera>();
            TryMigrateBaselineFromCamera();
            UpdateReferenceData();
            m_IsInitialized = true;
        }

        // 初始化前的属性赋值只保留配置，统一由 OnEnable 完成首次应用。
        private void RefreshIfInitialized() {
            if (m_IsInitialized) {
                RefreshCamera(true);
            }
        }

        /// <summary>检查写入条件，按需重算并应用结果。force 仅跳过计算缓存，不绕过启用检查。</summary>
        private void RefreshCamera(bool force) {
            if (!m_IsInitialized || m_Camera == null || !isActiveAndEnabled) {
                return;
            }

            // ExecuteAlways 也会作用于 Prefab 编辑对象；Play 期间不能把这类对象当作运行中的相机。
            if (!Application.IsPlaying(gameObject)) {
#if UNITY_EDITOR
                if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
                    return;
                }
#else
                return;
#endif
            }

            CaptureOriginalProjectionIfNeeded();
            SanitizeSerializedFields();

            bool baselineChanged =
                !CameraScalerMath.Approximately(m_PreviousReferenceResolution, m_ReferenceResolution)
                || !Mathf.Approximately(m_PreviousReferenceSize, m_ReferenceOrthographicSize)
                || !Mathf.Approximately(m_PreviousReferenceFov, m_ReferenceFieldOfView);

            if (baselineChanged || !m_HasApplied) {
                UpdateReferenceData();
            }

            // 使用相机自身比例，以兼容独立视口或 RenderTexture，而非假定整块屏幕就是视口。
            float currentAspect = CameraScalerMath.GetSafeAspect(m_Camera.aspect, m_TargetAspect);

            // 输入未变化也要应用缓存结果：其他控制器可能在上一帧或本帧改写 Size/FOV。
            if (!force
                && m_HasApplied
                && !baselineChanged
                && Mathf.Approximately(m_PreviousUpdateAspect, currentAspect)
                && m_PreviousUpdateMode == m_ScaleMode
                && Mathf.Approximately(m_PreviousUpdateMatch, m_MatchWidthOrHeight)
                && Mathf.Approximately(m_PreviousCameraZoom, m_CameraZoom)
                && m_PreviousOrthographic == m_Camera.orthographic) {
                ApplyProjection();
                return;
            }

            if (m_Camera.orthographic) {
                m_AppliedProjection = CameraScalerMath.CalculateOrthographicSize(m_ReferenceOrthographicSize,
                    m_TargetAspect,
                    currentAspect,
                    m_ScaleMode,
                    m_MatchWidthOrHeight,
                    m_CameraZoom);
            } else {
                m_AppliedProjection = CameraScalerMath.CalculateFieldOfView(m_ReferenceFieldOfView,
                    m_TargetAspect,
                    currentAspect,
                    m_ScaleMode,
                    m_MatchWidthOrHeight,
                    m_CameraZoom);
            }

            ApplyProjection();

            m_PreviousUpdateAspect = currentAspect;
            m_PreviousUpdateMode = m_ScaleMode;
            m_PreviousUpdateMatch = m_MatchWidthOrHeight;
            m_PreviousReferenceResolution = m_ReferenceResolution;
            m_PreviousReferenceSize = m_ReferenceOrthographicSize;
            m_PreviousReferenceFov = m_ReferenceFieldOfView;
            m_PreviousCameraZoom = m_CameraZoom;
            m_PreviousOrthographic = m_Camera.orthographic;
            m_HasApplied = true;
        }

        // 输入不变时复用计算结果，但仍恢复被其他相机控制器改写的投影参数。
        private void ApplyProjection() {
            if (m_Camera.orthographic) {
                if (!Mathf.Approximately(m_Camera.orthographicSize, m_AppliedProjection)) {
                    m_Camera.orthographicSize = m_AppliedProjection;
                }
            } else if (!Mathf.Approximately(m_Camera.fieldOfView, m_AppliedProjection)) {
                m_Camera.fieldOfView = m_AppliedProjection;
            }
        }

        /// <summary>计算参考分辨率下的派生数据，不包含当前视口比例和 Zoom。</summary>
        private void UpdateReferenceData() {
            m_TargetAspect = CameraScalerMath.CalculateAspect(m_ReferenceResolution);
            m_HorizontalFov = CameraScalerMath.CalcHorizontalFov(m_ReferenceFieldOfView, m_TargetAspect);
        }

        // Inspector、反序列化和 Undo 不会经过公开属性 setter，需单独校验底层字段。
        private void SanitizeSerializedFields() {
            m_ReferenceResolution = CameraScalerMath.SanitizeReferenceResolution(m_ReferenceResolution);
            m_MatchWidthOrHeight = CameraScalerMath.SanitizeMatch(m_MatchWidthOrHeight);
            m_ReferenceOrthographicSize = CameraScalerMath.SanitizeOrthographicSize(m_ReferenceOrthographicSize);
            m_ReferenceFieldOfView = CameraScalerMath.SanitizeFieldOfView(m_ReferenceFieldOfView);
            m_CameraZoom = CameraScalerMath.SanitizeZoom(m_CameraZoom);

            if (!CameraScalerMath.IsValidScaleMode(m_ScaleMode)) {
                m_ScaleMode = ScaleMode.ConstantWidth;
            }

            if (!CameraScalerMath.IsValidApplyTiming(m_ApplyTiming)) {
                m_ApplyTiming = ApplyTiming.Update;
            }
        }

        /// <summary>兼容尚未保存参考值的组件数据，采集一次后通过版本号避免重复迁移。</summary>
        private void TryMigrateBaselineFromCamera() {
            if (m_SerializedVersion >= k_CurrentSerializedVersion) {
                return;
            }

            if (m_Camera == null) {
                m_Camera = GetComponent<Camera>();
            }

            CopyBaselineFromCamera();
        }

        // 接管期间优先读取原始快照，否则重新采集会把适配和 Zoom 的输出再次当成基准。
        private void CopyBaselineFromCamera() {
            if (m_Camera == null) {
                return;
            }

            m_ReferenceOrthographicSize = 
                CameraScalerMath.SanitizeOrthographicSize(m_HasOriginalProjection 
                    ? m_OriginalSize
                    : m_Camera.orthographicSize);
            
            m_ReferenceFieldOfView =
                CameraScalerMath.SanitizeFieldOfView(m_HasOriginalProjection ? m_OriginalFov : m_Camera.fieldOfView);
            
            MarkBaselineSerialized();
        }

        private void MarkBaselineSerialized() {
            m_SerializedVersion = k_CurrentSerializedVersion;
        }
    }
}