using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel {
    /// <summary>可选的指针视觉与音效反馈。转盘只触发拨动，不覆盖 Inspector 中的幅度和时长。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class PointerController : MonoBehaviour {
        [SerializeField]
        private AudioClip m_PointerHit;

        [Min(0), SerializeField]
        private float m_FlickAngle = 20f;

        [Min(0.001f), SerializeField]
        private float m_FlickTime = 0.08f;

        [Min(0.001f), SerializeField]
        private float m_ReturnTime = 0.12f;

        [SerializeField]
        private bool m_IsClockwise = true;

        [Tooltip("关闭后使用上方固定方向；开启后根据转盘实际运动方向拨动。")]
        [SerializeField]
        private bool m_FollowWheelDirection = true;

        private AudioSource m_AudioSource;
        private Quaternion m_RestRotation;
        private Quaternion m_FromRotation;
        private Quaternion m_PeakRotation;
        private float m_Elapsed;
        private bool m_Flicking;
        private bool m_Initialized;
        private bool m_Unscaled;

        private void Awake() => EnsureInitialized();

        private void EnsureInitialized() {
            if (m_Initialized) {
                return;
            }

            m_AudioSource = GetComponent<AudioSource>();
            m_RestRotation = transform.localRotation;
            m_Initialized = true;
        }

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

        public void Flick() => PlayFlick(m_IsClockwise, false);

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