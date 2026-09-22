using System;
using JetBrains.Annotations;
using UnityEngine;
using TweenType = PrimeTween.TweenAnimation.TweenType;

namespace PrimeTween {
    public partial struct Tween {
        /// <summary>Returns the number of alive tweens.</summary>
        /// <param name="onTarget">If specified, returns the number of running tweens on the target. Please note: if the target is specified, this method call has O(n) complexity where n is the total number of running tweens.</param>
        public static int GetTweensCount([CanBeNull] object onTarget = null) {
            var manager = PrimeTweenManager.Instance;

            if (onTarget == null && manager.updateDepth == 0) {
                int result = manager.TweensCount;
#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
                Assert.AreEqual(result, PrimeTweenManager.ProcessAll(null, _ => true, true));
#endif
                return result;
            }

            return PrimeTweenManager.ProcessAll(onTarget, _ => true, true); // call ProcessAll to filter null tweens
        }

#if PRIME_TWEEN_EXPERIMENTAL
        public static int GetTweensCapacity() {
            var instance = PrimeTweenConfig.Instance;

            if (instance == null) {
                return PrimeTweenManager.sCustomInitialCapacity;
            }

            return instance.CurrentPoolCapacity;
        }

        public static Tween Custom(
            Double startValue,
            Double endValue,
            float duration,
            [NotNull] Action<Double> onValueChange,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            Custom(
                new TweenSettings<Double>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                ),
                onValueChange
            );

        public static Tween Custom(TweenSettings<Double> settings, [NotNull] Action<Double> onValueChange) {
            Assert.IsNotNull(onValueChange);

            if (settings.startFromCurrent) {
                Debug.LogWarning(Constants.kCustomTweensDontSupportStartFromCurrentWarning);
            }

            if (PrimeTweenManager.Instance.IsDestroyed) {
                return default;
            }

            var tween = PrimeTweenManager.FetchTween(settings.settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            d.startValue.CopyFrom(ref settings.startValue);
            rt.endValueOrDiff.CopyFrom(ref settings.endValue);
            tween.customOnValueChange = onValueChange;

            tween.Setup(
                PrimeTweenManager.sDummyTarget,
                ref settings.settings,
                false,
                TweenType.CustomDouble,
                ref rt,
                ref d
            );

            tween.onValueChange = (ref TweenData rt2, ref UnmanagedTweenData d2) => {
                var customOnValueChange = rt2.cold.customOnValueChange as Action<Double>;
                var val = TweenData.DoubleVal(d2.startValue, d2.easedInterpolationFactor, rt2.endValueOrDiff);
                customOnValueChange(val);
            };

            return PrimeTweenManager.Animate(ref rt, ref d);
        }

        public static Tween Custom<T>(
            [NotNull] T target,
            Double startValue,
            Double endValue,
            float duration,
            [NotNull] Action<T, Double> onValueChange,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) where T : class =>
            CustomInternal(
                target,
                new TweenSettings<Double>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                ),
                onValueChange
            );

        public static Tween Custom<T>(
            [NotNull] T target,
            TweenSettings<Double> settings,
            [NotNull] Action<T, Double> onValueChange
        ) where T : class =>
            CustomInternal(target, settings, onValueChange);

        public static Tween CustomAdditive<T>(
            [NotNull] T target,
            Double deltaValue,
            TweenSettings settings,
            [NotNull] Action<T, Double> onDeltaChange
        ) where T : class =>
            CustomInternal(target, new TweenSettings<Double>(default, deltaValue, settings), onDeltaChange, true);

        private static Tween CustomInternal<T>(
            [NotNull] T target,
            TweenSettings<Double> settings,
            [NotNull] Action<T, Double> onValueChange,
            bool isAdditive = false
        ) where T : class {
            Assert.IsNotNull(onValueChange);

            if (settings.startFromCurrent) {
                Debug.LogWarning(Constants.kCustomTweensDontSupportStartFromCurrentWarning);
            }

            if (PrimeTweenManager.Instance.IsDestroyed) {
                return default;
            }

            var tween = PrimeTweenManager.FetchTween(settings.settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            d.startValue.CopyFrom(ref settings.startValue);
            d.IsAdditive = isAdditive;

            rt.endValueOrDiff.CopyFrom(ref settings.endValue);
            tween.Setup(target, ref settings.settings, false, TweenType.CustomDouble, ref rt, ref d);

            tween.onValueChange = (ref TweenData rt2, ref UnmanagedTweenData d2) => {
                var startValue = d2.startValue;
                var t = d2.easedInterpolationFactor;
                var dataIsAdditive = d2.IsAdditive;

                var rt2Target = rt2.target as T;
                var diff = rt2.endValueOrDiff;

                double val;

                if (dataIsAdditive) {
                    var newVal = TweenData.DoubleVal(startValue, t, diff);
                    val = newVal.CalcDelta(rt2.cold.prevVal);
                    rt2.cold.prevVal.DoubleVal = newVal;
                } else {
                    val = TweenData.DoubleVal(startValue, t, diff);
                }

                var customOnValueChange = rt2.cold.customOnValueChange as Action<T, double>;
                customOnValueChange(rt2Target, val);
            };

            return PrimeTweenManager.Animate(ref rt, ref d);
        }
#endif

        /// <summary>Stops all tweens and sequences.<br/>
        /// If <see cref="onTarget"/> is provided, stops only tweens on this target (stopping a tween inside a Sequence is not allowed).</summary>
        /// <returns>The number of stopped tweens.</returns>
        public static int StopAll([CanBeNull] object onTarget = null) {
            var result = PrimeTweenManager.ProcessAll(
                onTarget,
                tween => {
                    ref var d = ref tween.Data;

                    if (d.IsInSequence) {
                        if (d.IsMainSequenceRoot()) {
                            new Sequence(tween.sequence).Stop();
                        }

                        // do nothing with nested tween or sequence. The main sequence root will process it
                    } else {
                        tween.ManagedData.Kill(ref d);
                    }

                    return true;
                },
                false
            );

            ForceUpdateManagerIfTargetIsNull(onTarget);
            return result;
        }

        /// <summary>Completes all tweens and sequences.<br/>
        /// If <see cref="onTarget"/> is provided, completes only tweens on this target (completing a tween inside a Sequence is not allowed).</summary>
        /// <returns>The number of completed tweens.</returns>
        public static int CompleteAll([CanBeNull] object onTarget = null) {
            var manager = PrimeTweenManager.Instance;

            if (manager.updateDepth != 0) {
                manager.completeAllRequested = true;
                manager.completeAllRequestedTarget = onTarget;
                return onTarget == null ? manager.TweensCount : GetTweensCount(onTarget);
            }

            manager.AddNewTweens(EUpdateType.Update);
            manager.AddNewTweens(EUpdateType.LateUpdate);
            manager.AddNewTweens(EUpdateType.FixedUpdate);

            var result = PrimeTweenManager.ProcessAll(
                onTarget,
                tween => {
                    ref var d = ref tween.Data;

                    if (d.IsInSequence) {
                        if (d.IsMainSequenceRoot()) {
                            new Sequence(tween.sequence).Complete();
                        }

                        // do nothing with nested tween or sequence. The main sequence root will process it
                    } else {
                        new Tween(tween).Complete();
                    }

                    return true;
                },
                false
            );

            ForceUpdateManagerIfTargetIsNull(onTarget);
            return result;
        }

        private static void ForceUpdateManagerIfTargetIsNull([CanBeNull] object onTarget) {
            if (onTarget == null) {
                var manager = PrimeTweenManager.Instance;

                if (manager != null) {
                    if (manager.updateDepth == 0) {
                        manager.UpdateTweens(EUpdateType.Update, 0f, 0f);
                        manager.UpdateTweens(EUpdateType.LateUpdate, 0f, 0f);
                        manager.UpdateTweens(EUpdateType.FixedUpdate, 0f, 0f);

                        Assert.AreEqual(0, manager.tweensUpdate.Count);

                        // Assert.AreEqual(0, manager.tweensLateUpdate.Count); // LateUpdate adds new tweens after update
                        Assert.AreEqual(0, manager.tweensFixedUpdate.Count);
                    }
                }
            }
        }

        /// <summary>Pauses/unpauses all tweens and sequences.<br/>
        /// If <see cref="onTarget"/> is provided, pauses/unpauses only tweens on this target (pausing/unpausing a tween inside a Sequence is not allowed).</summary>
        /// <returns>The number of paused/unpaused tweens.</returns>
        public static int SetPausedAll(bool isPaused, [CanBeNull] object onTarget = null) {
            if (isPaused) {
                return PrimeTweenManager.ProcessAll(onTarget, tween => { return tween.Data.TrySetPause(true); }, false);
            }

            return PrimeTweenManager.ProcessAll(onTarget, tween => { return tween.Data.TrySetPause(false); }, false);
        }

        /// <summary>Please note: delay may outlive the caller (the calling UnityEngine.Object may already be destroyed).
        /// When using this overload, it's user's responsibility to ensure that <see cref="onComplete"/> is safe to execute once the delay is finished.
        /// It's preferable to use the <see cref="Delay{T}"/> overload because it checks if the UnityEngine.Object target is still alive before calling the <see cref="onComplete"/>.</summary>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public static Tween Delay(
            float duration,
            [CanBeNull] Action onComplete = null,
            bool useUnscaledTime = false,
            bool? warnIfTargetDestroyed = null
        ) {
            return DelayInternal(
                PrimeTweenManager.sDummyTarget,
                duration,
                onComplete,
                useUnscaledTime,
                warnIfTargetDestroyed
            );
        }

        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public static Tween Delay(
            [NotNull] object target,
            float duration,
            [CanBeNull] Action onComplete = null,
            bool useUnscaledTime = false,
            bool? warnIfTargetDestroyed = null
        ) {
            return DelayInternal(target, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        private static Tween DelayInternal(
            [CanBeNull] object target,
            float duration,
            [CanBeNull] Action onComplete,
            bool useUnscaledTime,
            bool? warnIfTargetDestroyed
        ) {
            var result = DelayInternal(target, duration, useUnscaledTime);

            if (onComplete != null) {
                result?.tween.ManagedData.OnComplete(onComplete, warnIfTargetDestroyed);
            }

            return result ?? default;
        }

        /// <summary> This is the most preferable overload of all Delay functions:<br/>
        /// - It checks if UnityEngine.Object target is still alive before calling the <see cref="onComplete"/> callback.<br/>
        /// - It allows calling any method on <see cref="target"/> without producing garbage.</summary>
        /// <example>
        /// <code>
        /// Tween.Delay(this, duration: 1f, onComplete: _this =&gt; {
        ///     // Please note: we're using '_this' variable from the onComplete callback. Calling DoSomething() directly will implicitly capture 'this' variable (creating a closure) and generate garbage.
        ///     _this.DoSomething();
        /// });
        /// </code>
        /// </example>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public static Tween Delay<T>(
            [NotNull] T target,
            float duration,
            [NotNull] Action<T> onComplete,
            bool useUnscaledTime = false,
            bool? warnIfTargetDestroyed = null
        ) where T : class {
            var maybeDelay = DelayInternal(target, duration, useUnscaledTime);

            if (!maybeDelay.HasValue) {
                return default;
            }

            var delay = maybeDelay.Value;
            delay.tween.ManagedData.OnComplete(target, onComplete, warnIfTargetDestroyed);
            return delay;
        }

        private static Tween? DelayInternal([CanBeNull] object target, float duration, bool useUnscaledTime) {
            PrimeTweenManager.CheckDuration(target, duration);
            return PrimeTweenManager.DelayWithoutDurationCheck(target, duration, useUnscaledTime);
        }

        public static Tween MaterialColor(
            [NotNull] Material target,
            int propertyId,
            Color endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialColor(
                target,
                propertyId,
                new TweenSettings<Color>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialColor(
            [NotNull] Material target,
            int propertyId,
            Color startValue,
            Color endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialColor(
                target,
                propertyId,
                new TweenSettings<Color>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialColor([NotNull] Material target, int propertyId, TweenSettings<Color> settings) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialColorProperty);

        public static Tween MaterialProperty(
            [NotNull] Material target,
            int propertyId,
            float endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialProperty(
                target,
                propertyId,
                new TweenSettings<float>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialProperty(
            [NotNull] Material target,
            int propertyId,
            float startValue,
            float endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialProperty(
                target,
                propertyId,
                new TweenSettings<float>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween
            MaterialProperty([NotNull] Material target, int propertyId, TweenSettings<float> settings) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialProperty);

        public static Tween MaterialAlpha(
            [NotNull] Material target,
            int propertyId,
            float endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialAlpha(
                target,
                propertyId,
                new TweenSettings<float>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialAlpha(
            [NotNull] Material target,
            int propertyId,
            float startValue,
            float endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialAlpha(
                target,
                propertyId,
                new TweenSettings<float>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialAlpha([NotNull] Material target, int propertyId, TweenSettings<float> settings) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialAlphaProperty);

        public static Tween MaterialTextureOffset(
            [NotNull] Material target,
            int propertyId,
            Vector2 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialTextureOffset(
                target,
                propertyId,
                new TweenSettings<Vector2>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialTextureOffset(
            [NotNull] Material target,
            int propertyId,
            Vector2 startValue,
            Vector2 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialTextureOffset(
                target,
                propertyId,
                new TweenSettings<Vector2>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialTextureOffset(
            [NotNull] Material target,
            int propertyId,
            TweenSettings<Vector2> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialTextureOffset);

        public static Tween MaterialTextureScale(
            [NotNull] Material target,
            int propertyId,
            Vector2 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialTextureScale(
                target,
                propertyId,
                new TweenSettings<Vector2>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialTextureScale(
            [NotNull] Material target,
            int propertyId,
            Vector2 startValue,
            Vector2 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialTextureScale(
                target,
                propertyId,
                new TweenSettings<Vector2>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialTextureScale(
            [NotNull] Material target,
            int propertyId,
            TweenSettings<Vector2> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialTextureScale);

        public static Tween MaterialProperty(
            [NotNull] Material target,
            int propertyId,
            Vector4 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialProperty(
                target,
                propertyId,
                new TweenSettings<Vector4>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween MaterialProperty(
            [NotNull] Material target,
            int propertyId,
            Vector4 startValue,
            Vector4 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            MaterialProperty(
                target,
                propertyId,
                new TweenSettings<Vector4>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween
            MaterialProperty([NotNull] Material target, int propertyId, TweenSettings<Vector4> settings) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialPropertyVector4);

        // p1 todo also support material index
        // p1 todo also add MaterialPropertyBlockColor overload without the int propertyId. Should either search the color property with the [MainColor] ShaderLab attribute. Or use _Color in BiRP and _BaseColor in URP
        /// <summary>
        /// Animates a <see cref="MaterialPropertyBlock"/> property and applies it to the <paramref name="target"/> by calling <see cref="Renderer.SetPropertyBlock(MaterialPropertyBlock)"/>.<br/>
        /// Use this to animate sharedMaterial properties without creating new material instances.<br/><br/>
        ///
        /// NOTE: PrimeTween doesn't reset the MaterialPropertyBlock after animation completion to preserve the animated property at its endValue.<br/>
        /// As a result, directly modifying the <see cref="Material"/> property will have no effect since the MaterialPropertyBlock takes precedence.<br/>
        /// To modify the sharedMaterial property after animation completion, call 'renderer.SetPropertyBlock(null)' to clear the override.
        /// </summary>
        public static Tween MaterialPropertyBlockColor(
            [NotNull] Renderer target,
            int propertyId,
            TweenSettings<Color> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialPropertyBlockColorProperty);

        /// <inheritdoc cref="MaterialPropertyBlockColor"/>
        public static Tween MaterialPropertyBlockAlpha(
            [NotNull] Renderer target,
            int propertyId,
            TweenSettings<float> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialPropertyBlockAlphaProperty);

        /// <inheritdoc cref="MaterialPropertyBlockColor"/>
        public static Tween MaterialPropertyBlockProperty(
            [NotNull] Renderer target,
            int propertyId,
            TweenSettings<float> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialPropertyBlockProperty);

        /// <inheritdoc cref="MaterialPropertyBlockColor"/>
        public static Tween MaterialPropertyBlockProperty(
            [NotNull] Renderer target,
            int propertyId,
            TweenSettings<Vector4> settings
        ) =>
            AnimateMaterial(target, propertyId, ref settings, TweenType.MaterialPropertyBlockPropertyVector4);

        /// <inheritdoc cref="MaterialPropertyBlockColor"/>
        public static Tween MaterialPropertyBlockTextureScale(
            [NotNull] Renderer target,
            string textureName,
            TweenSettings<Vector2> settings
        ) =>
            AnimateMaterial(
                target,
                PackPropertyIdsToLowHigh(textureName),
                ref settings,
                TweenType.MaterialPropertyBlockTextureScale
            );

        /// <inheritdoc cref="MaterialPropertyBlockColor"/>
        public static Tween MaterialPropertyBlockTextureOffset(
            [NotNull] Renderer target,
            string textureName,
            TweenSettings<Vector2> settings
        ) =>
            AnimateMaterial(
                target,
                PackPropertyIdsToLowHigh(textureName),
                ref settings,
                TweenType.MaterialPropertyBlockTextureOffset
            );

        private static readonly System.Collections.Generic.Dictionary<int, int> s_TextureIdToST = new(10);

        static long PackPropertyIdsToLowHigh(string textureName) {
            int textureIdLow = Shader.PropertyToID(textureName);

            if (!s_TextureIdToST.TryGetValue(textureIdLow, out int stHigh)) {
                stHigh = Shader.PropertyToID(textureName + "_ST");
                s_TextureIdToST.Add(textureIdLow, stHigh);
            }

            return ((long)stHigh << 32) | (uint)textureIdLow;
        }

        // No 'startFromCurrent' overload because euler angles animation should always have the startValue to prevent ambiguous results
        public static Tween EulerAngles(
            [NotNull] Transform target,
            Vector3 startValue,
            Vector3 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            EulerAngles(
                target,
                new TweenSettings<Vector3>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween EulerAngles([NotNull] Transform target, TweenSettings<Vector3> settings) {
            ValidateEulerAnglesData(ref settings);
            return Animate(target, ref settings, TweenType.EulerAngles);
        }

        public static Tween LocalEulerAngles(
            [NotNull] Transform target,
            Vector3 startValue,
            Vector3 endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            LocalEulerAngles(
                target,
                new TweenSettings<Vector3>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime)
                )
            );

        public static Tween LocalEulerAngles([NotNull] Transform target, TweenSettings<Vector3> settings) {
            ValidateEulerAnglesData(ref settings);
            return Animate(target, ref settings, TweenType.LocalEulerAngles);
        }

        private static void ValidateEulerAnglesData(ref TweenSettings<Vector3> settings) {
            if (settings.startFromCurrent) {
                settings.startFromCurrent = false;

                Debug.LogWarning(
                    "Animating euler angles from the current value may produce unexpected results because there is more than one way to represent the current rotation using Euler angles.\n"
                    + "'"
                    + nameof(TweenSettings<float>.startFromCurrent)
                    + "' was ignored.\n"
                    + "More info: https://docs.unity3d.com/ScriptReference/Transform-eulerAngles.html\n"
                );
            }
        }

        // Called from TweenGenerated.cs
        public static Tween Scale([NotNull] Transform target, TweenSettings<float> uniformScaleSettings) {
            var remapped = new TweenSettings<Vector3>(
                uniformScaleSettings.startValue * Vector3.one,
                uniformScaleSettings.endValue * Vector3.one,
                uniformScaleSettings.settings
            ) {
                startFromCurrent = uniformScaleSettings.startFromCurrent
            };

            return Scale(target, remapped);
        }

        public static Tween Rotation([NotNull] Transform target, TweenSettings<Vector3> eulerAnglesSettings) =>
            Rotation(target, ToQuaternion(eulerAnglesSettings));

        public static Tween
            LocalRotation([NotNull] Transform target, TweenSettings<Vector3> localEulerAnglesSettings) =>
            LocalRotation(target, ToQuaternion(localEulerAnglesSettings));

        private static TweenSettings<Quaternion> ToQuaternion(TweenSettings<Vector3> s) =>
            new(Quaternion.Euler(s.startValue), Quaternion.Euler(s.endValue), s.settings)
                { startFromCurrent = s.startFromCurrent };

#if TEXT_MESH_PRO_INSTALLED
        public static Tween TextMaxVisibleCharacters([NotNull] TMPro.TMP_Text target, TweenSettings<int> settings) {
            int oldCount = target.textInfo.characterCount;
            target.ForceMeshUpdate();

            if (oldCount != target.textInfo.characterCount) {
                Debug.LogWarning("Please call TMP_Text.ForceMeshUpdate() before animating maxVisibleCharacters.");
            }

            var floatSettings = new TweenSettings<float>(settings.startValue, settings.endValue, settings.settings);
            return AnimateIntAsFloat(target, ref floatSettings, TweenType.TextMaxVisibleCharacters);
        }

        // p2 todo fix this correctly
        private static Tween AnimateIntAsFloat(object target, ref TweenSettings<float> settings, TweenType tweenType) {
            if (PrimeTweenManager.Instance.IsDestroyed) {
                return default;
            }

            var tween = PrimeTweenManager.FetchTween(settings.settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            d.startValue.CopyFrom(ref settings.startValue);
            rt.endValueOrDiff.CopyFrom(ref settings.endValue);
            tween.Setup(target, ref settings.settings, settings.startFromCurrent, tweenType, ref rt, ref d);
            return PrimeTweenManager.Animate(ref rt, ref d);
        }

        public static Tween TextMaxVisibleCharactersNormalized(
            [NotNull] TMPro.TMP_Text target,
            TweenSettings<float> settings
        ) =>
            Animate(target, ref settings, TweenType.TextMaxVisibleCharactersNormalized);
#endif

        // not generated automatically because GlobalTimeScale() should have 'useUnscaledTime: true'
        public static Tween GlobalTimeScale(
            Single endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0
        ) =>
            GlobalTimeScale(
                new TweenSettings<float>(
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, true)
                )
            );

        public static Tween GlobalTimeScale(
            Single startValue,
            Single endValue,
            float duration,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0
        ) =>
            GlobalTimeScale(
                new TweenSettings<float>(
                    startValue,
                    endValue,
                    new TweenSettings(duration, ease, cycles, cycleMode, startDelay, endDelay, true)
                )
            );

        public static Tween GlobalTimeScale(TweenSettings<float> settings) {
            clampTimescale(ref settings.startValue);
            clampTimescale(ref settings.endValue);

            if (!settings.settings.useUnscaledTime) {
                Debug.LogWarning(
                    "Setting " + nameof(TweenSettings.useUnscaledTime) + " to true to animate Time.timeScale correctly."
                );

                settings.settings.useUnscaledTime = true;
            }

            return Animate(PrimeTweenManager.sDummyTarget, ref settings, TweenType.GlobalTimeScale);

            void clampTimescale(ref float value) {
                if (value < 0) {
                    Debug.LogError($"timeScale should be >= 0, but was {value}");
                    value = 0;
                }
            }
        }

        public static Tween TweenTimeScale(Tween tween, TweenSettings<float> settings) =>
            AnimateTimeScale(tween, settings, TweenType.TweenTimeScale);

        private static Tween AnimateTimeScale(Tween tween, TweenSettings<float> settings, TweenType tweenType) {
            if (!tween.TryManipulate()) {
                return default;
            }

            var result = Animate(tween.tween, ref settings, tweenType);
            Assert.IsTrue(result.isAlive);
            result.tween.longParam = tween.id;
            return result;
        }

        public static Tween TweenTimeScale(Sequence sequence, TweenSettings<float> settings) =>
            AnimateTimeScale(sequence.root, settings, TweenType.TweenTimeScaleSequence);

        public static Tween RotationAtSpeed(
            [NotNull] Transform target,
            Vector3 endValue,
            float averageAngularSpeed,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            RotationAtSpeed(
                target,
                new TweenSettings<Vector3>(
                    endValue,
                    new TweenSettings(
                        averageAngularSpeed,
                        ease,
                        cycles,
                        cycleMode,
                        startDelay,
                        endDelay,
                        useUnscaledTime
                    )
                )
            );

        public static Tween RotationAtSpeed(
            [NotNull] Transform target,
            Vector3 startValue,
            Vector3 endValue,
            float averageAngularSpeed,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            RotationAtSpeed(
                target,
                new TweenSettings<Vector3>(
                    startValue,
                    endValue,
                    new TweenSettings(
                        averageAngularSpeed,
                        ease,
                        cycles,
                        cycleMode,
                        startDelay,
                        endDelay,
                        useUnscaledTime
                    )
                )
            );

        private static Tween RotationAtSpeed([NotNull] Transform target, TweenSettings<Vector3> settingsVector3) {
            var settings = ToQuaternion(settingsVector3);
            var speed = settings.settings.duration;

            if (speed <= 0) {
                Debug.LogError($"Invalid speed provided to the Tween.{nameof(RotationAtSpeed)}() method: {speed}.");
                return default;
            }

            if (settings.startFromCurrent) {
                settings.startFromCurrent = false;
                settings.startValue = target.rotation;
            }

            settings.settings.duration = Extensions.CalcDistance(settings.startValue, settings.endValue) / speed;
            return Rotation(target, settings);
        }

        public static Tween LocalRotationAtSpeed(
            [NotNull] Transform target,
            Vector3 endValue,
            float averageAngularSpeed,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            LocalRotationAtSpeed(
                target,
                new TweenSettings<Vector3>(
                    endValue,
                    new TweenSettings(
                        averageAngularSpeed,
                        ease,
                        cycles,
                        cycleMode,
                        startDelay,
                        endDelay,
                        useUnscaledTime
                    )
                )
            );

        public static Tween LocalRotationAtSpeed(
            [NotNull] Transform target,
            Vector3 startValue,
            Vector3 endValue,
            float averageAngularSpeed,
            Easing ease = default,
            int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0,
            float endDelay = 0,
            bool useUnscaledTime = false
        ) =>
            LocalRotationAtSpeed(
                target,
                new TweenSettings<Vector3>(
                    startValue,
                    endValue,
                    new TweenSettings(
                        averageAngularSpeed,
                        ease,
                        cycles,
                        cycleMode,
                        startDelay,
                        endDelay,
                        useUnscaledTime
                    )
                )
            );

        private static Tween LocalRotationAtSpeed([NotNull] Transform target, TweenSettings<Vector3> settingsVector3) {
            var settings = ToQuaternion(settingsVector3);
            var speed = settings.settings.duration;

            if (speed <= 0) {
                Debug.LogError(
                    $"Invalid speed provided to the Tween.{nameof(LocalRotationAtSpeed)}() method: {speed}."
                );

                return default;
            }

            if (settings.startFromCurrent) {
                settings.startFromCurrent = false;
                settings.startValue = target.localRotation;
            }

            settings.settings.duration = Extensions.CalcDistance(settings.startValue, settings.endValue) / speed;
            return LocalRotation(target, settings);
        }

#if PHYSICS_MODULE_INSTALLED
        public static Tween RigidbodyMoveRotation(Rigidbody target, TweenSettings<Vector3> eulerAnglesSettings) =>
            RigidbodyMoveRotation(target, ToQuaternion(eulerAnglesSettings));
#endif
    }
}