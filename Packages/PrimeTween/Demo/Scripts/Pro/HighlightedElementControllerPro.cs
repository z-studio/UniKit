using JetBrains.Annotations;
using PrimeTween;
using UnityEngine;

namespace PrimeTweenDemo {
    public class HighlightedElementControllerPro : MonoBehaviour {
        [SerializeField]
        Camera mainCamera;

        [SerializeField]
        TweenAnimationComponent animateAllPartsAnimation;

        HighlightableElementPro current;

        void Awake() {
#if !PHYSICS_MODULE_INSTALLED
            Debug.LogError("Please install the package needed for Physics.Raycast(): 'Package Manager/Packages/Built-in/Physics' (com.unity.modules.physics).");
#endif
        }

        void Update() {
            if (Application.isMobilePlatform && InputController.touchSupported && !InputController.Get()) {
                SetCurrentHighlighted(null);
                return;
            }

            var screenPosition = InputController.screenPosition;

            if (!new Rect(0f, 0f, Screen.width, Screen.height).Contains(screenPosition)) {
                return;
            }

            var ray = mainCamera.ScreenPointToRay(screenPosition);
            var highlightableElement = RaycastHighlightableElement(ray);
            SetCurrentHighlighted(highlightableElement);

            if (current != null && InputController.GetDown() && !animateAllPartsAnimation.animation.isAlive) {
                current.clickAnimation.animation.Trigger();
            }
        }

        [CanBeNull]
        static HighlightableElementPro RaycastHighlightableElement(Ray ray) {
#if PHYSICS_MODULE_INSTALLED

            // If you're seeing a compilation error on the next line, please install the package needed for Physics.Raycast(): 'Package Manager/Packages/Built-in/Physics' (com.unity.modules.physics).
            return Physics.Raycast(ray, out var hit) ? hit.collider.GetComponentInParent<HighlightableElementPro>()
                : null;
#else
                return null;
#endif
        }

        void SetCurrentHighlighted([CanBeNull] HighlightableElementPro newHighlighted) {
            if (newHighlighted != current) {
                if (current != null) {
                    current.highlightAnimation.state = false;
                }

                current = newHighlighted;

                if (newHighlighted != null) {
                    newHighlighted.highlightAnimation.state = true;
                }
            }
        }

        public void SetFogColor(Color color) => RenderSettings.fogColor = color;
    }
}
