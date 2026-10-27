using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel {
    /// <summary>
    /// 指针的可选表现组件：收到 Flick 后拨开，再回到初始姿态，同时播放一次拨动音效。
    /// 不决定中奖结果，也不负责让圆盘停准；WheelController 在跨过奖格边界时调用它。
    /// 挂在固定指针上，轴心放在摆动支点，指针不能是旋转圆盘的子物体。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class PointerController : MonoBehaviour {
        [Tooltip("每次触发拨动时播放一次。留空仍可摆动；组件会自动添加 AudioSource。")]
        [SerializeField]
        private AudioClip m_PointerHit;

        [Tooltip("相对指针原位拨开多少度。越大摆动越明显；0 表示只播放音效。")]
        [Min(0), SerializeField]
        private float m_FlickAngle = 20f;

        [Tooltip("从当前位置拨到最大角度所用的时间，越短越利落。")]
        [Min(0.001f), SerializeField]
        private float m_FlickTime = 0.08f;

        [Tooltip("从最大角度回到原位所用的时间。原位是组件初始化时记住的姿态。")]
        [Min(0.001f), SerializeField]
        private float m_ReturnTime = 0.12f;

        [Tooltip("关闭自动选择拨动方向时，使用此固定方向。单独调用无参数 Flick() 也使用此方向。")]
        [SerializeField]
        private bool m_IsClockwise = true;

        [Tooltip("推荐开启：指针向圆盘实际运动的反方向让开，包含起转前回拉和停转后回摆。关闭后可指定固定方向。")]
        [SerializeField]
        private bool m_FollowWheelDirection = true;

        // 原位只记一次；连续跨格时从当前姿态重新拨开，避免每次触发先跳回原位。
        private AudioSource m_AudioSource;
        private Quaternion m_RestRotation;
        private Quaternion m_FromRotation;
        private Quaternion m_PeakRotation;
        private float m_Elapsed;
        private bool m_Flicking;
        private bool m_Initialized;
        private bool m_Unscaled;

        private void Awake() => EnsureInitialized();

        // Awake 与外部调用共用初始化入口，不要求指针和转盘按特定顺序执行 Awake。
        private void EnsureInitialized() {
            if (m_Initialized) {
                return;
            }

            m_AudioSource = GetComponent<AudioSource>();
            m_RestRotation = transform.localRotation;
            m_Initialized = true;
        }

        /// <summary>
        /// 可选的代码配置入口；Inspector 配置完成后无需调用。
        /// isClockwise 设置固定拨动方向；转盘自动方向模式下仍由实际运动方向决定拨动方向。
        /// </summary>
        public void SetFlickParams(float angle, float flickTime, float returnTime, bool isClockwise) {
            if (float.IsNaN(angle) || float.IsInfinity(angle) || float.IsNaN(flickTime) || float.IsInfinity(flickTime)
                || float.IsNaN(returnTime)
                || float.IsInfinity(returnTime)) {
                throw new System.ArgumentOutOfRangeException();
            }

            m_FlickAngle = Mathf.Abs(angle);
            m_FlickTime = Mathf.Max(0.001f, flickTime);
            m_ReturnTime = Mathf.Max(0.001f, returnTime);
            m_IsClockwise = isClockwise;
        }

        /// <summary>独立触发一次拨动：使用固定方向和游戏时间，不读取转盘状态。</summary>
        public void Flick() => PlayFlick(m_IsClockwise, false);

        /// <summary>转盘调用入口。传入的是圆盘方向；自动模式会换算为相反的指针拨动方向。</summary>
        public void Flick(bool wheelClockwise, bool useUnscaledTime) =>
            PlayFlick(m_FollowWheelDirection ? !wheelClockwise : m_IsClockwise, useUnscaledTime);

        private void PlayFlick(bool clockwise, bool useUnscaledTime) {
            if (!isActiveAndEnabled) {
                return;
            }

            EnsureInitialized();
            m_Unscaled = useUnscaledTime;
            
            // 指针向轮盘运动反方向让开；重入从当前姿态开始，最终始终回到固定静止姿态。
            m_FromRotation = transform.localRotation;
            m_PeakRotation = m_RestRotation * Quaternion.Euler(0, 0, (clockwise ? -1 : 1) * m_FlickAngle);
            m_Elapsed = 0;
            m_Flicking = true;

            if (m_PointerHit) {
                m_AudioSource.PlayOneShot(m_PointerHit);
            }
        }

        private void Update() {
            if (!m_Flicking) {
                return;
            }

            m_Elapsed += m_Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            var flickTime = Mathf.Max(0.001f, m_FlickTime);
            var returnTime = Mathf.Max(0.001f, m_ReturnTime);

            // 前段拨开、后段回位；二次缓出让每段接近目标时放缓。大帧可直接跨入回位段。
            if (m_Elapsed < flickTime) {
                var t = m_Elapsed / flickTime;
                transform.localRotation = Quaternion.Slerp(m_FromRotation, m_PeakRotation, 1 - (1 - t) * (1 - t));
            } else {
                var t = Mathf.Clamp01((m_Elapsed - flickTime) / returnTime);
                transform.localRotation = Quaternion.Slerp(m_PeakRotation, m_RestRotation, 1 - (1 - t) * (1 - t));

                if (t >= 1) {
                    m_Flicking = false;
                }
            }
        }

        /// <summary>立即回到初始化时的原位并停止拨动音效；转盘完成、取消或禁用时调用。</summary>
        public void CancelFlick() {
            if (!m_Initialized) {
                return;
            }

            m_Flicking = false;
            transform.localRotation = m_RestRotation;

            if (m_AudioSource) {
                m_AudioSource.Stop();
            }
        }

        private void OnDisable() => CancelFlick();
        private void OnDestroy() => CancelFlick();
    }
}