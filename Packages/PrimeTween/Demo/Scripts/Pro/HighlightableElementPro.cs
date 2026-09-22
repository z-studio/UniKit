using PrimeTween;
using UnityEngine;

namespace PrimeTweenDemo {
    public class HighlightableElementPro : MonoBehaviour {
        [SerializeField]
        public TweenAnimationComponent clickAnimation;

        [SerializeField]
        public TweenAnimation highlightAnimation = new TweenAnimation();
    }
}
