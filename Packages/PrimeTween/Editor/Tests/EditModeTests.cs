#if TEST_FRAMEWORK_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PrimeTween;
using TMPro;
using UnityEditor;
using UnityEditor.TestTools.TestRunner;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class EditModeTests {
    [Test]
    public void TestEditMode() {
        Tween.StopAll();
        Assert.AreEqual(0, PrimeTweenManager.Instance.TweensCount);
        PrimeTweenConfig.warnEndValueEqualsCurrent = false;
        PrimeTweenConfig.warnZeroDuration = false;
        ExpectError();
        Tween.Custom(0, 1, 1, delegate { });
        var go = new GameObject();

        {
            ExpectError();
            Tween.Alpha(go.AddComponent<SpriteRenderer>(), 0, 1);
            ExpectError();
            Tween.Delay(1);
            ExpectError();
            Tween.Delay(0);
            ExpectError();
            Tween.CompleteAll();
            PrimeTweenConfig.warnEndValueEqualsCurrent = true;
            ExpectError();
            Tween.StopAll();
            ExpectError();
            Tween.SetPausedAll(true);
            ExpectError();
            Tween.ShakeLocalPosition(go.transform, Vector3.one, 1);
            ExpectError();
            Tween.ShakeCustom(go, Vector3.zero, new ShakeSettings(Vector3.one, 1), delegate { });
            ExpectError();
            Sequence.Create();
            ExpectError();
            Tween.GetTweensCount(this);
            ExpectError();
            Tween.GetTweensCount();
            ExpectError();
            Sequence.Create(Tween.Delay(0.01f));

            TweenSettings.ValidateCustomCurveKeyframes(AnimationCurve.Linear(0, 0, 1, 1));
            PrimeTweenConfig.SetTweensCapacity(10);
            Assert.DoesNotThrow(() => PrimeTweenConfig.defaultEase = Ease.InCirc);
        }

        Object.DestroyImmediate(go);
        void ExpectError() { }
        LogAssert.NoUnexpectedReceived();
        PrimeTweenConfig.warnEndValueEqualsCurrent = true;
        PrimeTweenConfig.warnZeroDuration = true;
    }

    [UnityTest]
    public IEnumerator TweenAnimationComponentAnimations() {
        Type inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        new GameObject(nameof(AudioListener)).AddComponent<AudioListener>();
        bool testInspector = Random.value > 0.0f;
        var tweenTypes = Enum.GetValues(typeof(TweenAnimation.TweenType)).Cast<TweenAnimation.TweenType>();

        // TweenAnimation.TweenType[] tweenTypes = { TweenAnimation.TweenType.UIHorizontalNormalizedPosition };
        foreach (var tweenType in tweenTypes) {
            // Debug.Log(tweenType);
            (PropType propType, Type targetType) = Utils.TweenTypeToTweenData(tweenType);

            if (propType == PropType.Quaternion) {
                continue;
            }

            switch (tweenType) {
                case TweenAnimation.TweenType.Disabled:
                case TweenAnimation.TweenType.MainSequence:
                case TweenAnimation.TweenType.NestedSequence:
                case TweenAnimation.TweenType.TweenTimeScale:
                case TweenAnimation.TweenType.TweenTimeScaleSequence:
                case TweenAnimation.TweenType.VisualElementLayout:
                case TweenAnimation.TweenType.VisualElementPosition:
                case TweenAnimation.TweenType.VisualElementRotationQuaternion:
                case TweenAnimation.TweenType.VisualElementScale:
                case TweenAnimation.TweenType.VisualElementSize:
                case TweenAnimation.TweenType.VisualElementTopLeft:
                case TweenAnimation.TweenType.VisualElementColor:
                case TweenAnimation.TweenType.VisualElementBackgroundColor:
                case TweenAnimation.TweenType.VisualElementOpacity:
#if PRIME_TWEEN_EXPERIMENTAL
                case TweenAnimation.TweenType.CustomDouble:
#endif
                case TweenAnimation.TweenType.TweenAwaiter:
                case TweenAnimation.TweenType.GlobalTimeScale: // don't animate global time scale in tests
                    continue;
            }

            GameObject go;

            if (targetType == typeof(Renderer)) {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = tweenType.ToString();
            } else {
                go = new GameObject(tweenType.ToString());
            }

            var animationComponent =
                go.AddComponent<TweenAnimationComponent>(); // create as the first component on the GameObject for easier visual validation

            Object target = GetTarget();

            Object GetTarget() {
                if (tweenType == TweenAnimation.TweenType.MaterialPropertyVector4) {
                    return new Material(Shader.Find("ShaderWithVector4Property"));
                }

                if (targetType == typeof(Material)) {
                    return Resources.FindObjectsOfTypeAll<Material>().Single(x => x.name == "Default-Material");
                }

                if (targetType == typeof(Transform)) {
                    return go.GetComponent<Transform>();
                }

                if (targetType == typeof(TMP_Text)) {
                    var text = go.AddComponent<TextMeshPro>();
                    const string str = "0123456789012345678901234567890123456789";
                    text.text = str;
                    text.ForceMeshUpdate();
                    return text;
                }

                if (targetType == typeof(Graphic)) {
                    return go.AddComponent<RawImage>();
                }

                if (typeof(Object).IsAssignableFrom(targetType)) {
                    if (targetType == typeof(Renderer)) {
                        var renderer = go.GetComponent<Renderer>();
                        Assert.IsNotNull(renderer);

                        if (tweenType == TweenAnimation.TweenType.MaterialPropertyBlockPropertyVector4) {
                            renderer.material = new Material(Shader.Find("ShaderWithVector4Property"));
                        }

                        return renderer;
                    }

                    var result = go.AddComponent(targetType);

                    if (result is ScrollRect scrollRect) {
                        scrollRect.content = go.GetComponent<RectTransform>();
                        Assert.IsNotNull(scrollRect.content);
                    }

                    return result;
                }

                return null;
            }

            const float duration = 0.1f;
            TweenAnimation.ValueWrapper startValue;
            TweenAnimation.ValueWrapper endValue;

            if (tweenType == TweenAnimation.TweenType.TextMaxVisibleCharacters) {
                startValue = 5f.ToContainer();
                endValue = 10f.ToContainer();
            } else {
                startValue = new Vector4(0.1f, 0.2f, 0.3f, 0.4f).ToContainer();
                endValue = new Vector4(0.4f, 0.3f, 0.2f, 0.1f).ToContainer();
            }

            string stringParam = GetStringParam();

            string GetStringParam() {
                switch (tweenType) {
                    case TweenAnimation.TweenType.MaterialPropertyBlockPropertyVector4:
                    case TweenAnimation.TweenType.MaterialPropertyVector4:
                        return "_ColorVector";
                    case TweenAnimation.TweenType.MaterialPropertyBlockColorProperty:
                    case TweenAnimation.TweenType.MaterialPropertyBlockAlphaProperty:
                    case TweenAnimation.TweenType.MaterialAlphaProperty:
                    case TweenAnimation.TweenType.MaterialColorProperty:
                        return "_Color";
                    case TweenAnimation.TweenType.MaterialPropertyBlockProperty:
                    case TweenAnimation.TweenType.MaterialProperty:
                        return "_Mode";
                    case TweenAnimation.TweenType.MaterialPropertyBlockTextureScale:
                    case TweenAnimation.TweenType.MaterialPropertyBlockTextureOffset:
                    case TweenAnimation.TweenType.MaterialTextureOffset:
                    case TweenAnimation.TweenType.MaterialTextureScale:
                        return "_MainTex";
                    default:
                        return string.Empty;
                }
            }

            var data = new TweenAnimation.Data {
                tweenType = tweenType,
                targets = new List<Object> { target },
                duration = duration,
                stringParam = stringParam,
                hasStartValue = TweenAnimation.Data.IsCustomTweenType(tweenType),
                startValue = startValue,
                endValue = endValue
            };

            const float shakeFrequency = 10f;

            switch (tweenType) {
                case TweenAnimation.TweenType.ShakeCamera:
                    data.shakeCameraStrengthFactor = startValue.single;
                    data.shakeFrequency = shakeFrequency;
                    break;
                case TweenAnimation.TweenType.ShakeLocalPosition:
                case TweenAnimation.TweenType.ShakeLocalRotation:
                case TweenAnimation.TweenType.ShakeScale:
                    data.shakeStrength = startValue.vector3;
                    data.shakeFrequency = shakeFrequency;
                    break;
                case TweenAnimation.TweenType.ShakeCustom:
                    data.shakeStrength = startValue.vector3;
                    data.shakeFrequency = shakeFrequency;

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventVector3 = new TweenAnimation.Data.Custom.UnityEventVector3()
                    };

                    break;

                case TweenAnimation.TweenType.CustomColor: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventColor();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventColor = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.CustomFloat: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventFloat();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventFloat = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.CustomRect: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventRect();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventRect = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.CustomVector2: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventVector2();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventVector2 = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.CustomVector3: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventVector3();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventVector3 = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.CustomVector4: {
                    var evt = new TweenAnimation.Data.Custom.UnityEventVector4();
                    evt.AddListener(x => _ = x);

                    data.customData = new TweenAnimation.Data.Custom {
                        unityEventVector4 = evt
                    };

                    break;
                }

                case TweenAnimation.TweenType.Callback:
                    data.customData = new TweenAnimation.Data.Custom {
                        callback = new UnityEvent()
                    };

                    break;
            }

            animationComponent.animation = new TweenAnimation {
                animations = new List<TweenAnimation.Data> { data },
                useUnscaledTime = tweenType == TweenAnimation.TweenType.GlobalTimeScale
            };

            bool supportsStartValueGetter = !TweenAnimation.Data.IsCustomTweenType(tweenType);

            switch (tweenType) {
                case TweenAnimation.TweenType.Callback:
                case TweenAnimation.TweenType.Rotation:
                case TweenAnimation.TweenType.LocalRotation:
                case TweenAnimation.TweenType.RigidbodyMoveRotation:
                case TweenAnimation.TweenType.ShakeCamera:
                case TweenAnimation.TweenType.ShakeCustom:
                case TweenAnimation.TweenType.ScaleUniform:
                case TweenAnimation.TweenType.TweenAnimationComponent:
                // Setting up a scroll rect is a complex process, which this test doesn't cover
                case TweenAnimation.TweenType.UINormalizedPosition:
                case TweenAnimation.TweenType.UIHorizontalNormalizedPosition:
                case TweenAnimation.TweenType.UIVerticalNormalizedPosition:
                    supportsStartValueGetter = false;
                    break;
            }

            if (supportsStartValueGetter) {
                TweenData rt = new TweenData {
                    target = target,
                    endValueOrDiff = endValue,
                    cold = new ColdData {
                        longParam = Shader.PropertyToID(data.stringParam),
                        onValueChange = delegate { Debug.LogError($"onValueChange should not be called {tweenType}"); }
                    },
                    Id = 42
                };

                UnmanagedTweenData d = new UnmanagedTweenData {
                    IsAlive = true,
                    tweenType = tweenType,
                    startValue = startValue,
                    id = 42
                };

                var shakeData = new ShakeData();
                shakeData.Setup(new ShakeSettings(Vector3.one), ref rt, ref d, target);
                rt.cold.shakeData = shakeData;

                if (tweenType == TweenAnimation.TweenType.GlobalTimeScale) {
                    d.startValue = 1f.ToContainer();
                }

                Utils.SetAnimatedValue(ref rt, ref d);

                if (testInspector) {
                    Selection.activeGameObject = go;
                    EditorWindow.GetWindow(inspectorWindowType);

                    bool isRigidbody = false;

                    switch (tweenType) {
                        case TweenAnimation.TweenType.RigidbodyMovePosition:
                        case TweenAnimation.TweenType.RigidbodyMovePosition2D:
                        case TweenAnimation.TweenType.RigidbodyMoveRotationQuaternion:
                        case TweenAnimation.TweenType.RigidbodyMoveRotation:
                        case TweenAnimation.TweenType.RigidbodyMoveRotation2D:
                            isRigidbody = true;
                            break;
                    }

                    yield return WaitInEditor();

                    if (isRigidbody) { } else {
                        TweenAnimation.ValueWrapper savedStartValue =
                            animationComponent.animation.animations[0].startValue;

                        if (Utils.TweenTypeToTweenData(tweenType).Item1 == PropType.Quaternion) {
                            Assert.AreEqual(startValue.quaternion.normalized, savedStartValue.quaternion.normalized);
                        } else {
                            const float tolerance = 0.0001f;
                            Assert.AreEqual(startValue.x, savedStartValue.x, tolerance);

                            if (savedStartValue.y != 0f) {
                                Assert.AreEqual(startValue.y, savedStartValue.y, tolerance);
                            }

                            if (savedStartValue.z != 0f) {
                                Assert.AreEqual(startValue.z, savedStartValue.z, tolerance);
                            }

                            if (Utils.IsShake(tweenType)) {
                                Assert.AreEqual(shakeFrequency, savedStartValue.w);
                            } else if (savedStartValue.w != 0f) {
                                Assert.AreEqual(startValue.w, savedStartValue.w, tolerance);
                            }
                        }
                    }
                }
            } else if (testInspector) {
                Selection.activeGameObject = go;
                EditorWindow.GetWindow(inspectorWindowType);
                yield return WaitInEditor();
            }

            animationComponent.animation.Trigger();
            var seq = animationComponent.animation.sequence;
            Assert.IsTrue(seq.isAlive);
            ColdData t = null;

            foreach (var child in seq.GetAllChildren()) {
                t = child;
            }

            Assert.IsNotNull(t);

            seq.Stop();
            Selection.activeGameObject = null;
            yield return WaitInEditor();
        }

        if (testInspector) {
            EditorWindow.GetWindow<TestRunnerWindow>();
        }

        LogAssert.NoUnexpectedReceived();
    }

    private IEnumerator WaitInEditor() {
        double startTime = EditorApplication.timeSinceStartup;

        while (EditorApplication.timeSinceStartup - startTime < 0.01) {
            yield return null;
        }
    }

    /// If the test fails, ensure that the TweenAnimationComponent is expanded in Inspector
    [UnityTest]
    public IEnumerator RecursiveTweenAnimationComponent() {
        LogAssert.Expect(
            LogType.Warning,
            new Regex("It's not allowed to reference 'TweenAnimationComponent' from itself.")
        );

        var animationComponent = new GameObject().AddComponent<TweenAnimationComponent>();

        animationComponent.animation.animations.Add(
            new TweenAnimation.Data {
                tweenType = TweenAnimation.TweenType.TweenAnimationComponent,
                targets = new List<Object> { animationComponent }
            }
        );

        Selection.activeGameObject = animationComponent.gameObject;
        Type inspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        EditorWindow.GetWindow(inspectorWindowType);
        yield return WaitInEditor();
        Selection.activeGameObject = null;
    }

    [UnityTest]
    public IEnumerator TargetTweenAnimationComponentNullAnimation() {
        for (int _ = 0; _ < 2; _++) {
            LogAssert.Expect(LogType.Error, new Regex("Target's TweenAnimationComponent animation is null."));
        }

        var animationComponent1 = new GameObject().AddComponent<TweenAnimationComponent>();
        animationComponent1.animation = null;

        var animationComponent2 = new GameObject().AddComponent<TweenAnimationComponent>();
        Selection.activeGameObject = animationComponent2.gameObject;

        animationComponent2.animation.animations.Add(
            new TweenAnimation.Data {
                tweenType = TweenAnimation.TweenType.TweenAnimationComponent,
                targets = new List<Object> { animationComponent1 }
            }
        );

        animationComponent2.animation.Trigger();
        yield return WaitInEditor();
        Selection.activeGameObject = null;
    }
}
#endif