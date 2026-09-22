#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
#define SAFETY_CHECKS
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;
using SerializeField = UnityEngine.SerializeField;
using HideInInspector = UnityEngine.HideInInspector;
using Random = System.Random;
using Transform = UnityEngine.Transform;
using TweenType = PrimeTween.TweenAnimation.TweenType;
#if UNITY_EDITOR
using UnityEditor;
using SceneManagement = UnityEditor.SceneManagement;
#endif

namespace PrimeTween {
    // p1 todo document experimental features like additive tweens
    [AddComponentMenu("")]
    internal sealed class PrimeTweenManager : MonoBehaviour {
        internal static readonly Random sRandom = new();
        internal bool IsDestroyed { get; private set; }

#if UNITY_EDITOR || SAFETY_CHECKS
        internal static PrimeTweenManager sInstance;
#endif

#if UNITY_EDITOR
        internal static PrimeTweenManager Instance {
            get {
                if (!HasInstance) {
                    if (Application.isEditor && !Application.isPlaying) {
                        // DebugLifetimeStatic("CreateInstance lazily in Edit Mode");
                        CreateInstance();
                        sInstance.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    } else {
                        const string error = nameof(PrimeTweenManager)
                                             + " is not created yet. Please add the 'PRIME_TWEEN_EXPERIMENTAL' define to your project, then use '"
                                             + nameof(PrimeTweenConfig)
                                             + "."
                                             + nameof(PrimeTweenConfig.ManualInitialize)
                                             + "()' to initialize PrimeTween before '"
                                             + nameof(RuntimeInitializeLoadType)
                                             + "."
                                             + nameof(RuntimeInitializeLoadType.BeforeSceneLoad)
                                             + "'.";

                        throw new Exception(error);
                    }
                } /*else if (sInstance == null) { // p2 todo this throws if PrimeTween API is called from OnDestroy(). See the DestructionOrderTest scene. How to detect manual PrimeTweenManager destruction? Also, a user can destroy the PrimeTweenManager manually
                    throw new Exception(nameof(PrimeTweenManager) + " was manually destroyed after creation, which is not allowed. Please check you're not destroying all objects manually.");
                }*/

                return sInstance;
            }
            private set => sInstance = value;
        }
#else
        internal static PrimeTweenManager Instance;
#endif

        internal static bool HasInstance {
            get {
#if UNITY_EDITOR || SAFETY_CHECKS
                return !ReferenceEquals(null, sInstance);
#else
                return Instance != null;
#endif
            }
        }

        internal static int sCustomInitialCapacity = -1;

        internal TweenArray tweensUpdate;
        internal TweenArray tweensLateUpdate;
        internal TweenArray tweensFixedUpdate;

        internal TweenArray newTweensUpdate;
        internal TweenArray newTweensLateUpdate;
        internal TweenArray newTweensFixedUpdate;

#if PRIME_TWEEN_EXPERIMENTAL
        internal TweenArray tweensManual;
        internal TweenArray newTweensManual;
#endif

        internal TweenArray[] allTweenArrays;

        internal List<CoroutineIterator> coroutineIterators;

        [NonSerialized]
        internal List<ColdData> pool;

        /// startValue can't be replaced with 'Tween lastTween'
        /// because the lastTween may already be dead, but the tween before it is still alive (count >= 1)
        /// and we can't retrieve the startValue from the dead lastTween
        ///
        /// We also can't implement a similar caching for non-Transform shakes because there can be multiple custom shakes on the same target.
        /// And it's impossible to tell which shake should be de-duplicated and which should not.
        internal Dictionary<(Transform, TweenType), (TweenAnimation.ValueWrapper startValue, int count)> shakes;

        internal int CurrentPoolCapacity { get; private set; }
        internal int MaxSimultaneousTweensCount { get; private set; }

        [NonSerialized]
        internal long lastId = 1;

        internal Ease defaultEase = Ease.OutQuad;
        internal EUpdateType defaultUpdateType = EUpdateType.Update;
        internal const Ease kDefaultShakeEase = Ease.OutQuad;
        internal bool warnTweenOnDisabledTarget = true;
        internal bool warnZeroDuration = true;
        internal bool warnStructBoxingAllocationInCoroutine = true;
        internal bool warnBenchmarkWithAsserts = true;
        internal bool validateCustomCurves = true;
        internal bool warnEndValueEqualsCurrent = true;
        internal bool warnIfTargetDestroyed = true;
        internal int updateDepth;
        internal static readonly object sDummyTarget = new();
        internal bool completeAllRequested;
        internal object completeAllRequestedTarget;
        private readonly int m_HashCode;
        internal MaterialPropertyBlock materialPropertyBlockForSetter;
        internal MaterialPropertyBlock materialPropertyBlockForGetter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad() {
            if (!HasInstance) {
                CreateInstanceAndDontDestroy();
            }
        }

        internal static void CreateInstanceAndDontDestroy() {
            DebugLifetimeStatic("CreateInstanceAndDontDestroy");
            CreateInstance();
            DontDestroyOnLoad(Instance.gameObject);
        }

        private static GameObject CreateNewGameObject() {
#if UNITY_EDITOR
            if (Application.isEditor) {
                const string exceptionMessage =
                    "this PrimeTween's API is not allowed to be called from a MonoBehaviour constructor, instance field initializer, or during domain reload.";

                if (!EditorApplication.isPlaying && !EnteredEditMode) {
                    // DebugLifetimeStatic("EnteredEditMode check failed");
                    throw new Exception(exceptionMessage);
                }

                if (EditorApplication.isUpdating) {
                    // DebugLifetimeStatic("EditorApplication.isUpdating check failed");
                    throw new Exception(exceptionMessage);
                }

                try {
                    return new GameObject(nameof(PrimeTweenManager));
                } catch (UnityException e) {
                    if (e.Message.Contains("is not allowed to be called from a MonoBehaviour constructor")) {
                        // DebugLifetimeStatic("new GameObject check failed");
                        throw new Exception(exceptionMessage);
                    }

                    throw;
                }
            }
#endif
            return new GameObject(nameof(PrimeTweenManager));
        }

        private static void CreateInstance() {
            GameObject go = CreateNewGameObject();
            var instance = go.AddComponent<PrimeTweenManager>();
            const int defaultInitialCapacity = 200;
            instance.Init(sCustomInitialCapacity != -1 ? sCustomInitialCapacity : defaultInitialCapacity);
            Instance = instance;
        }

        private void Init(int capacity) {
            DebugLifetime("Init");
            Assert.IsNull(allTweenArrays);
            Assert.IsNull(tweensUpdate);
            tweensUpdate = new TweenArray(capacity, nameof(tweensUpdate));
            tweensLateUpdate = new TweenArray(capacity, nameof(tweensLateUpdate));
            tweensFixedUpdate = new TweenArray(capacity, nameof(tweensFixedUpdate));
            newTweensUpdate = new TweenArray(capacity, nameof(newTweensUpdate));
            newTweensLateUpdate = new TweenArray(capacity, nameof(newTweensLateUpdate));
            newTweensFixedUpdate = new TweenArray(capacity, nameof(newTweensFixedUpdate));
#if PRIME_TWEEN_EXPERIMENTAL
            tweensManual = new TweenArray(capacity, nameof(tweensManual));
            newTweensManual = new TweenArray(capacity, nameof(newTweensManual));
#endif
            allTweenArrays = new[] {
                tweensUpdate, newTweensUpdate, tweensLateUpdate, newTweensLateUpdate, tweensFixedUpdate,
                newTweensFixedUpdate,
#if PRIME_TWEEN_EXPERIMENTAL
                tweensManual, newTweensManual
#endif
            };

            pool = new List<ColdData>(capacity);
            ResizeAndSetCapacity(pool, capacity, capacity);

            shakes = new Dictionary<(Transform, TweenType), (TweenAnimation.ValueWrapper, int)>(capacity);
            CurrentPoolCapacity = capacity;

            materialPropertyBlockForSetter = new MaterialPropertyBlock();
            materialPropertyBlockForGetter = new MaterialPropertyBlock();

            coroutineIterators = new List<CoroutineIterator>(capacity);
            ResizeAndSetCapacity(coroutineIterators, capacity, capacity);
        }

        private const string k_ManualInstanceCreationIsNotAllowedMessage =
            "Please don't create the " + nameof(PrimeTweenManager) + " instance manually.";

        private void Awake() => Assert.IsFalse(HasInstance, k_ManualInstanceCreationIsNotAllowedMessage);

        private void OnDestroy() {
            DebugLifetime("OnDestroy");
            Dispose();
#if UNITY_EDITOR
            foreach (var backup in startValuesBackup) {
                var target = backup.target;

                if (target != null) {
                    // Debug.Log($"restore {backup.tweenType}, {target.name}, {startValue}", target);
                    Utils.SetMaterialValue(
                        backup.tweenType,
                        target,
                        Shader.PropertyToID(backup.propertyName),
                        backup.startValue
                    );
                }
            }
#endif
        }

        private void Dispose() {
            if (!IsDestroyed) {
                DebugLifetime("Dispose");
                Assert.IsNotNull(allTweenArrays);
                Assert.IsFalse(IsDestroyed);
                IsDestroyed = true;

                foreach (var arr in allTweenArrays) {
                    arr.Dispose();
                }
            }
        }

#if UNITY_EDITOR
        internal static bool sIsInspectorScrubbingPaused;

        internal bool updateInspectorTweens;

        [SerializeField]
        internal List<TweenInspectorData> inspectorTweensUpdate = new();

        [SerializeField]
        internal List<TweenInspectorData> inspectorTweensLateUpdate = new();

        [SerializeField]
        internal List<TweenInspectorData> inspectorTweensFixedUpdate = new();

        internal void UpdateInspectorTweens() {
            if (updateInspectorTweens) {
                AddTweensToInspectorList(inspectorTweensUpdate, EUpdateType.Update);
                AddTweensToInspectorList(inspectorTweensLateUpdate, EUpdateType.LateUpdate);
                AddTweensToInspectorList(inspectorTweensFixedUpdate, EUpdateType.FixedUpdate);
            }
        }

        private void AddTweensToInspectorList(List<TweenInspectorData> list, EUpdateType updateType) {
            list.Clear();
            Add(GetCurrentTweensArray(updateType));
            Add(GetNewTweensArray(updateType));

            void Add(TweenArray array) {
                foreach (var el in array) {
                    var rt = el.Tween;
                    var cold = rt.cold;

                    if (cold == null) {
                        list.Add(default);
                        continue;
                    }

                    if (string.IsNullOrEmpty(cold.debugDescription)) {
                        cold.debugDescription = rt.GetDescription();
                    }

                    var d = el.Data;

                    list.Add(
                        new TweenInspectorData {
                            debugDescription = cold.debugDescription,
                            unityTarget = rt.target as UnityEngine.Object,
                            isPaused = d.IsPaused,
                            elapsedTimeTotal = d.elapsedTimeTotal,
                            easedInterpolationFactor = d.easedInterpolationFactor,
                            startEndValue = new ValueContainerStartEnd {
                                tweenType = d.tweenType,
                                startFromCurrent = d.StartFromCurrent,
                                startValue = d.startValue,
                                endValue = GetEndValue()
                            },
                            settings = new TweenSettings {
                                duration = d.animationDuration,
                                ease = d.ease,
                                customEase = cold.customEase,
                                cycles = d.cyclesTotal,
                                cycleMode = d.cycleMode,
                                startDelay = d.startDelay,
                                endDelay = d.cycleDuration - d.startDelay,
                                useUnscaledTime = d.UseUnscaledTime,
                                _updateType = d.updateType,
                            },
                            cycledDone = d.cyclesDone
                        }
                    );

                    TweenAnimation.ValueWrapper GetEndValue() {
                        var endValue = rt.endValueOrDiff;

                        if (d.StartFromCurrent) {
                            return endValue;
                        }

                        var diff = rt.endValueOrDiff;

                        switch (d.PropType) {
                            case PropType.Quaternion:
                                return endValue;
                            case PropType.Double:
                                return (d.startValue.DoubleVal + diff.DoubleVal).ToContainer();
                            default:
                                return (d.startValue.vector4 + diff.vector4).ToContainer();
                        }
                    }
                }
            }
        }

        [Serializable]
        internal struct StartValueBackupData {
            internal Material target;
            internal TweenAnimation.ValueWrapper startValue;
            internal TweenType tweenType;
            internal string propertyName;
        }

        [SerializeField, HideInInspector]
        internal List<StartValueBackupData> startValuesBackup = new();

        [Serializable]
        internal struct CurrentAnimationData {
            public float startTime;
            public float duration;
            public float progressTotal;
        }

        [SerializeField]
        internal List<CurrentAnimationData> currentAnimationData = new();

        internal float currentAnimationDurationOrZero;

        [SerializeField, HideInInspector]
        internal List<TweenAnimation>
            currentTweenAnimations = new(); // should be non-readonly serialized list to support script hot reloading

        [SerializeField, HideInInspector]
        internal List<TweenAnimation> currentTweenAnimationsPrefabStage = new();

        internal void TryAddCurrentTweenAnimation(TweenAnimation a) {
            Assert.IsNotNull(a);

            if (EnteredEditMode) {
                if (!currentTweenAnimations.Contains(a)) {
                    // Debug.Log($"Add currentTweenAnimations {GetHashCode()} {(a.context != null ? a.context.name : "NULL")}");
                    currentTweenAnimations.Add(a);
                }
            } else if (HasPrefabStage()) {
                if (!currentTweenAnimationsPrefabStage.Contains(a)) {
                    if (HasTargetInPrefabState(a)) {
                        // Debug.Log($"Add currentTweenAnimationsPrefabStage {GetHashCode()} {(a.context != null ? a.context.name : "NULL")}");
                        currentTweenAnimationsPrefabStage.Add(a);
                    }
                }
            }
        }

        internal void ResetCurrentTweenAnimations(
            PlayModeStateChange stateChangeForRegularAnimations = PlayModeStateChange.EnteredEditMode
        ) {
            if (sPlayModeState == stateChangeForRegularAnimations) {
                foreach (var tweenAnimation in currentTweenAnimations) {
                    // Debug.Log($"Reset currentTweenAnimations {(tweenAnimation.context != null ? tweenAnimation.context.name : "NULL")} count: {currentTweenAnimations.Count}");
                    tweenAnimation.Reset();
                }
            }

            currentTweenAnimations.Clear();

            if (HasPrefabStage()) {
                foreach (var tweenAnimation in currentTweenAnimationsPrefabStage) {
                    if (HasTargetInPrefabState(tweenAnimation)) {
                        // Debug.Log($"Reset currentTweenAnimationsPrefabStage {(tweenAnimation.context != null ? tweenAnimation.context.name : "NULL")} count: {currentTweenAnimationsPrefabStage.Count}");
                        tweenAnimation.Reset();
                    }
                }
            }

            currentTweenAnimationsPrefabStage.Clear();
        }

        private static bool HasTargetInPrefabState(TweenAnimation a) {
            Assert.IsNotNull(a);

            if (a.animations != null) {
                foreach (var d in a.animations) {
                    if (d.targets != null) {
                        foreach (var target in d.targets) {
                            if (target is Component comp && IsInPrefabStage(comp.gameObject)) {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        ~PrimeTweenManager() {
            DebugLifetime("~PrimeTweenManager");
            Selection.selectionChanged -= () => ResetCurrentTweenAnimations();
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        }

        private PrimeTweenManager() {
            m_HashCode = GetHashCode();
            DebugLifetime("PrimeTweenManager");
            Selection.selectionChanged += () => ResetCurrentTweenAnimations();
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private void BeforeAssemblyReload() {
            DebugLifetime("BeforeAssemblyReload");

            if (Application.isPlaying) {
                var count = TweensCount;

                if (count > 0) {
                    Debug.Log($"All tweens ({TweensCount}) were stopped because of 'Recompile And Continue Playing'.");
                }
            }

            // Unity doesn't call OnDestroy during hot reload, so we need to dispose manually before reloading assemblies
            Dispose();
        }

        internal static PlayModeStateChange? sPlayModeState;
        internal static bool EnteredEditMode => sPlayModeState == PlayModeStateChange.EnteredEditMode;

        [InitializeOnLoadMethod]
        private static void IniOnLoad() {
            DebugLifetimeStatic("IniOnLoad");

            SceneManagement.PrefabStage.prefabSaving += _ => {
                if (HasInstance) {
                    sInstance.ResetCurrentTweenAnimations();
                }
            };

            AssemblyReloadEvents.afterAssemblyReload += () => {
                var newState = EditorApplication.isPlayingOrWillChangePlaymode ? PlayModeStateChange.EnteredPlayMode
                    : PlayModeStateChange.EnteredEditMode;

                if (sPlayModeState != newState) {
                    DebugLifetimeStatic($"AfterAssemblyReload sPlayModeState = {newState}");
                    sPlayModeState = newState;
                }
            };

            double curTime = (float)EditorApplication.timeSinceStartup;

            EditorApplication.update += () => {
                if (Application.isPlaying) {
                    return;
                }

                double newTime = EditorApplication.timeSinceStartup;
                double unscaledDeltaTime = newTime - curTime;

                if (unscaledDeltaTime < 1f / 120f) {
                    return;
                }

                if (unscaledDeltaTime > 1f / 10f) {
                    // Unity Editor doesn't trigger EditorApplication.update when a context menu is open, which results in a big time jump. Clamp dt in this case
                    unscaledDeltaTime = 1f / 120f;
                }

                curTime = newTime;

                if (HasInstance && Instance.TweensCount > 0) {
                    float deltaTime = (float)(unscaledDeltaTime * Time.timeScale);
                    Instance.UpdateTweens(EUpdateType.Update, deltaTime, (float)unscaledDeltaTime);
                    Instance.UpdateTweens(EUpdateType.LateUpdate, deltaTime, (float)unscaledDeltaTime);
                    Instance.UpdateTweens(EUpdateType.FixedUpdate, deltaTime, (float)unscaledDeltaTime);
                    EditorApplication.QueuePlayerLoopUpdate();

#if UNITY_EDITOR
                    Instance.UpdateInspectorTweens();
#endif
                }
            };

            EditorApplication.playModeStateChanged += state => {
                sPlayModeState = state;
                DebugLifetimeStatic($"_playModeState: {sPlayModeState}");

                switch (state) {
                    case PlayModeStateChange.EnteredEditMode:
                        Instance = null;
                        sCustomInitialCapacity = -1;
                        TweenAnimation.sIsPreviewing = false;
                        break;
                    case PlayModeStateChange.ExitingEditMode:
                        sIsInspectorScrubbingPaused = false;

                        if (HasInstance) {
                            sInstance.ResetCurrentTweenAnimations(
                                state
                            ); // passing the current state allows resetting animations in the current state instead of the default PlayModeStateChange.EnteredEditMode

                            sInstance.Dispose();
                            sInstance.DebugLifetime("DestroyImmediate");
                            DestroyImmediate(sInstance.gameObject);
                            Instance = null;
                        }

                        break;
                }
            };

            if (HasInstance) {
                return;
            }

            DebugLifetimeStatic("no Instance, try hot reload");

            if (Application.isPlaying) {
                sPlayModeState = PlayModeStateChange.EnteredPlayMode;
                DebugLifetimeStatic($"Application.isPlaying, set sPlayModeState: {sPlayModeState}");
            }

            var instances = Resources.FindObjectsOfTypeAll<PrimeTweenManager>();

            Assert.IsTrue(
                instances.Length <= 1,
                null,
                $"{instances.Length}: {string.Join(", ", System.Linq.Enumerable.Select(instances, x => x.m_HashCode.ToString()))}"
            );

            if (instances.Length == 0) {
                return;
            }

            var foundInScene = instances[0];

            if (foundInScene == null) {
                return;
            }
#if PRIME_TWEEN_INSPECTOR_DEBUGGING
            Debug.LogError("PRIME_TWEEN_INSPECTOR_DEBUGGING doesn't work with 'Recompile And Continue Playing' because Tween.id is serializable but Tween.tween is not.");
            return;
#endif
            foundInScene.DebugLifetime("hot reload done");
            foundInScene.IsDestroyed = false;
            foundInScene.Init(foundInScene.CurrentPoolCapacity);
            foundInScene.updateDepth = 0;
            foundInScene.lastId = 1;
            Instance = foundInScene;
            foundInScene.ResetCurrentTweenAnimations();
        }

        private void Reset() {
            Assert.IsFalse(Application.isPlaying);
        }
#endif // UNITY_EDITOR

        [Conditional("_")]
        private static void DebugLifetimeStatic(string log) {
            int hashCode = HasInstance ? Instance.m_HashCode : -1;
            Debug.Log($"{hashCode}: {log}");
        }

        [Conditional("_")]
        private void DebugLifetime(string log) {
            Debug.Log($"{m_HashCode}: {log}");
        }

        private void Start() {
            Assert.AreEqual(Instance, this, k_ManualInstanceCreationIsNotAllowedMessage);
        }

        internal void FixedUpdate() => UpdateTweens(EUpdateType.FixedUpdate);

        /// <summary>
        /// The most common tween lifecycle:
        /// 1. User's script creates a tween in Update() in frame N.
        /// 2. PrimeTweenManager.LateUpdate() applies the 'startValue' to the tween in the SAME FRAME N. This guarantees that the animation is rendered at the 'startValue' in the same frame the tween is created.
        /// 3. PrimeTweenManager.Update() executes the first animation step on frame N+1. PrimeTweenManager's execution order is -2000; this means that
        ///     all tweens created in previous frames will already be updated before the user's script Update() (if the user's script execution order is greater than -2000).
        /// 4. PrimeTweenManager.Update() completes the tween on frame N+(duration*targetFrameRate) given that targetFrameRate is stable.
        /// </summary>
        internal void Update() => UpdateTweens(EUpdateType.Update);

        private void UpdateTweenArray(TweenArray array, float deltaTime, float unscaledDeltaTime) {
            if (updateDepth != 0) {
                foreach (var arr in allTweenArrays) {
                    foreach (var el in arr) {
                        var t = el.Tween.cold;

                        if (t != null && t.Data.IsAlive) {
                            var onComplete = t.onComplete;

                            if (onComplete != null) {
                                try {
                                    onComplete(t.ManagedData);
                                } catch (Exception e) {
                                    Debug.LogException(e);
                                }
                            }

                            t.longParam = -1;
                            t.id = -1;
                            t.Data.IsAlive = false;
                        }
                    }

                    arr.Clear();
                }

                shakes.Clear();
                updateDepth = 0;

                Debug.LogError(
                    "PrimeTween recovered from an exception, all running animations have been stopped. Please reach out support describing the issue and providing error logs: https://github.com/KyryloKuzyk/PrimeTween?tab=readme-ov-file#support."
                );

                return;
            }

            updateDepth++;
            int count = array.Count;

#if SAFETY_CHECKS
            if (sRandom.NextDouble() > 0.5f)
#else
            if (count > 0)
#endif
            {
                unsafe {
                    Profiler.BeginSample(nameof(TweenData.UpdateTweensJob));

                    var job = new TweenData.UpdateTweensJob {
                        dataPtr = array.DataPtr,
                        deltaTime = deltaTime,
                        unscaledDeltaTime = unscaledDeltaTime
                    };

#if SAFETY_CHECKS
                    if (sRandom.NextDouble() > 0.5f)
#else
                    if (count < 2000)
#endif
                    {
                        for (int i = 0; i < count; i++) {
                            job.Execute(i);
                        }
                    } else {
                        job.Schedule(count, 256).Complete();
                    }

                    Profiler.EndSample();
                }
            }

            var numRemoved = 0;

            using (new TweenArray.Lock(array)) {
                // Process tweens in the order of creation.
                // This allows creating tween duplicates because the latest tween on the same value will overwrite the previous ones.
                Profiler.BeginSample(nameof(TweenData.UpdateAndCheckIfRunning));

                for (var i = 0; i < count; i++) {
                    ref TweenData tween = ref array[i];
                    ref UnmanagedTweenData data = ref array.GetDataAt(i);

                    Assert.AreEqual(data.id, tween.cold.id);
                    var newIndex = i - numRemoved;
#if SAFETY_CHECKS
                    Assert.IsNotNull(tween.cold);

                    if (numRemoved > 0) {
                        Assert.IsNull(array[newIndex].cold);
                    }
#endif
                    if (tween.UpdateAndCheckIfRunning(data.UseUnscaledTime ? unscaledDeltaTime : deltaTime, ref data)) {
                        if (i != newIndex) {
                            array.MoveAndClearOld(tween, i, newIndex);
                        }
                    } else {
                        ReleaseTweenToPool(ref tween, ref data);

                        array[i] =
                            default; // set to null after ReleaseTweenToPool() so in case of an exception, the tween will stay inspectable via Inspector

                        numRemoved++;
                    }
                }

                Profiler.EndSample();
            }

#if SAFETY_CHECKS
            Assert.IsTrue(count - numRemoved >= 0);

            using (new TweenArray.Lock(array)) {
                for (int i = count - numRemoved; i < count; i++) {
                    // Check that removed tweens are shifted to the left and are null
                    Assert.IsNull(array[i].cold);
                    Assert.AreEqual(0, array.GetDataAt(i).id);
                }
            }
#endif
            updateDepth--;

            Assert.AreEqual(count, array.Count);

            if (numRemoved > 0) {
                array.TrimEndNulls(numRemoved);
            }

            Assert.AreEqual(array.Count, count - numRemoved);

#if SAFETY_CHECKS

            // Check no duplicates
            m_HashSet.Clear();

            foreach (var el in array) {
                ref TweenData t = ref el.Tween;
                Assert.IsNotNull(t.cold);
                m_HashSet.Add(t);
            }

            Assert.AreEqual(m_HashSet.Count, array.Count);
#endif
            ProcessRequestedCompleteAll();
        }

        void ProcessRequestedCompleteAll() {
            if (completeAllRequested) {
                completeAllRequested = false;
                var completeAllTarget = completeAllRequestedTarget;
                completeAllRequestedTarget = null;
                Tween.CompleteAll(completeAllTarget);
            }
        }

#if SAFETY_CHECKS
        private readonly HashSet<TweenData> m_HashSet = new();
#endif

        internal void LateUpdate() {
            UpdateTweens(EUpdateType.LateUpdate);
            ApplyStartValues(EUpdateType.Update);
            ApplyStartValues(EUpdateType.LateUpdate);

#if UNITY_EDITOR
            UpdateInspectorTweens();
#endif
        }

        [Serializable, UsedImplicitly]
        internal struct TweenInspectorData {
            public string debugDescription;
            public UnityEngine.Object unityTarget;
            public bool isPaused;
            public float elapsedTimeTotal;
            public float easedInterpolationFactor;
            public ValueContainerStartEnd startEndValue;
            public TweenSettings settings;
            public int cycledDone;
        }

        internal void ApplyStartValues(EUpdateType updateType) {
            switch (updateType) {
                case EUpdateType.Default:
                    Debug.LogError("Please provide non-default update type.");
                    break;
                case EUpdateType.Update:
                case EUpdateType.LateUpdate:
                case EUpdateType.FixedUpdate:
                    TweenArray newTweens = GetNewTweensArray(updateType);
                    TweenArray currentTweens = GetCurrentTweensArray(updateType);

                    Assert.IsFalse(newTweens.IsLocked);
                    Assert.IsFalse(currentTweens.IsLocked);
                    Assert.AreEqual(0, updateDepth);

                    int oldCount = currentTweens.Count;
                    AddNewTweens(newTweens, currentTweens);

                    using (new TweenArray.Lock(currentTweens)) {
                        updateDepth++;

                        for (int i = oldCount; i < currentTweens.Count; i++) {
                            ref TweenData tween = ref currentTweens[i];
                            ref UnmanagedTweenData data = ref currentTweens.GetDataAt(i);

                            Assert.IsNotNull(tween.cold);

                            if (data.IsAlive
                                && !data.StartFromCurrent
                                && data.startDelay == 0
                                && !data.IsAdditive
                                && data.CanManipulate()
                                && data.elapsedTimeTotal == 0f) {
                                tween.SetElapsedTimeTotal(0f, true, ref data);
                            }
                        }

                        updateDepth--;
                    }

                    AddNewTweens(newTweens, currentTweens);
                    ProcessRequestedCompleteAll();
                    break;
                default:
                    throw new Exception($"Invalid update type: {updateType}");
            }
        }

        internal void UpdateTweens(EUpdateType updateType, float? deltaTime = null, float? unscaledDeltaTime = null) {
            var currentTweensArray = GetCurrentTweensArray(updateType);

            switch (updateType) {
                case EUpdateType.Default:
                    Debug.LogError("Please provide non-default update type.");
                    break;
                case EUpdateType.Update:
#if PRIME_TWEEN_EXPERIMENTAL
                case EUpdateType.Manual:
#endif
                    AddNewTweens(updateType);

                    UpdateTweenArray(
                        currentTweensArray,
                        deltaTime ?? Time.deltaTime,
                        unscaledDeltaTime ?? Time.unscaledDeltaTime
                    );

                    break;
                case EUpdateType.LateUpdate:
                    // Because LateUpdate executes in the same frame after tweens were created in user-defined Update, we should only process tweens from the previous frame here.
                    // Newly created tweens in this frame should be ignored.
                    UpdateTweenArray(
                        currentTweensArray,
                        deltaTime ?? Time.deltaTime,
                        unscaledDeltaTime ?? Time.unscaledDeltaTime
                    );

                    AddNewTweens(updateType);
                    break;
                case EUpdateType.FixedUpdate:
                    AddNewTweens(updateType);

                    UpdateTweenArray(
                        currentTweensArray,
                        deltaTime ?? Time.fixedDeltaTime,
                        unscaledDeltaTime ?? Time.fixedUnscaledDeltaTime
                    );

                    break;
                default:
                    throw new Exception($"Invalid update type: {updateType}");
            }
        }

        internal void AddNewTweens(EUpdateType updateType) =>
            AddNewTweens(GetNewTweensArray(updateType), GetCurrentTweensArray(updateType));

        static void AddNewTweens(TweenArray newTweens, TweenArray currentTweens) {
            Assert.IsFalse(newTweens.IsLocked);
            Assert.IsFalse(currentTweens.IsLocked, currentTweens.name);

            if (newTweens.Count > 0) {
                foreach (var el in newTweens) {
                    UnmanagedTweenData data = el.Data;
                    TweenData tween = el.Tween;
                    ColdData cold = tween.cold;
                    currentTweens.Add(cold);
                    cold.ManagedData = tween;
                    cold.Data = data;
                }

                newTweens.Clear();
            }
        }

        private void ReleaseTweenToPool(ref TweenData rt, ref UnmanagedTweenData d) {
#if SAFETY_CHECKS
            Assert.IsTrue(rt.Id > 0);

            foreach (var list in allTweenArrays) {
                foreach (var el in list) {
                    var t = el.Tween.cold;

                    if (t != null) {
                        Assert.AreNotEqual(rt.Id, t.next?.id);
                        Assert.AreNotEqual(rt.Id, t.nextSibling?.id);
                        Assert.AreNotEqual(rt.Id, t.prev?.id);
                    }
                }
            }
#endif
            rt.Reset(ref d);
            var coldData = rt.cold;
            Assert.IsNotNull(coldData);
            Assert.AreEqual(-1, coldData.id);
            pool.Add(coldData);
            Assert.AreEqual(-1, rt.Id);
        }

        /// Returns null if the target is a destroyed UnityEngine.Object
        internal static Tween? DelayWithoutDurationCheck(
            [CanBeNull] object target,
            float duration,
            bool useUnscaledTime
        ) {
            var settings = new TweenSettings {
                duration = duration,
                ease = Ease.Linear,
                useUnscaledTime = useUnscaledTime
            };

            if (Instance.IsDestroyed) {
                return null;
            }

            var tween = FetchTween(settings._updateType);
            ref var rt = ref tween.ManagedData;
            ref var d = ref tween.Data;

            tween.Setup(target, ref settings, false, TweenType.Delay, ref rt, ref d);
            var result = AddTween(ref rt, ref d);

            // ReSharper disable once RedundantCast
            return result.IsCreated ? result : (Tween?)null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ColdData FetchTween(EUpdateType updateType) => Instance.FetchTweenInternal(updateType);

        private ColdData FetchTweenInternal(EUpdateType updateType) {
            ColdData coldData;

            if (pool.Count == 0) {
                coldData = new ColdData();

                if (TweensCount + 1 > CurrentPoolCapacity) {
                    var newCapacity = CurrentPoolCapacity == 0 ? 4 : CurrentPoolCapacity * 2;

                    if (Application.isPlaying) {
                        Debug.LogWarning(
                            $"Tweens capacity has been increased from {CurrentPoolCapacity} to {newCapacity}. Please increase the capacity manually to prevent memory allocations at runtime by calling {Constants.kSetTweensCapacityMethod}.\n"
                            + $"To know the highest number of simultaneously running tweens, please observe the '{nameof(PrimeTweenManager)}/{Constants.kMaxAliveTweens}' in Inspector.\n"
                        );
                    }

                    CurrentPoolCapacity = newCapacity;
                }
            } else {
                var lastIndex = pool.Count - 1;
                coldData = pool[lastIndex];
                pool.RemoveAt(lastIndex);
            }

            Assert.IsNotNull(coldData);
            Assert.AreEqual(-1, coldData.id);
            coldData.id = lastId;

            switch (updateType) {
                case EUpdateType.Update:
                case EUpdateType.LateUpdate:
                case EUpdateType.FixedUpdate:
#if PRIME_TWEEN_EXPERIMENTAL
                case EUpdateType.Manual:
#endif
                    break;
                case EUpdateType.Default:
                    updateType = Instance.defaultUpdateType;
                    break;
                default:
                    Debug.LogError($"Invalid update type: {updateType}");
                    updateType = EUpdateType.Update;
                    break;
            }

            GetNewTweensArray(updateType).Add(coldData);
            coldData.Data.updateType = updateType;
            return coldData;
        }

        private TweenArray GetCurrentTweensArray(EUpdateType updateType) {
            switch (updateType) {
                case EUpdateType.Update:
                    return tweensUpdate;
                case EUpdateType.LateUpdate:
                    return tweensLateUpdate;
                case EUpdateType.FixedUpdate:
                    return tweensFixedUpdate;
#if PRIME_TWEEN_EXPERIMENTAL
                case EUpdateType.Manual:
                    return tweensManual;
#endif
                case EUpdateType.Default:
                default:
                    throw new Exception();
            }
        }

        private TweenArray GetNewTweensArray(EUpdateType updateType) {
            switch (updateType) {
                case EUpdateType.Update:
                    return newTweensUpdate;
                case EUpdateType.LateUpdate:
                    return newTweensLateUpdate;
                case EUpdateType.FixedUpdate:
                    return newTweensFixedUpdate;
#if PRIME_TWEEN_EXPERIMENTAL
                case EUpdateType.Manual:
                    return newTweensManual;
#endif
                case EUpdateType.Default:
                default:
                    throw new Exception();
            }
        }

        internal static Tween Animate(ref TweenData rt, ref UnmanagedTweenData d) {
            CheckDuration(rt.target, d.animationDuration);
            return AddTween(ref rt, ref d);
        }

        internal static void CheckDuration<T>([CanBeNull] T target, float duration) where T : class {
            if (Instance.warnZeroDuration && duration <= 0) {
                Debug.LogWarning(
                    $"Tween duration ({duration}) <= 0. {Constants.BuildWarningCanBeDisabledMessage(nameof(warnZeroDuration))}",
                    target as UnityEngine.Object
                );
            }
        }

        internal static Tween AddTween(ref TweenData tween, ref UnmanagedTweenData d) {
            Assert.IsNotNull(tween.cold);
            Assert.IsTrue(tween.cold.HasData);
            var manager = Instance;
            var res = manager.TryAddTween(tween.cold, ref tween);

            if (res.HasValue) {
                return res.Value;
            }

            tween.Kill(ref d);

            manager.ReleaseTweenToPool(
                ref tween,
                ref d
            ); // it calls tween.Reset() under the hood, which requires cold data

            tween.cold.tweenArray.RemoveLast(tween.cold);
            return default;
        }

        private Tween? TryAddTween(ColdData tween, ref TweenData rt) {
            Assert.IsNotNull(tween);
            Assert.IsTrue(tween.id > 0);

            if (rt.target == null || rt.IsUnityTargetDestroyed()) {
                Debug.LogError(
                    $"Tween's target is null: {rt.GetDescription()}. This error can mean that:\n"
                    + "- The target reference is null.\n"
                    + "- UnityEngine.Object target reference is not populated in the Inspector.\n"
                    + "- UnityEngine.Object target has been destroyed.\n"
                    + "Please ensure you're using a valid target.\n"
                );

                return null;
            }

            if (warnTweenOnDisabledTarget) {
                if (rt.target is Component comp && !comp.gameObject.activeInHierarchy) {
                    Debug.LogWarning(
                        $"Tween is started on GameObject that is not active in hierarchy: {comp.name}. {Constants.BuildWarningCanBeDisabledMessage(nameof(warnTweenOnDisabledTarget))}",
                        comp
                    );
                }
            }
#if SAFETY_CHECKS

            // rt.print($"[{Time.frameCount}] tween created lastId:{Instance.lastId}, _hashCode:{Instance._hashCode}");
            StackTraces.Record(tween.id);
#endif
            lastId++; // increment only when tween added successfully
#if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
            MaxSimultaneousTweensCount = Math.Max(MaxSimultaneousTweensCount, TweensCount);

            if (warnBenchmarkWithAsserts && MaxSimultaneousTweensCount > 50000) {
                warnBenchmarkWithAsserts = false;

                var msg =
                    "PrimeTween detected more than 50000 concurrent tweens. If you're running benchmarks, please add the PRIME_TWEEN_DISABLE_ASSERTIONS to the 'ProjectSettings/Player/Script Compilation' to disable assertions. This will ensure PrimeTween runs with the release performance.\n"
                    + "Also disable optional convenience features: PrimeTweenConfig.warnZeroDuration and PrimeTweenConfig.warnTweenOnDisabledTarget.\n";

                if (Application.isEditor) {
                    msg +=
                        "Please also run the tests in real builds, not in the Editor, to measure the performance correctly.\n";
                }

                msg +=
                    $"{Constants.BuildWarningCanBeDisabledMessage(nameof(PrimeTweenConfig.warnBenchmarkWithAsserts))}\n";

                Debug.LogError(msg);
            }
#endif

            // rt.print($"AddTween startValue: {tween.data.startValue}, endValue: {rt.endValueOrDiff}");
            return new Tween(tween);
        }

        internal static int ProcessAll(
            [CanBeNull] object onTarget,
            [NotNull] Predicate<ColdData> predicate,
            bool allowToProcessTweensInsideSequence
        ) {
            return Instance.ProcessAllInternal(onTarget, predicate, allowToProcessTweensInsideSequence);
        }

        internal static bool logCantManipulateError = true;

        private int ProcessAllInternal(
            [CanBeNull] object onTarget,
            [NotNull] Predicate<ColdData> predicate,
            bool allowToProcessTweensInsideSequence
        ) {
            int res = 0;
            Assert.IsNotNull(allTweenArrays);

            foreach (var arr in allTweenArrays) {
                updateDepth++;
                res += processInList(arr);
                updateDepth--;
            }

            return res;

            int processInList(TweenArray tweens) {
                int numProcessed = 0;
                int totalCount = 0;

                foreach (var el in tweens) {
                    ref TweenData rt = ref el.Tween;
                    ref UnmanagedTweenData d = ref el.Data;

                    if (rt.cold == null) {
                        continue;
                    }

                    totalCount++;

                    if (onTarget != null) {
                        if (rt.target != onTarget) {
                            continue;
                        }

                        if (!allowToProcessTweensInsideSequence && d.IsInSequence) {
                            // To support stopping sequences by target, I can add a new API 'Sequence.Create(object sequenceTarget)'.
                            // But 'sequenceTarget' is a different concept to tween's target, so I should not mix these two concepts:
                            //     'sequenceTarget' serves the purpose of unique 'id', while tween's target is the animated object.
                            // In my opinion, the benefits of this new API don't outweigh the added complexity. A much simpler approach is to store the Sequence reference and call sequence.Stop() directly.
                            Assert.IsFalse(d.IsMainSequenceRoot());

                            if (logCantManipulateError) {
                                rt.LogErrorWithStackTrace(Constants.kCantManipulateNested);
                            }

                            continue;
                        }
                    }

                    if (d.IsAlive && predicate(rt.cold)) {
                        numProcessed++;
                    }
                }

                if (onTarget == null) {
                    return totalCount;
                }

                return numProcessed;
            }
        }

        internal void SetTweensCapacity(int capacity) {
            var runningTweens = TweensCount;

            if (capacity < runningTweens) {
                Debug.LogError(
                    $"New capacity ({capacity}) should be greater than the number of currently running tweens ({runningTweens}).\n"
                    + $"You can use {nameof(Tween)}.{nameof(Tween.StopAll)}() to stop all running tweens."
                );

                return;
            }

            foreach (var arr in allTweenArrays) {
                arr.Capacity = capacity;
            }

            shakes.EnsureCapacity(
                capacity
            ); // p2 todo this is wasteful and shakes capacity can be lower than regular capacity?

            ResizeAndSetCapacity(pool, capacity - runningTweens, capacity);
            CurrentPoolCapacity = capacity;
            Assert.AreEqual(capacity, pool.Capacity);

            ResizeAndSetCapacity(coroutineIterators, capacity, capacity);
        }

        internal int TweensCount {
            get {
                int res = 0;

                foreach (var arr in allTweenArrays) {
                    res += arr.Count;
                }

                return res;
            }
        }

        internal static void ResizeAndSetCapacity<T>([NotNull] List<T> list, int newCount, int newCapacity)
            where T : new() {
            Assert.IsTrue(newCapacity >= newCount);
            int curCount = list.Count;

            if (curCount > newCount) {
                var numToRemove = curCount - newCount;
                list.RemoveRange(newCount, numToRemove);
                list.Capacity = newCapacity;
            } else {
                list.Capacity = newCapacity;

                if (newCount > curCount) {
                    var numToCreate = newCount - curCount;

                    for (int i = 0; i < numToCreate; i++) {
                        list.Add(new T());
                    }
                }
            }

            Assert.AreEqual(newCount, list.Count);
            Assert.AreEqual(newCapacity, list.Capacity);
        }

        [Conditional("UNITY_ASSERTIONS")]
        internal void WarnStructBoxingInCoroutineOnce(long id, [CanBeNull] ColdData tween) {
            if (!warnStructBoxingAllocationInCoroutine) {
                return;
            }

            warnStructBoxingAllocationInCoroutine = false;

            Assert.LogWarningWithStackTrace(
                "Please use Tween/Sequence."
                + nameof(Tween.ToYieldInstruction)
                + "() when waiting for a Tween/Sequence in coroutines to prevent struct boxing.\n"
                + Constants.BuildWarningCanBeDisabledMessage(
                    nameof(PrimeTweenConfig.warnStructBoxingAllocationInCoroutine)
                )
                + "\n",
                id,
                tween?.ManagedData.target
            );
        }

        internal static bool HasPrefabStage() {
#if UNITY_EDITOR
            return SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
#else
            return false;
#endif
        }

        private static bool IsInPrefabStage([CanBeNull] GameObject go) {
#if UNITY_EDITOR
            return go && SceneManagement.PrefabStageUtility.GetPrefabStage(go) != null;
#else
            return false;
#endif
        }
    }
}