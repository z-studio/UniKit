using JetBrains.Annotations;
using PrimeTween;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PrimeTweenManager))]
internal class PrimeTweenManagerInspector : Editor {
    private SerializedProperty m_TweensProp;
    private SerializedProperty m_LateUpdateTweensProp;
    private SerializedProperty m_FixedUpdateTweensProp;
    private GUIContent m_AliveTweenGuiContent;
    private GUIContent m_LateUpdateTweenGuiContent;
    private GUIContent m_FixedUpdateTweenGuiContent;
    private StringCache m_TweensCountCache;
    private StringCache m_MaxSimultaneousTweensCountCache;
    private StringCache m_CurrentPoolCapacityCache;

    private void OnEnable() {
        m_TweensProp = serializedObject.FindProperty(nameof(PrimeTweenManager.inspectorTweensUpdate));
        m_LateUpdateTweensProp = serializedObject.FindProperty(nameof(PrimeTweenManager.inspectorTweensLateUpdate));
        m_FixedUpdateTweensProp = serializedObject.FindProperty(nameof(PrimeTweenManager.inspectorTweensFixedUpdate));
        Assert.IsNotNull(m_TweensProp);
        Assert.IsNotNull(m_LateUpdateTweensProp);
        Assert.IsNotNull(m_FixedUpdateTweensProp);
        m_AliveTweenGuiContent = new GUIContent("Tweens");
        m_LateUpdateTweenGuiContent = new GUIContent("Late update tweens");
        m_FixedUpdateTweenGuiContent = new GUIContent("Fixed update tweens");

        PrimeTweenManager.sInstance.updateInspectorTweens = true;
        PrimeTweenManager.sInstance.UpdateInspectorTweens();
    }

    private void OnDisable() => PrimeTweenManager.sInstance.updateInspectorTweens = false;

    public override void OnInspectorGUI() {
        using (new EditorGUI.DisabledScope(true)) {
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromMonoBehaviour((MonoBehaviour)target),
                typeof(MonoBehaviour),
                false
            );
        }

        var manager = target as PrimeTweenManager;
        Assert.IsNotNull(manager);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Alive tweens", EditorStyles.label);
        GUILayout.Label(m_TweensCountCache.GetCachedString(manager.TweensCount), EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label(Constants.kMaxAliveTweens, EditorStyles.label);

        GUILayout.Label(
            m_MaxSimultaneousTweensCountCache.GetCachedString(manager.MaxSimultaneousTweensCount),
            EditorStyles.boldLabel
        );

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Tweens capacity", EditorStyles.label);
        GUILayout.Label(m_CurrentPoolCapacityCache.GetCachedString(manager.CurrentPoolCapacity), EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Use "
            + Constants.kSetTweensCapacityMethod
            + " to set tweens capacity.\n"
            + "To prevent memory allocations during runtime, choose the value that is greater than the maximum number of simultaneous tweens in your game.",
            MessageType.None
        );

        DrawList(m_TweensProp, m_AliveTweenGuiContent);
        DrawList(m_LateUpdateTweensProp, m_LateUpdateTweenGuiContent);
        DrawList(m_FixedUpdateTweensProp, m_FixedUpdateTweenGuiContent);

        void DrawList(SerializedProperty prop, GUIContent guiContent) {
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.PropertyField(prop, guiContent);
            }
        }
    }

    private struct StringCache {
        private int m_CurrentValue;
        private string m_Str;

        [NotNull]
        internal string GetCachedString(int value) {
            if (m_CurrentValue != value || m_Str == null) {
                m_CurrentValue = value;
                m_Str = value.ToString();
            }

            return m_Str;
        }
    }
}