using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using PrimeTween;
using UnityEditor;
using UnityEngine;
using Mathf = PrimeTween.Mathf;
using Data = PrimeTween.TweenAnimation.Data;
using TweenType = PrimeTween.TweenAnimation.TweenType;

[CustomEditor(typeof(TweenAnimationComponent)), CanEditMultipleObjects]
internal class TweenAnimationComponentEditor : Editor {
    private TweenAnimationPropDrawer m_PropDrawer;

    private void OnEnable() => m_PropDrawer = new TweenAnimationPropDrawer();

    public override void OnInspectorGUI() {
        float iniY = GUILayoutUtility.GetRect(0f, 0f).yMax;
        serializedObject.Update();
        var prop = serializedObject.GetIterator();

        using (new EditorGUI.DisabledScope(true)) {
            // draw script name
            prop.NextVisible(true);
            EditorGUILayout.PropertyField(prop);
        }

        prop = serializedObject.FindProperty(nameof(TweenAnimationComponent.animation));

        { // draw TweenAnimation without a foldout
            Rect pos = GUILayoutUtility.GetLastRect();
            float space = EditorGUIUtility.standardVerticalSpacing * 1f;
            pos.y += EditorGUIUtility.singleLineHeight + space;
            float contentHeight = m_PropDrawer.DrawContents(pos, prop, true);

            GUILayoutUtility.GetRect(
                0f,
                contentHeight + space
            ); // same as EditorGUILayout.Space but works in Unity 2018
        }

        using (new EditorGUI.DisabledScope(TweenAnimation.sIsPreviewing)) {
            GUILayoutUtility.GetRect(0f, EditorGUIUtility.standardVerticalSpacing * 4f);
            prop.NextVisible(nameof(TweenAnimationComponent.onEnable));
            EditorGUILayout.PropertyField(prop);
            prop.NextVisible(nameof(TweenAnimationComponent.onDisable));
            EditorGUILayout.PropertyField(prop);
            serializedObject.ApplyModifiedProperties();
        }

        Rect fullRect = GUILayoutUtility.GetLastRect();
        fullRect.height = fullRect.yMax - iniY;
        fullRect.y = iniY;
        var evt = Event.current;

        if (evt.type == EventType.MouseDown && fullRect.Contains(evt.mousePosition) && TweenAnimation.sIsPreviewing) {
            // Debug.Log("Reset from custom inspector");
            foreach (var t in targets) {
                var component = t as TweenAnimationComponent;

                if (component.animation != null && component.animation.CanManipulate()) {
                    component.animation.Reset();
                }
            }

            evt.Use();
        }
    }
}

[CustomPropertyDrawer(typeof(TweenAnimation))]
internal class TweenAnimationPropDrawer : PropertyDrawer {
    private readonly Dictionary<string, TweenAnimation[]> m_TargetsCache = new(1);
    private TweenAnimation[] m_Targets;
    private bool m_IsMouseDown;
    private bool m_WasPausedOnMouseDown;
    private GUIContent m_PropNameGuiContent;
    private GUIContent m_TriggerButtonGuiContent;
    private GUIContent m_StateButtonGuiContent;
    private GUIContent m_PauseButtonGuiContent;
    private GUIContent m_CycleModeGuiContent;
    private GUIContent m_InterruptionModeGuiContent;
    private GUIContent m_ResetOnCompletionGuiContent;

    private readonly Dictionary<(TweenType, UnityEngine.Object), TweenAnimation.ValueWrapper> m_EndValues = new();

    private GUIStyle m_TotalDurationStyle;
    private float m_CachedListHeight;
    private float m_PrevListHeight;

    [CanBeNull]
    internal static TweenAnimation sCurrentAnimation;

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label) {
        if (prop.isExpanded) {
            return DrawProperty(default, prop, false);
        }

        return EditorGUIUtility.singleLineHeight;
    }

    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label) {
        DrawProperty(pos, prop, true);

        Event evt = Event.current;

        if (evt.type == EventType.MouseDown && pos.Contains(evt.mousePosition)) {
            foreach (TweenAnimation x in m_Targets) {
                if (TweenAnimation.sIsPreviewing && x.CanManipulate()) {
                    // Debug.Log("Reset from property drawer");
                    x.Reset();
                    evt.Use();
                }
            }
        }
    }

    private float DrawControls(Rect pos, bool draw) {
        const float controlsHeight = 20;
        float result = controlsHeight + EditorGUIUtility.standardVerticalSpacing;

        if (!draw) {
            return result;
        }

        if (m_Targets.Length == 0) {
            EditorGUI.LabelField(pos, $"Target {nameof(TweenAnimation)} property not found.");
            return result;
        }

        Rect iniPos = pos;
        pos.x += EditorGUI.indentLevel * 16;
        pos.width = 32f;
        pos.height = controlsHeight;

        using (new EditorGUI.DisabledScope(m_Targets.Any(x => !x.CanManipulate()))) {
            void IgnoreRightMouseButtonClick() {
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 1
                    && pos.Contains(Event.current.mousePosition)) {
                    Event.current.Use();
                }
            }

            IgnoreRightMouseButtonClick();

            if (m_TriggerButtonGuiContent == null) {
                m_TriggerButtonGuiContent = EditorGUIUtility.IconContent("PlayButton");

                m_TriggerButtonGuiContent.tooltip =
                    "Triggers the animation. The resulting animation state depends on the animation type (simple, reversible, or infinite) and 'interruptionMode'.\n\n"
                    + "- Simple and infinite animations: plays the animation. If already playing, applies 'interruptionMode'\n\n"
                    + "- Reversible animations: toggles the direction.";
            }

            if (GUI.Button(pos, m_TriggerButtonGuiContent, EditorStyles.miniButton) && Event.current.button == 0) {
                bool wasPaused = PrimeTweenManager.sIsInspectorScrubbingPaused;
                PrimeTweenManager.sIsInspectorScrubbingPaused = false;

                foreach (var target in m_Targets) {
                    if (wasPaused) {
                        target.isPaused = false;
                    }

                    target.Trigger();
                }

                return result;
            }

            pos.x += pos.width;

            IgnoreRightMouseButtonClick();

            if (m_PauseButtonGuiContent == null) {
                m_PauseButtonGuiContent = EditorGUIUtility.IconContent("PauseButton");
                m_PauseButtonGuiContent.tooltip = "Pause";
            }

            if (PrimeTweenManager.EnteredEditMode) {
                if (GUI.Toggle(
                        pos,
                        PrimeTweenManager.sIsInspectorScrubbingPaused,
                        m_PauseButtonGuiContent,
                        EditorStyles.miniButton
                    )
                    != PrimeTweenManager.sIsInspectorScrubbingPaused
                    && Event.current.button == 0) {
                    PrimeTweenManager.sIsInspectorScrubbingPaused = !PrimeTweenManager.sIsInspectorScrubbingPaused;

                    foreach (var target in m_Targets) {
                        target.isPaused = PrimeTweenManager.sIsInspectorScrubbingPaused;
                    }
                }
            } else {
                bool currentIsPaused = m_Targets.Any(x => x.isPaused);

                if (GUI.Toggle(pos, currentIsPaused, m_PauseButtonGuiContent, EditorStyles.miniButton)
                    != currentIsPaused
                    && Event.current.button == 0) {
                    foreach (var target in m_Targets) {
                        target.isPaused = !currentIsPaused;
                    }
                }
            }

            pos.x += pos.width;

            IgnoreRightMouseButtonClick();

            if (m_StateButtonGuiContent == null) {
                m_StateButtonGuiContent =
                    EditorGUIUtility.IconContent("Packages/com.kyrylokuzyk.primetween/Editor/Icons/state.png");

                m_StateButtonGuiContent.tooltip = "Toggle animation state.\n\n"
                                                  + "- Simple animations: state is 'true' if an animation is currently playing.\n\n"
                                                  + "- Infinite animations: state is 'true' if an animation is currently playing and NOT being interrupted by 'interruptionMode'\n\n"
                                                  + "- Reversible animations ('isReversible' == true): state is 'true' if the animation is moving forward OR already at the end. Changing state changes animation direction.";
            }

            bool curState = m_Targets[0].state;

            if (GUI.Toggle(pos, curState, m_StateButtonGuiContent, EditorStyles.miniButton) != curState
                && Event.current.button == 0) {
                bool wasPaused = PrimeTweenManager.sIsInspectorScrubbingPaused;
                PrimeTweenManager.sIsInspectorScrubbingPaused = false;

                foreach (var target in m_Targets) {
                    if (wasPaused) {
                        target.isPaused = false;
                    }

                    target.state = !curState;
                }
            }

            pos.x += pos.width + 2;

            float rightEdge = iniPos.x + iniPos.width;
            pos.width = rightEdge - pos.x - 6f;

            Event evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (evt.GetTypeForControl(id)) {
                case EventType.MouseDown:
                    if (evt.button == 0) {
                        if (pos.Contains(evt.mousePosition)) {
                            m_WasPausedOnMouseDown = PrimeTweenManager.EnteredEditMode
                                ? PrimeTweenManager.sIsInspectorScrubbingPaused : m_Targets.Any(x => x.isPaused);

                            foreach (var target in m_Targets) {
                                target.isPaused = true;
                            }

                            UpdateProgress();
                            m_IsMouseDown = true;
                            GUIUtility.hotControl = id;
                            evt.Use();
                        }
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && m_IsMouseDown) {
                        UpdateProgress();
                        GUI.changed = true;
                        evt.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && m_IsMouseDown) {
                        foreach (var target in m_Targets) {
                            if (GetScrubbingProgress() < 0.01f) {
                                target.Reset(); // Reset instead of Stop to release potential MaterialPropertyBlock overrides on Renderer
                            } else {
                                target.isPaused = m_WasPausedOnMouseDown;
                            }
                        }

                        m_IsMouseDown = false;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }

                    break;
                case EventType.Repaint:
                    EditorGUIUtility.AddCursorRect(pos, MouseCursor.SlideArrow);

                    pos = new Rect(pos.x, pos.y + 0, pos.width, pos.height - 2);
                    float color = (EditorGUIUtility.isProSkin ? 20f : 90f) / 255f;
                    EditorGUI.DrawRect(pos, new Color(color, color, color, 1f));

                    var firstTarget = m_Targets[0];
                    float progress;

                    if (firstTarget.isAlive) {
                        if (firstTarget.IsInfinite()) {
                            progress = firstTarget.progress;

                            if (firstTarget.cycleMode != Sequence.SequenceCycleMode.Restart
                                && firstTarget.sequence.cyclesDone % 2 != 0) {
                                progress = 1f - progress;
                            }
                        } else {
                            progress = firstTarget.progressTotal;
                        }
                    } else {
                        progress = firstTarget.state ? 1f : 0f;
                    }

                    EditorGUI.DrawRect(new Rect(pos.x + pos.width * progress, pos.y, 2f, pos.height), Color.white);

                    if (sCurrentAnimation?.isAlive == true) {
                        GUI.changed = true;
                    }

                    // Draw total duration on the right side of the slider
                    if (m_TotalDurationStyle == null) {
                        m_TotalDurationStyle = new GUIStyle(EditorStyles.boldLabel) {
                            alignment = TextAnchor.MiddleRight,
                            clipping = TextClipping.Overflow,
                            normal = new GUIStyleState { textColor = Color.white, },
                            hover = new GUIStyleState { textColor = Color.white, },
                        };
                    }

                    var durationLabelRect = new Rect(pos.xMax - 44f, pos.y, 40f, pos.height);

                    if (PrimeTweenManager.HasInstance || PrimeTweenManager.EnteredEditMode) {
                        GUI.Label(
                            durationLabelRect,
                            $"{PrimeTweenManager.Instance.currentAnimationDurationOrZero:0.0#}s",
                            m_TotalDurationStyle
                        );
                    }

                    break;
            }

            void UpdateProgress() {
                float progress = GetScrubbingProgress();

                foreach (var target in m_Targets) {
                    if (target.cycles == -1) {
                        target.PlayIfNotAlive();
                        target.progress = Mathf.Min(progress, 0.999f);
                    } else {
                        target.SetProgressTotal(progress, false);
                    }
                }
            }

            float GetScrubbingProgress() => Mathf.Clamp01((evt.mousePosition.x - pos.x + 1f) / pos.width);
        }

        return result;
    }

    internal static float ProcessAnimationDataAndReturnTotalDuration(
        TweenAnimation animation,
        [CanBeNull] ref List<PrimeTweenManager.CurrentAnimationData> outResult
    ) {
        var animations = animation.animations;

        if (outResult != null) {
            outResult.Clear();

            for (int i = 0; i < animations.Count; i++) {
                outResult.Add(default);
            }
        }

        float startTime = 0f;
        float totalDuration = 0f;
        float defaultProgressTotal = !animation.isAlive && animation.state ? 1f : 0f;

        for (int i = 0; i < animations.Count; i++) {
            Data data = animations[i];

            switch (data.operation) {
                case TweenAnimation.Operation.Disabled:
                    break;
                case TweenAnimation.Operation.Insert:
                    startTime = data.startTime;
                    break;
                case TweenAnimation.Operation.Chain:
                    startTime = totalDuration;
                    break;
                case TweenAnimation.Operation.Group:
                    break;
                default:
                    throw new Exception();
            }

            float duration = 0f;

            switch (data.tweenType) {
                case TweenType.TweenAnimationComponent:
                    if (data.targets?.FirstOrDefault() is TweenAnimationComponent animationComponent
                        && animationComponent.animation is TweenAnimation childAnimation
                        && childAnimation != animation) {
                        Assert.AreNotEqual(childAnimation, animation);
                        List<PrimeTweenManager.CurrentAnimationData> nullData = null;
                        duration = ProcessAnimationDataAndReturnTotalDuration(childAnimation, ref nullData);
                    }

                    break;
                case TweenType.Disabled:
                case TweenType.Callback:
                    break;
                default:
                    duration = data.duration;
                    break;
            }

            if (Utils.CanHaveCycles(data.tweenType)) {
                duration *= Math.Max(1, data.cycles);
            }

            if (outResult != null) {
                outResult[i] = new PrimeTweenManager.CurrentAnimationData
                    { startTime = startTime, duration = duration, progressTotal = defaultProgressTotal };
            }

            totalDuration = Math.Max(totalDuration, startTime + duration);
        }

        if (outResult != null) {
            if (animation.isAlive) {
                foreach (var child in animation.sequence.GetSelfChildren()) {
                    int i = child.indexInTweenAnimation;

                    if (i >= 0) {
                        // when resetOnCompletion is enabled, TweenAnimation adds ChainCallback to restore the state on completion but doesn't set the indexInTweenAnimation
                        if (i < outResult.Count) {
                            var data = outResult[i];
                            data.progressTotal = new Tween(child).progressTotal;
                            outResult[i] = data;
                        }
                    }
                }
            }
        }

        return totalDuration;
    }

    internal static bool FindSelfOrCircularReference(
        TweenAnimationComponent current,
        TweenAnimationComponent origin,
        int depth = 0
    ) {
        if (depth > 32) {
            return true;
        }

        var animations = current.animation?.animations;

        if (animations != null) {
            foreach (var data in animations) {
                if (data.tweenType == TweenType.TweenAnimationComponent
                    && data.targets?.FirstOrDefault() is TweenAnimationComponent child) {
                    if (child == origin) {
                        return true;
                    }

                    if (FindSelfOrCircularReference(child, origin, depth + 1)) {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    internal float DrawContents(Rect pos, SerializedProperty prop, bool draw) {
        string propertyPath = prop.propertyPath;

        if (!m_TargetsCache.TryGetValue(propertyPath, out m_Targets)) {
            m_Targets = prop.GetParentObjects<TweenAnimation>();

            // Debug.Log($"{m_Targets.Length} {string.Join(", ", m_Targets.Select(x => x.GetHashCode()))} this:{GetHashCode()}");
            m_TargetsCache.Add(propertyPath, m_Targets);
        }

        if ((PrimeTweenManager.HasInstance || PrimeTweenManager.EnteredEditMode)
            && Event.current.type == EventType.Repaint
           ) {
            TweenAnimation firstAnimation = m_Targets.FirstOrDefault();
            var manager = PrimeTweenManager.Instance;
            sCurrentAnimation = firstAnimation;

            if (firstAnimation != null) {
                ref var currentAnimationData = ref manager.currentAnimationData;
                Assert.IsNotNull(currentAnimationData);

                manager.currentAnimationDurationOrZero =
                    ProcessAnimationDataAndReturnTotalDuration(firstAnimation, ref currentAnimationData);
            } else {
                manager.currentAnimationDurationOrZero = 0f;
            }
        }

        float height = 0f;
        float controlsHeight = DrawControls(pos, draw);
        AddHeight(controlsHeight);

        using (new EditorGUI.DisabledScope(TweenAnimation.sIsPreviewing || m_Targets.Any(x => !x.CanManipulate()))) {
            prop.NextVisible(nameof(TweenAnimation.animations), enterChildren: true);

            if (draw && Event.current.type == EventType.Repaint) {
                SaveStartValues(prop);
            }

            {
                // Cache height only when EditorGUI.GetPropertyHeight() returns two same values in a row. This prevents one-frame flickering caused by the ReorderableList height cache bug when the tooltip appears/disappears over fields in inspector.
                float listHeight = EditorGUI.GetPropertyHeight(prop, true);

                if (m_TargetsCache.Count <= 1) {
                    if (m_CachedListHeight == 0f || listHeight == m_PrevListHeight) {
                        m_CachedListHeight = listHeight;
                    } /*else {
                        Debug.Log($"discard new height to prevent flickering {sCurrentAnimation.GetHashCode()} {m_Targets.Length} {prop.propertyPath} listHeight:{listHeight}");
                    }*/

                    m_PrevListHeight = listHeight;
                    listHeight = m_CachedListHeight;
                }

                pos.height = listHeight;

                if (draw) {
                    EditorGUI.PropertyField(pos, prop, null, true);
                }

                AddHeight(listHeight + EditorGUIUtility.standardVerticalSpacing);
            }

            NextPropertyField(nameof(TweenAnimation.isReversible));
            bool isReversible = prop.boolValue;

            NextProperty(nameof(TweenAnimation.interruptionMode));
            var copy = prop.Copy();
            copy.NextVisible(nameof(TweenAnimation.cycles));
            int cycles = copy.intValue;

            if (draw) {
                if (m_InterruptionModeGuiContent == null) {
                    m_InterruptionModeGuiContent = new GUIContent(
                        ObjectNames.NicifyVariableName(nameof(TweenAnimation.interruptionMode))
                    );
                }

                using (var scope = new CustomPropertyScope(pos, m_InterruptionModeGuiContent, prop)) {
                    int newVal = (int)(TweenAnimation.InterruptionMode)EditorGUI.EnumPopup(
                        pos,
                        scope.content,
                        (TweenAnimation.InterruptionMode)prop.intValue,
                        x => {
                            var mode = (TweenAnimation.InterruptionMode)x;

                            if (cycles == -1) {
                                if (mode == TweenAnimation.InterruptionMode.Complete) {
                                    return false;
                                }
                            } else if (isReversible) {
                                if (mode == TweenAnimation.InterruptionMode.Reset) {
                                    return false;
                                }
                            }

                            return true;
                        }
                    );

                    if (scope.EndChangeCheck()) {
                        prop.intValue = newVal;
                    } else {
                        var interruptionMode = (TweenAnimation.InterruptionMode)prop.intValue;

                        if (TweenAnimation.SanitiseInterruptionMode(isReversible, cycles, ref interruptionMode)) {
                            prop.SetIntChecked((int)interruptionMode);
                        }
                    }
                }
            }

            AddSingleLineHeight();

            NextPropertyField(nameof(TweenAnimation.cycles));
            cycles = prop.intValue;

            if (TweenAnimation.SanitiseCycles(isReversible, ref cycles)) {
                prop.SetIntChecked(cycles);
            }

            NextProperty(nameof(TweenAnimation.cycleMode));

            if (draw) {
                if (m_CycleModeGuiContent == null) {
                    m_CycleModeGuiContent =
                        new GUIContent(ObjectNames.NicifyVariableName(nameof(TweenAnimation.cycleMode)));
                }

                if (cycles != 1 || isReversible) {
                    using (var scope = new CustomPropertyScope(pos, m_CycleModeGuiContent, prop)) {
                        int newVal = (int)(Sequence.SequenceCycleMode)EditorGUI.EnumPopup(
                            pos,
                            scope.content,
                            (Sequence.SequenceCycleMode)prop.intValue,
                            x => {
                                if (isReversible
                                    && (Sequence.SequenceCycleMode)x == Sequence.SequenceCycleMode.Restart) {
                                    return false;
                                }

                                return true;
                            }
                        );

                        if (scope.EndChangeCheck()) {
                            prop.intValue = newVal;
                        } else {
                            var cycleMode = (Sequence.SequenceCycleMode)prop.intValue;

                            if (TweenAnimation.SanitiseCycleMode(isReversible, ref cycleMode)) {
                                prop.SetIntChecked((int)cycleMode);
                            }
                        }
                    }
                } else {
                    using (new EditorGUI.DisabledScope(true)) {
                        EditorGUI.LabelField(pos, m_CycleModeGuiContent);
                    }
                }
            }

            AddSingleLineHeight();

            AddHeight(EditorGUIUtility.standardVerticalSpacing * 4);
            NextPropertyField(nameof(TweenAnimation.ignoreDuplicateTrigger));

            NextProperty(nameof(TweenAnimation.resetOnCompletion));

            if (draw) {
                if (m_ResetOnCompletionGuiContent == null) {
                    m_ResetOnCompletionGuiContent = new GUIContent(
                        ObjectNames.NicifyVariableName(nameof(TweenAnimation.resetOnCompletion))
                    );
                }

                bool canResetOnCompletion = m_Targets.Count(x => x.CanResetOnComplete()) == m_Targets.Length;

                if (canResetOnCompletion) {
                    EditorGUI.PropertyField(pos, prop, m_ResetOnCompletionGuiContent);
                } else {
                    using (new EditorGUI.DisabledScope(true)) {
                        EditorGUI.LabelField(pos, m_ResetOnCompletionGuiContent);
                    }
                }
            }

            AddSingleLineHeight();

            NextPropertyField(nameof(TweenAnimation.sequenceEase));

            if (prop.intValue == (int)Ease.Custom && prop.SetIntChecked((int)Ease.Linear)) {
                Debug.LogWarning(
                    $"{ObjectNames.NicifyVariableName(nameof(TweenAnimation.sequenceEase))} doesn't support Ease.{nameof(Ease.Custom)}, setting to Ease.{nameof(Ease.Linear)}."
                );
            }

            NextPropertyField(nameof(TweenAnimation.useUnscaledTime));
            NextPropertyField(nameof(TweenAnimation._updateType));
            NextPropertyField(nameof(TweenAnimation._timeScale));

            if (draw) {
                TweenSettingsPropDrawer.ClampProperty(prop, 1f, TweenAnimation.kMinTimeScale);
            }

            prop.Next(nameof(TweenAnimation.context));

            if (!prop.serializedObject.isEditingMultipleObjects) {
                prop.SetObjectReferenceChecked(prop.serializedObject.targetObject);
            }
        }

        return height;

        void NextPropertyField(string expectedName) {
            NextProperty(expectedName);
            PropertyField();
        }

        void NextProperty(string expectedName, bool enterChildren = false) {
            prop.NextVisible(expectedName, enterChildren);
        }

        void PropertyField(GUIContent guiContent = null, bool includeChildren = false, bool isMultiLine = false) {
            float propertyHeight = isMultiLine ? EditorGUI.GetPropertyHeight(prop, includeChildren)
                : EditorGUIUtility.singleLineHeight;

            pos.height = propertyHeight;

            if (draw) {
                EditorGUI.PropertyField(pos, prop, guiContent, includeChildren);
            }

            AddHeight(propertyHeight + EditorGUIUtility.standardVerticalSpacing);
        }

        void AddHeight(float h) {
            pos.y += h;
            height += h;
        }

        void AddSingleLineHeight() =>
            AddHeight(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
    }

    /// Save start values.
    /// If there is a previous animation with the same target and tween type, use endValue value of that animation as the startValue.
    /// Else, read the current value from getter and save it to startValue.
    private void SaveStartValues(SerializedProperty prop) {
        // p0 todo looping motion should take the start value instead of end value
        // Populate startValue automatically only in Edit Mode. But at runtime, always use the saved value from Edit Mode.
        if (PrimeTweenManager.EnteredEditMode
            && !TweenAnimation.sIsPreviewing
           ) {
            m_EndValues.Clear();

            for (int i = 0; i < prop.arraySize; i++) {
                var el = prop.GetArrayElementAtIndex(i);

                if (!string.IsNullOrEmpty(TweenAnimation.sEndValueHighlightData.propertyPath)
                    && el.propertyPath == TweenAnimation.sEndValueHighlightData.propertyPath) {
                    // Debug.Log($"skip SaveStartValue {TweenAnimation._endValueHighlightData.propertyPath}");
                    continue;
                }

                el.NextVisible(nameof(Data.operation), true);

                if (el.hasMultipleDifferentValues) {
                    continue;
                }

                TweenAnimation.Operation operation = (TweenAnimation.Operation)el.intValue;

                if (operation == TweenAnimation.Operation.Disabled) {
                    continue;
                }

                el.NextVisible(nameof(Data.startTime));
                el.NextVisible(nameof(Data.tweenType));

                if (el.hasMultipleDifferentValues) {
                    continue;
                }

                TweenType tweenType = (TweenType)el.intValue;

                if (Data.IsCustomTweenType(tweenType)
                    || tweenType == TweenType.Disabled
                    || Utils.IsShake(tweenType)
                    || tweenType == TweenType.TweenAnimationComponent
                   ) {
                    continue;
                }

                el.NextVisible(nameof(Data.targets));
                UnityEngine.Object target = el.arraySize > 0 ? el.GetArrayElementAtIndex(0).objectReferenceValue : null;
                Type targetType;

                try {
                    targetType = Utils.TweenTypeToTweenData(tweenType).Item2;
                } catch {
                    return;
                }

                if (tweenType != TweenType.GlobalTimeScale) {
                    if (target == null || targetType == null || !targetType.IsAssignableFrom(target.GetType())) {
                        continue;
                    }
                }

                el.NextVisible(nameof(Data.stringParam));
                string stringParam = el.stringValue;
                el.NextVisible(nameof(Data._customData));
                el.NextVisible(nameof(Data.boolParam));

                if (el.hasMultipleDifferentValues) {
                    continue;
                }

                bool hasStartValue = el.boolValue;
                el.NextVisible(nameof(Data.startValue));

                TweenAnimation.ValueWrapper? startValue = null;

                if (!hasStartValue) {
                    if (m_EndValues.TryGetValue((tweenType, target), out var prevEndValue)) {
                        if (operation == TweenAnimation.Operation.Chain) {
                            startValue = prevEndValue;
                        }
                    } else {
                        startValue = TweenAnimation.GetCurrentValue(operation, target, tweenType, stringParam);
                    }
                }

                if (startValue.HasValue) {
                    el.NextVisible(nameof(TweenAnimation.ValueWrapper.x), enterChildren: true);

                    for (int j = 0; j < 4; j++) {
                        if (j != 0) {
                            el.NextVisible(false);
                        }

                        float newVal = (float)Math.Round(startValue.Value[j], 3, MidpointRounding.AwayFromZero);

                        if (el.floatValue != newVal) {
                            // Debug.LogWarning($"{tweenType} start value changed at index {j} from {el.floatValue} to {newVal}");
                            el.floatValue = newVal;
                        }
                    }
                }

                el.NextVisible(nameof(Data.endValue));
                el.NextVisible(nameof(TweenAnimation.ValueWrapper.x), enterChildren: true);
                TweenAnimation.ValueWrapper endValue = default;

                for (int j = 0; j < 4; j++) {
                    endValue[j] = el.floatValue;
                    el.NextVisible(false);
                }

                m_EndValues[(tweenType, target)] = endValue;
            }
        }
    }

    private float DrawProperty(Rect pos, SerializedProperty prop, bool draw) {
        float height = 0f;

        if (m_PropNameGuiContent == null) {
            m_PropNameGuiContent = new GUIContent(prop.displayName);
        }

        PropertyField(m_PropNameGuiContent);

        if (prop.isExpanded) {
            EditorGUI.indentLevel++;
            float contentsHeight = DrawContents(pos, prop, draw);
            AddHeight(contentsHeight);
            EditorGUI.indentLevel--;
        }

        return height;

        void PropertyField(GUIContent guiContent = null) {
            float propertyHeight = EditorGUIUtility.singleLineHeight;
            pos.height = propertyHeight;

            if (draw) {
                EditorGUI.PropertyField(pos, prop, guiContent);
            }

            AddHeight(propertyHeight + EditorGUIUtility.standardVerticalSpacing);
        }

        void AddHeight(float h) {
            pos.y += h;
            height += h;
        }
    }
}

/// https://discussions.unity.com/t/a-simple-way-to-access-the-class-from-the-property-drawer/900422/9
internal static class SerializedPropertyUtils {
    private const BindingFlags k_Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Retrieves the parent as an object from the specified SerializedProperty.
    /// </summary>
    /// <param name="prop">The SerializedProperty to get the parent object from.</param>
    /// <returns>
    /// The parent object of the specified SerializedProperty.
    /// </returns>
    [NotNull]
    internal static T[] GetParentObjects<T>(this SerializedProperty prop) where T : class {
        // Debug.Log($"GetParentObjects");
        return prop.serializedObject.targetObjects
                   .Select(targetObject => {
                           string propertyPath = prop.propertyPath;
                           object parent = GetObjectFromPropertyPath(propertyPath, targetObject);
                           var context = targetObject;

                           if (parent == null || !(parent is T)) {
                               Debug.LogError(
                                   $"parent not found {propertyPath} {prop} {context.name} {context.GetInstanceID()}"
                               );
                           } else {
                               // Debug.Log($"parent found {propertyPath} {prop} {context.name} instanceId:{context.GetInstanceID()}, hashCode:{parent.GetHashCode()}");
                           }

                           return parent as T;
                       }
                   )
                   .Where(parent => parent != null)
                   .ToArray();
    }

    private static object GetObjectFromPropertyPath(this string propertyPath, object root) {
        var elements = propertyPath.Replace(".Array.data[", "[").Split('.');
        var obj = root;

        foreach (var e in elements) {
            if (e.Contains("[")) {
                // handle arrays
                var eName = e.Substring(0, e.IndexOf("[", StringComparison.Ordinal));

                if (e.IndexOf("[", StringComparison.Ordinal) < 0 || e.IndexOf("[", StringComparison.Ordinal) > e.Length)
                    continue;

                int i = Convert.ToInt32(
                    e.Substring(e.IndexOf("[", StringComparison.Ordinal)).Replace("[", "").Replace("]", "")
                );

                obj = GetMemberValue(obj, eName);

                if (obj is IList l && i < l.Count) {
                    obj = l[i];
                } else {
                    return null;
                }
            } else {
                obj = GetMemberValue(obj, e);

                if (obj == null)
                    return null;
            }
        }

        return obj;
    }

    private static object GetMemberValue(object source, string name) {
        if (source == null) {
            return null;
        }

        var type = source.GetType();

        var f = type.GetField(name, k_Flags);

        if (f != null) {
            return f.GetValue(source);
        }

        var p = type.GetProperty(name, k_Flags);
        return p != null ? p.GetValue(source, null) : null;
    }
}

[CustomPropertyDrawer(typeof(Data))]
internal class TweenAnimationDataPropDrawer : PropertyDrawer {
    internal const int kNumEnums = 131;

    private static GUIContent[] s_EnumNamesSorted;
    private static (int sortedIndexToEnum, int enumToSortedIndex)[] s_EnumIndexMappings;

    private readonly GUIContent m_OnValueChangeGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ColdData.onValueChange)));

    private readonly GUIContent m_MaterialPropertyNameGuiContent = new("Property Name");
    private readonly GUIContent m_TweenTypeGuiContent = new(ObjectNames.NicifyVariableName(nameof(Data.tweenType)));
    private readonly GUIContent m_TargetGuiContent = new("Target");
    private readonly GUIContent m_TargetsGuiContent = new(ObjectNames.NicifyVariableName(nameof(Data.targets)));
    private readonly GUIContent m_EmptyLabel = new(" ");
    private readonly GUIContent m_StrengthFactorGuiContent = new("Strength Factor");
    private readonly GUIContent m_WarnIfTargetDestroyedGuiContent = new("Warn If Target Destroyed");

    private readonly GUIContent[] m_XYZLabels = {
        new("X"),
        new("Y"),
        new("Z")
    };

    private readonly GUIContent m_StartValueGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(TweenSettings<float>.startValue)));

    private readonly GUIContent m_EndValueGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(TweenSettings<float>.endValue)));

    private readonly GUIContent m_ShakeStrengthGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ShakeSettings.strength)));

    private readonly GUIContent m_ShakeFrequencyGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ShakeSettings.frequency)));

    private readonly GUIContent m_ShakeEnableFalloffGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ShakeSettings.enableFalloff)));

    private readonly GUIContent m_ShakeFalloffEaseGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ShakeSettings.falloffEase)));

    private readonly GUIContent m_ShakeAsymmetryGuiContent =
        new(ObjectNames.NicifyVariableName(nameof(ShakeSettings.asymmetry)));

    private readonly StringBuilder m_Sb = new();
    private readonly StringBuilder m_EventNamesSb = new();
    private bool m_DidCheckCircularReference;
    private readonly List<UnityEngine.Object> m_Targets = new(); // p0 todo rename _filteredTargetsCache

    private const float k_ExtraCellSpacing = 6;

    public override float GetPropertyHeight([NotNull] SerializedProperty prop, GUIContent label) {
        bool isExpanded = prop.isExpanded;
        float height = DrawProperty(default, prop, false); // always call DrawProperty to update headers
        return isExpanded ? height : EditorGUIUtility.singleLineHeight + k_ExtraCellSpacing;
    }

    public override void OnGUI(Rect pos, [NotNull] SerializedProperty prop, GUIContent propLabel) {
        DrawProperty(pos, prop, true);
    }

    private float DrawProperty(Rect pos, SerializedProperty prop, bool draw) {
        try {
            return DrawPropertyInternal(pos, prop, draw);
        } catch (ExitGUIException) {
            throw;
        } catch (Exception e) {
            Debug.LogException(e);
            throw;
        }
    }

    private float DrawPropertyInternal(Rect pos, SerializedProperty prop, bool draw) {
        if (draw && Event.current.type == EventType.Repaint) {
            Rect bg = pos;
            bg.x -= 12f;
            bg.width += 18f;
            bg.y -= 1f;
            float backgroundColor = (EditorGUIUtility.isProSkin ? 65f : 200f) / 255;
            EditorGUI.DrawRect(bg, new Color(backgroundColor, backgroundColor, backgroundColor, 1f));
        }

        pos.height = EditorGUIUtility.singleLineHeight;
        float height = k_ExtraCellSpacing;

        // progress bar
        int arrayIndex = GetArrayIndex(prop.propertyPath);

        if (draw
            && arrayIndex != -1
            && (PrimeTweenManager.HasInstance || PrimeTweenManager.EnteredEditMode)
            && Event.current.type == EventType.Repaint
           ) {
            var manager = PrimeTweenManager.Instance;
            var currentAnimationData = manager.currentAnimationData;
            float durationTotal = manager.currentAnimationDurationOrZero;

            if (durationTotal != 0f && arrayIndex < currentAnimationData.Count) {
                Rect r = pos;
                const float leftPadding = 37f;
                r.x += leftPadding;
                r.width -= leftPadding;
                r.y += 18f;
                r.height = 2.0f;

                float color = EditorGUIUtility.isProSkin ? 0.16f : 0.7f;
                EditorGUI.DrawRect(r, new Color(color, color, color, 1f));

                var animationData = currentAnimationData[arrayIndex];
                float startTime = animationData.startTime;
                r.x += r.width * (startTime / durationTotal);
                r.width = Mathf.Max(6, r.width * (animationData.duration / durationTotal));
                color = EditorGUIUtility.isProSkin ? 0.5f : 0.35f;
                EditorGUI.DrawRect(r, new Color(color, color, color, 1f));

                r.width *= animationData.progressTotal;
                EditorGUI.DrawRect(r, Color.white);
            }
        }

        // header
        TweenAnimation.HeaderData header = default;
        var currentAnimation = TweenAnimationPropDrawer.sCurrentAnimation;

        if (arrayIndex != -1) {
            if (currentAnimation != null) {
                ref var headers = ref currentAnimation.headers;

                if (headers == null || headers.Length < arrayIndex + 1) {
                    int newSize = Math.Max(headers?.Length * 2 ?? 8, arrayIndex + 1);
                    Array.Resize(ref headers, newSize);
                }

                header = headers[arrayIndex];
            }
        }

        if (draw) {
            EditorGUI.PropertyField(pos, prop, header.guiContent);
        }

        MoveToNextLine();
        float? foldedHeight = null;

        if (!prop.isExpanded) {
            draw = false;
            foldedHeight = height;
        }

        pos.y += k_ExtraCellSpacing;
        pos.x += 15; // using EditorGUI.indentLevel breaks the toggle in ValueContainerStartEndPropDrawer
        pos.width -= 15;

        Next(nameof(Data.operation), true);
        var operation = (TweenAnimation.Operation)prop.intValue;
        bool isInsert = operation == TweenAnimation.Operation.Insert;
        PropertyField();

        using (new EditorGUI.DisabledScope(operation == TweenAnimation.Operation.Disabled)) {
            Next(nameof(Data.startTime));
            float insertionTime = 0f;

            if (isInsert) {
                PropertyField();

                if (draw && prop.floatValue < 0f) {
                    prop.SetFloatChecked(0f);
                }

                insertionTime = prop.floatValue;
            }

            Next(nameof(Data.tweenType));

            if (s_EnumNamesSorted == null) {
                TweenType[] enumValues = Enum.GetValues(typeof(TweenType)) as TweenType[];
                int count = enumValues.Length;

                Assert.IsTrue(
                    count <= kNumEnums
                ); // CustomDouble is gated by the experimental define, so enumValues can be less than numEnums

                var tempEnums = new (int enumValue, GUIContent name)[count];
                s_EnumNamesSorted = new GUIContent[count];
                s_EnumIndexMappings = new (int sortedIndexToEnum, int enumToSortedIndex)[kNumEnums];

                string[] prefixes = {
                    "Text", "UI", "Camera", "Rigidbody", "Light", "Audio", "MaterialPropertyBlock", "Material",
                    "Custom", "Shake", "LocalEulerAngles", "LocalPosition", "Position", "Scale"
                };

                for (int i = 0; i < count; i++) {
                    TweenType enumValue = enumValues[i];
                    TweenType filtered = Filter(enumValue);
                    string tooltip = null;

                    if (Utils.TweenTypeToTweenData(filtered).Item2 == typeof(Renderer)) {
                        tooltip = Constants.kMaterialPropBlockTooltip;
                    }

                    tempEnums[i] = ((int)enumValue, name: new GUIContent(GetEnumName(filtered), tooltip));

                    TweenType Filter(TweenType x) {
                        (PropType, Type) tweenTypeData;

                        try {
                            tweenTypeData = Utils.TweenTypeToTweenData(x);
                        } catch {
                            return TweenType.Disabled;
                        }
#if UI_ELEMENTS_MODULE_INSTALLED
                        if (typeof(UnityEngine.UIElements.ITransform).IsAssignableFrom(tweenTypeData.Item2)) {
                            return TweenType.Disabled;
                        }
#endif
                        switch (x) {
                            case TweenType.ShakeLocalRotation:
                                return x;
                            case TweenType.MainSequence:
                            case TweenType.NestedSequence:
                            case TweenType.TweenTimeScale:
                            case TweenType.TweenTimeScaleSequence:
                            case TweenType.TweenAwaiter:
                            // Animating Time.timeScale requires 'useUnscaledTime' to animate it correctly. But this makes TweenAnimation too confusing to use: it's no longer allowed to nest such animations without propagating the useUnscaledTime to all parent TweenAnimationComponents.
                            case TweenType.GlobalTimeScale:
                                return TweenType.Disabled;
                        }

                        if (tweenTypeData.Item1 == PropType.Quaternion || tweenTypeData.Item1 == PropType.Double) {
                            return TweenType.Disabled;
                        }

                        return x;
                    }

                    string GetEnumName(TweenType x) {
                        switch (x) {
                            case TweenType.ColorSpriteRenderer:
                                return "Color/SpriteRenderer";
                            case TweenType.UIColorGraphic:
                                return "Color/Graphic (Image, Text, RawImage)";
                            case TweenType.UIColorShadow:
                                return "Color/Shadow (Outline)";

                            case TweenType.AlphaSpriteRenderer:
                                return "Alpha/SpriteRenderer";
                            case TweenType.UIAlphaGraphic:
                                return "Alpha/Graphic (Image, Text, RawImage)";
                            case TweenType.UIAlphaShadow:
                                return "Alpha/Shadow (Outline)";
                            case TweenType.UIAlphaCanvasGroup:
                                return "Alpha/CanvasGroup";
                        }

                        string enumStr = x.ToString();
                        m_Sb.Clear();

                        foreach (string prefix in prefixes) {
                            if (enumStr.StartsWith(prefix, StringComparison.Ordinal)) {
                                int prefixLength = prefix.Length;
                                m_Sb.Append(prefix);

                                if (enumStr != prefix) {
                                    m_Sb.Append('/');

                                    if (enumStr.Contains("Graphic")) {
                                        m_Sb.Append(enumStr, prefixLength, enumStr.Length - prefixLength)
                                            .Append(" (Image, Text, RawImage)");
                                    } else if (x == TweenType.UIEffectDistance) {
                                        m_Sb.Append(enumStr, prefixLength, enumStr.Length - prefixLength)
                                            .Append(" (Shadow, Outline)");
                                    } else {
                                        m_Sb.Append(enumStr, prefixLength, enumStr.Length - prefixLength);
                                    }
                                }

                                return m_Sb.ToString();
                            }
                        }

                        return enumStr;
                    }
                }

                Array.Sort(
                    tempEnums,
                    (a, b) => {
                        if (a.enumValue == (int)TweenType.Disabled)
                            return -1; // Disabled always first

                        if (b.enumValue == (int)TweenType.Disabled)
                            return 1;

                        return string.Compare(a.name.text, b.name.text, StringComparison.Ordinal);
                    }
                );

                for (int i = 0; i < count; i++) {
                    (int enumValue, GUIContent name) = tempEnums[i];
                    s_EnumNamesSorted[i] = name;
                    s_EnumIndexMappings[i].sortedIndexToEnum = enumValue;
                    s_EnumIndexMappings[enumValue].enumToSortedIndex = i;
                }
            }

            bool didTweenTypeChange = false;

            if (draw) {
                Assert.IsNotNull(m_TweenTypeGuiContent);

                using (var scope = new CustomPropertyScope(pos, m_TweenTypeGuiContent, prop)) {
                    int intVal = prop.intValue;

                    int currentSortedIndex = intVal >= 0 && intVal < s_EnumIndexMappings.Length
                        ? s_EnumIndexMappings[intVal].enumToSortedIndex : 0;

                    int newSortedIndex = EditorGUI.Popup(pos, scope.content, currentSortedIndex, s_EnumNamesSorted);

                    if (scope.EndChangeCheck()) {
                        int newVal = s_EnumIndexMappings[newSortedIndex].sortedIndexToEnum;

                        if (prop.intValue != newVal) {
                            didTweenTypeChange = true;
                            prop.intValue = newVal;
                        }
                    }
                }
            }

            MoveToNextLine();

            TweenType tweenType = (TweenType)prop.intValue;
            (PropType, Type) tweenData;

            try {
                tweenData = Utils.TweenTypeToTweenData(tweenType);
            } catch {
                Debug.LogError(
                    $"Invalid tween type {tweenType} for {prop.propertyPath}. Please install necessary packages or use a newer version of Unity."
                );

                return height;
            }

            bool isShake = Utils.IsShake(tweenType);

            float duration = -1f;
            string targetName = null;

            var invalidInputColor = EditorGUIUtility.isProSkin ? new Color(2f, 0.9f, 0.9f, 1f)
                : new Color(1f, 0.9f, 0.9f, 1f);

            object targetMaterialOrRenderer = null;

            {
                // p1 todo support drag and drop
                Next(nameof(Data.targets));
                m_Targets.Clear();
                Type targetType = tweenData.Item2;

                if (targetType != null) {
                    if (draw) {
                        EditorGUI.LabelField(pos, prop.arraySize > 1 ? m_TargetsGuiContent : m_TargetGuiContent);
                    }

                    if (prop.arraySize == 0) {
                        prop.InsertArrayElementAtIndex(0);

                        if (AutoPopulateTargetReference(targetType, tweenType, prop) is UnityEngine.Object comp) {
                            prop.Next("Array", true);
                            prop.Next("size", true);
                            prop.Next("data");
                            prop.SetObjectReferenceChecked(comp);
                            return height;
                        }
                    }

                    bool canHaveMultipleTargets = tweenType != TweenType.TweenAnimationComponent
                                                  && tweenType != TweenType.ShakeCamera;

                    if (!canHaveMultipleTargets && !prop.SetArraySizeChecked(1)) {
                        return height;
                    }

                    var arrayProp = prop.Copy();
                    prop.Next("Array", true);
                    prop.Next("size", true);

                    for (int i = 0; i < arrayProp.arraySize; i++) {
                        prop.Next("data");
                        var el = prop;

                        Rect targetPropPos = canHaveMultipleTargets ? new Rect(
                            pos.x,
                            pos.y,
                            pos.width - 25f,
                            EditorGUIUtility.singleLineHeight
                        ) : pos;

                        UnityEngine.Object objectReferenceValue = el.objectReferenceValue;

                        if (tweenType == TweenType.TweenAnimationComponent) {
                            if (!m_DidCheckCircularReference) {
                                m_DidCheckCircularReference = true;

                                if (objectReferenceValue == el.serializedObject.targetObject
                                    || (objectReferenceValue is TweenAnimationComponent tac
                                        && TweenAnimationPropDrawer.FindSelfOrCircularReference(tac, tac))
                                   ) {
                                    Debug.LogWarning(TweenAnimationComponent.kSelfReferenceError);
                                    el.objectReferenceValue = null;
                                    return height;
                                }
                            }
                        }

                        if (draw) {
                            Color origColor = GUI.backgroundColor;

                            if (objectReferenceValue == null) {
                                GUI.backgroundColor = invalidInputColor;
                            }

                            using (var scope = new CustomPropertyScope(targetPropPos, m_EmptyLabel, el)) {
                                var newVal = EditorGUI.ObjectField(
                                    targetPropPos,
                                    scope.content,
                                    objectReferenceValue,
                                    targetType,
                                    true
                                );

                                if (targetMaterialOrRenderer == null) {
                                    targetMaterialOrRenderer =
                                        targetType == typeof(Material)
                                            ? newVal as Material
                                            : targetType == typeof(Renderer)
                                                ? newVal as Renderer
                                                : (object)null;
                                }

                                if (scope.EndChangeCheck()) {
                                    el.objectReferenceValue = newVal;

                                    if (tweenType == TweenType.TweenAnimationComponent) {
                                        m_DidCheckCircularReference = false;
                                        return height; // early return to check self and circular reference again
                                    }
                                }
                            }

                            GUI.backgroundColor = origColor;
                        }

                        if (objectReferenceValue != null) {
                            if (targetType.IsAssignableFrom(objectReferenceValue.GetType())) {
                                m_Targets.Add(objectReferenceValue);

                                if (string.IsNullOrEmpty(targetName)) {
                                    targetName = objectReferenceValue.name;
                                }
                            } else {
                                UnityEngine.Object newTarget =
                                    i == 0 ? AutoPopulateTargetReference(targetType, tweenType, el) : null;

                                el.SetObjectReferenceChecked(newTarget);
                            }
                        }

                        if (canHaveMultipleTargets) {
                            if (draw) {
                                Rect buttonRect = new Rect(
                                    pos.x + pos.width - 22f,
                                    pos.y,
                                    20f,
                                    EditorGUIUtility.singleLineHeight
                                );

                                if (i == 0) {
                                    if (GUI.Button(buttonRect, "+", EditorStyles.miniButton)) {
                                        arrayProp.InsertArrayElementAtIndex(arrayProp.arraySize - 1);

                                        return
                                            height; // early-exit because Unity 2019 throws an exception while multi-editing
                                    }
                                } else {
                                    if (GUI.Button(buttonRect, "-", EditorStyles.miniButton)) {
                                        arrayProp.DeleteArrayElementAtIndex(i);
                                        return height;
                                    }
                                }
                            }
                        } else if (!draw) {
                            if (tweenType == TweenType.TweenAnimationComponent
                                && objectReferenceValue is TweenAnimationComponent animationComponent
                                && animationComponent.animation is TweenAnimation tweenAnimation) {
                                List<PrimeTweenManager.CurrentAnimationData> nullData = null;

                                duration = TweenAnimationPropDrawer.ProcessAnimationDataAndReturnTotalDuration(
                                    tweenAnimation,
                                    ref nullData
                                );
                            }
                        }

                        MoveToNextLine();
                    }
                } else if (!prop.SetArraySizeChecked(0)) {
                    return height;
                }
            }

            // p2 todo it would be nice to select material property from the dropdown with the help of GetPropertyNames()
            Next(nameof(Data.stringParam));
            string stringParam = null;

            if (Utils.IsMaterialPropertyAnimation(tweenType)) {
                Color origColor = GUI.backgroundColor;
                stringParam = prop.stringValue;

                if (targetMaterialOrRenderer != null
                    && !Utils.IsValidMaterialProperty(
                        tweenType,
                        targetMaterialOrRenderer,
                        Shader.PropertyToID(stringParam)
                    )) {
                    GUI.backgroundColor = invalidInputColor;
                }

                PropertyField(m_MaterialPropertyNameGuiContent);
                GUI.backgroundColor = origColor;
            }

            Next(nameof(Data._customData));
            bool enableShakeFalloff = false;
            m_EventNamesSb.Clear(); // clear unconditionally to not mix different props with each other

            if (Data.IsCustomTweenType(tweenType)
                || tweenType == TweenType.ShakeCustom
                || tweenType == TweenType.Callback
               ) {
                if (!prop.SetArraySizeChecked(1)) {
                    return height;
                }

                prop.NextVisible("size", true);
                prop.NextVisible("data");

                Next(nameof(Data.Custom.callback), true);
                DrawUnityEvent(tweenType == TweenType.Callback, null, nameof(Data.Custom.unityEventFloat));

                DrawUnityEvent(
                    tweenType == TweenType.CustomFloat,
                    m_OnValueChangeGuiContent,
                    nameof(Data.Custom.unityEventColor)
                );

                DrawUnityEvent(
                    tweenType == TweenType.CustomColor,
                    m_OnValueChangeGuiContent,
                    nameof(Data.Custom.unityEventVector2)
                );

                DrawUnityEvent(
                    tweenType == TweenType.CustomVector2,
                    m_OnValueChangeGuiContent,
                    nameof(Data.Custom.unityEventVector3)
                );

                DrawUnityEvent(
                    tweenType == TweenType.CustomVector3 || tweenType == TweenType.ShakeCustom,
                    m_OnValueChangeGuiContent,
                    nameof(Data.Custom.unityEventVector4)
                );

                DrawUnityEvent(
                    tweenType == TweenType.CustomVector4,
                    m_OnValueChangeGuiContent,
                    nameof(Data.Custom.unityEventRect)
                );

                DrawUnityEvent(tweenType == TweenType.CustomRect, m_OnValueChangeGuiContent, nameof(Data.boolParam));

                void DrawUnityEvent(bool check, GUIContent guiContent, string nextPropName) {
                    if (check) {
                        PropertyField(guiContent, true);

                        prop.Next( /*"m_PersistentCalls",*/ true
                        ); // expectedName should be commented out because it was renamed in the past

                        prop.Next( /*"m_Calls",*/ true);
                        Assert.IsTrue(prop.isArray);
                        int arraySize = prop.arraySize;

                        if (arraySize == 0) {
                            prop.ExitCurrentDepth();
                        } else {
                            prop.Next("Array", true);
                            prop.Next("size", true);
                            prop.Next("data", true);

                            for (int i = 0; i < arraySize; i++) {
                                // Debug.Log(prop.propertyPath);
                                prop.Next( /*"m_Target", */true);
                                UnityEngine.Object eventTarget = prop.objectReferenceValue;

                                prop.Next( /*"m_TargetAssemblyTypeName", */false
                                ); // added in Unity 2020.1 https://github.com/Unity-Technologies/UnityCsReference/commit/2dce98888bf4f3b50cdb55b3ee761414b15bf7dc#diff-894c7d72950515231708ebe40ee39b7bc032a2d7b26af8fd0af7927dc2af7ef0

                                string methodName = prop.stringValue;
                                prop.Next( /*"m_MethodName", */false);

                                if (prop.propertyType == SerializedPropertyType.String) {
                                    methodName = prop.stringValue;
                                }

                                if (eventTarget != null && !string.IsNullOrEmpty(methodName)) {
                                    if (m_EventNamesSb.Length > 0) {
                                        m_EventNamesSb.Append(", ");
                                    }

                                    m_EventNamesSb
                                        .Append(eventTarget.GetType().Name)
                                        .Append('.')
                                        .Append(methodName);
                                }

                                prop.ExitCurrentDepth();
                            }
                        }

                        prop.CheckName(nextPropName);
                    } else {
                        prop.Next(nextPropName);
                    }
                }
            } else {
                if (!prop.SetArraySizeChecked(0)) {
                    return height;
                }
                
                Next(nameof(Data.boolParam));
            }

            if (tweenType == TweenType.Callback) {
                if (didTweenTypeChange) {
                    prop.SetBoolChecked(true);
                }

                PropertyField(m_WarnIfTargetDestroyedGuiContent);
                Next(nameof(Data.startValue));
                Next(nameof(Data.endValue));
            } else if (isShake) {
                if (tweenType != TweenType.ShakeCamera) {
                    if (didTweenTypeChange) {
                        prop.SetBoolChecked(false);
                    }

                    PropertyField(m_ShakeEnableFalloffGuiContent);
                    enableShakeFalloff = prop.boolValue;
                }

                Next(nameof(Data.startValue));
                Next(nameof(TweenAnimation.ValueWrapper.x), true);

                if (tweenType == TweenType.ShakeCustom) {
                    if (draw) {
                        EditorGUI.MultiPropertyField(pos, m_XYZLabels, prop, m_StartValueGuiContent);
                    } else {
                        Next(nameof(TweenAnimation.ValueWrapper.y));
                        Next(nameof(TweenAnimation.ValueWrapper.z));
                        Next(nameof(TweenAnimation.ValueWrapper.w));
                    }

                    AddHeight(EditorGUI.GetPropertyHeight(SerializedPropertyType.Vector3, m_StartValueGuiContent));
                } else {
                    Next(nameof(TweenAnimation.ValueWrapper.y));
                    Next(nameof(TweenAnimation.ValueWrapper.z));
                    Next(nameof(TweenAnimation.ValueWrapper.w));
                }

                PropertyField(m_ShakeFrequencyGuiContent);

                if (draw) {
                    ShakeSettingsPropDrawer.ClampFrequency(prop);
                }

                Next(nameof(Data.endValue));
                Next(nameof(TweenAnimation.ValueWrapper.x), true);

                if (tweenType == TweenType.ShakeCamera) {
                    if (didTweenTypeChange) {
                        prop.SetFloatChecked(0.5f);
                    }

                    PropertyField(m_StrengthFactorGuiContent);

                    if (draw) {
                        TweenSettingsPropDrawer.ClampProperty(prop, 0.5f);
                    }

                    Next(nameof(TweenAnimation.ValueWrapper.y));
                    Next(nameof(TweenAnimation.ValueWrapper.z));
                    Next(nameof(TweenAnimation.ValueWrapper.w));
                } else if (isShake) {
                    if (draw) {
                        if (didTweenTypeChange && !prop.hasMultipleDifferentValues) {
                            var copy = prop.Copy();
                            prop.floatValue = 0.5f;
                            prop.Next(nameof(TweenAnimation.ValueWrapper.y));
                            prop.floatValue = 0.5f;
                            prop.Next(nameof(TweenAnimation.ValueWrapper.z));
                            prop.floatValue = 0.5f;
                            prop = copy;
                        }

                        EditorGUI.MultiPropertyField(pos, m_XYZLabels, prop, m_ShakeStrengthGuiContent);
                    } else {
                        Next(nameof(TweenAnimation.ValueWrapper.y));
                        Next(nameof(TweenAnimation.ValueWrapper.z));
                        Next(nameof(TweenAnimation.ValueWrapper.w));
                    }

                    AddHeight(EditorGUI.GetPropertyHeight(SerializedPropertyType.Vector3, m_ShakeStrengthGuiContent));

                    if (draw) {
                        EditorGUI.Slider(pos, prop, 0f, 1f, m_ShakeAsymmetryGuiContent);
                    }

                    MoveToNextLine();
                }
            } else if (
                tweenType == TweenType.Disabled
                || tweenType == TweenType.TweenAnimationComponent
                || tweenType == TweenType.Delay
            ) {
                Next(nameof(Data.startValue));
                Next(nameof(Data.endValue));
            } else {
                if (draw) {
                    // p1 todo add a feature to pick the current value into start or end values. Instead of entering values manually, the user can press "pick current" button
                    float prevPosY = pos.y;
                    bool isCustom = Data.IsCustomTweenType(tweenType);
                    bool drawStartFromCurrentToggle = !isCustom;

                    if (PrimeTweenManager.sPlayModeState == PlayModeStateChange.EnteredPlayMode) {
                        // Populate startValue automatically only in Edit Mode. But at runtime, always use the saved value from Edit Mode.
                        prop.SetBoolChecked(true);
                    }

                    var idAndEndValue = ValueContainerStartEndPropDrawer.Draw(
                        ref pos,
                        prop,
                        tweenType,
                        drawStartFromCurrentToggle,
                        false,
                        m_StartValueGuiContent,
                        m_EndValueGuiContent,
                        TweenAnimation.sEndValueHighlightData.id
                    );
                    
                    if (!TweenAnimation.sIsPreviewing
                        && Event.current.type == EventType.Repaint
                        && !isCustom
                        && PrimeTweenManager.EnteredEditMode
                        && currentAnimation != null
                       ) {
                        var manager = PrimeTweenManager.Instance;

                        if (idAndEndValue.HasValue) {
                            (int id, TweenAnimation.ValueWrapper endValue) = idAndEndValue.Value;
                            Assert.AreNotEqual(0, id);

                            if (TweenAnimation.sEndValueHighlightData.id != id) {
                                manager.ResetCurrentTweenAnimations();
                                manager.TryAddCurrentTweenAnimation(currentAnimation);

                                Data? iniData = null;

                                foreach (var target in m_Targets) {
                                    var iniVal = TweenAnimation.GetCurrentValue(
                                        operation,
                                        target,
                                        tweenType,
                                        stringParam
                                    );

                                    if (iniVal.HasValue) {
                                        iniData = new Data {
                                            tweenType = tweenType,
                                            targets = m_Targets.ToList(),
                                            stringParam = stringParam,
                                            startValue = iniVal.Value,
                                            endValue = endValue,
                                            duration = 1f
                                        };

                                        break;
                                    }
                                }

                                string path = prop.propertyPath;
                                int lastIndex = path.LastIndexOf(".endValue.w", StringComparison.Ordinal);

                                TweenAnimation.sEndValueHighlightData = (
                                    id, GUIUtility.keyboardControl, currentAnimation, path.Substring(0, lastIndex),
                                    iniData);

                                // Debug.Log($"set _endValueHighlightData id:{id}, keyboardControl:{GUIUtility.keyboardControl}, {TweenAnimation._endValueHighlightData.propertyPath}");
                            }

                            for (int i = 0; i < m_Targets.Count; i++) {
                                var data = new Data {
                                    tweenType = tweenType,
                                    targets = m_Targets,
                                    stringParam = stringParam,
                                    endValue = endValue,
                                    duration = 1f
                                };

                                string error = string.Empty;
                                var tween = data.StartTween(ref error, currentAnimation, i);

                                if (tween.isAlive) {
                                    tween.Complete();
                                } else {
                                    Debug.LogError(error);
                                }
                            }
                        } else if (currentAnimation == TweenAnimation.sEndValueHighlightData.animation
                                   && TweenAnimation.sEndValueHighlightData.keyboardControl
                                   != GUIUtility.keyboardControl) {
                            manager.ResetCurrentTweenAnimations();
                        }
                    }

                    height += pos.y - prevPosY;
                } else {
                    AddHeight(ValueContainerStartEndPropDrawer.GetHeight(prop, null, tweenType));
                    Next(nameof(Data.startValue));
                    Next(nameof(Data.endValue));
                }
            }

            Next(nameof(Data.duration));

            if (tweenType == TweenType.Disabled
                || tweenType == TweenType.TweenAnimationComponent
                || tweenType == TweenType.Callback) { } else {
                pos.height = EditorGUIUtility.singleLineHeight;

                if (draw) {
                    TweenSettingsPropDrawer.DrawDuration(pos, prop);
                }

                duration = prop.floatValue;
                MoveToNextLine();
            }

            int cycles = -2;

            if (!Utils.CanHaveCycles(tweenType)) {
                Next(nameof(Data.ease));
                Next(nameof(Data.customEase));
                Next(nameof(Data.cycles));
                Next(nameof(Data.cycleMode));
            } else {
                if (isShake) {
                    Next(nameof(Data.ease));

                    if (enableShakeFalloff) {
                        PropertyField(m_ShakeFalloffEaseGuiContent);

                        if (prop.intValue == (int)Ease.Custom && prop.SetIntChecked((int)Ease.Default)) {
                            Debug.LogWarning($"Ease.Custom is not supported for {nameof(ShakeSettings.falloffEase)}.");
                        }
                    }
                } else {
                    float prevPosY = pos.y;

                    const bool
                        allowInfiniteCycles =
                            false; // infinite cycles (-1) should not be allowed because TweenAnimation returns Sequence and Sequence can't have infinite nested animations

                    TweenSettingsPropDrawer.DrawEaseAndCycles(
                        out cycles,
                        prop,
                        ref pos,
                        false,
                        draw,
                        allowInfiniteCycles
                    );

                    height += pos.y - prevPosY;
                }
            }

            // Debug.Log($"1 {draw} {arrayIndex}");
            if (arrayIndex != -1) {
                if (header.operation != operation
                    || header.tweenType != tweenType
                    || header.targetName != targetName
                    || header.insertionTime != insertionTime
                    || header.duration != duration
                    || header.cycles != cycles
                    || !StringBuilderEquals(m_EventNamesSb, header.eventNames)
                   ) {
                    m_Sb.Clear();
                    m_Sb.Append(operation);

                    if (isInsert) {
                        m_Sb.Append(" at ").AppendFormat("{0:0.0#}s", insertionTime);
                    }

                    const string separator = "   /   ";
                    m_Sb.Append(separator);
                    m_Sb.Append(tweenType);

                    if (m_EventNamesSb.Length > 0) {
                        m_Sb.Append(separator)
                            .Append(m_EventNamesSb);
                    } else if (!string.IsNullOrEmpty(targetName)) {
                        m_Sb.Append(separator).Append(targetName);
                    }

                    if (duration != -1f) {
                        m_Sb.Append(separator).AppendFormat("{0:0.0#}s", duration);

                        if (cycles > 1) {
                            m_Sb.Append(" (x").Append(cycles).Append(')');
                        }
                    }

                    header.operation = operation;
                    header.tweenType = tweenType;
                    header.targetName = targetName;
                    header.insertionTime = insertionTime;
                    header.duration = duration;
                    header.cycles = cycles;
                    header.eventNames = m_EventNamesSb.ToString();

                    if (header.guiContent == null) {
                        header.guiContent = new GUIContent();
                    }

                    header.guiContent.text = m_Sb.ToString();
        
                    if (currentAnimation != null) {
                        currentAnimation.headers[arrayIndex] = header;
                    }
                }
            }

            bool StringBuilderEquals(StringBuilder sb, string str) {
                if (sb.Length != str.Length) {
                    return false;
                }

                for (int i = 0; i < sb.Length; i++) {
                    if (sb[i] != str[i]) {
                        return false;
                    }
                }

                return true;
            }

            return foldedHeight ?? height;
        }

        void PropertyField(GUIContent guiContent = null, bool isMultiLine = false) {
            float propertyHeight = isMultiLine ? EditorGUI.GetPropertyHeight(prop, false)
                : EditorGUIUtility.singleLineHeight;

            pos.height = propertyHeight;

            if (draw) {
                EditorGUI.PropertyField(pos, prop, guiContent, false);
            }

            AddHeight(propertyHeight + EditorGUIUtility.standardVerticalSpacing);
        }

        void Next(string expectedName, bool enterChildren = false) => prop.Next(expectedName, enterChildren);

        void MoveToNextLine() =>
            AddHeight(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

        void AddHeight(float h) {
            pos.y += h;
            height += h;
        }
    }

    private static UnityEngine.Object AutoPopulateTargetReference(
        Type type,
        TweenType tweenType,
        SerializedProperty prop
    ) {
        // p0 todo this has a bug that confuses references. To reproduce: create object1 that references child1. Copy object1, select both copies and change their tweenType. Their target references will be swapped
        /*if (prop.serializedObject.isEditingMultipleObjects) {
            return null;
        }
        if (tweenType == TweenType.TweenAnimationComponent) {
            return null;
        }
        if (typeof(Component).IsAssignableFrom(type)
            #if UNITY_2021_2_OR_NEWER
            && UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() == null
            #else
            && UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() == null
            #endif
            && TweenAnimationPropDrawer._currentAnimation.context is MonoBehaviour mb && mb
           ) {
            return mb.GetComponentInChildren(type);
            // return mb.GetComponentInChildren(type) ??
            // #if UNITY_2021_2_OR_NEWER
            // UnityEngine.Object.FindAnyObjectByType(type);
            // #else
            // UnityEngine.Object.FindObjectOfType(type);
            // #endif
        }*/
        return null;
    }

    private static int GetArrayIndex(string propertyPath) {
        int startIndex = propertyPath.LastIndexOf('[');

        if (startIndex == -1) {
            return -1;
        }

        Assert.IsTrue(startIndex >= 0);

        int endIndex = propertyPath.IndexOf(']', startIndex);
        Assert.IsTrue(endIndex > 0);

        int result = 0;

        for (int i = startIndex + 1; i < endIndex; i++) {
            char c = propertyPath[i];
            Assert.IsTrue(c >= '0' && c <= '9');
            result = result * 10 + (c - '0');
        }

        return result;
    }
}