#pragma warning disable CS0162
using JetBrains.Annotations;
using PrimeTween;
using UnityEditor;
using UnityEngine;
using static UnityEditor.EditorGUI;
using static UnityEditor.EditorGUIUtility;

[CustomPropertyDrawer(typeof(TweenSettings<float>)),
 CustomPropertyDrawer(typeof(TweenSettings<Color>)),
 CustomPropertyDrawer(typeof(TweenSettings<Vector2>)),
 CustomPropertyDrawer(typeof(TweenSettings<Vector3>)),
 CustomPropertyDrawer(typeof(TweenSettings<Vector4>)),
 CustomPropertyDrawer(typeof(TweenSettings<Rect>)),
 CustomPropertyDrawer(typeof(TweenSettings<Quaternion>)),
 CustomPropertyDrawer(typeof(TweenSettings<int>))
]
internal class TweenSettingsTypesPropDrawer : PropertyDrawer {
    private const bool k_DrawStartFromCurrent = false;

    public override float GetPropertyHeight([NotNull] SerializedProperty property, GUIContent label) {
        if (!property.isExpanded) {
            return singleLineHeight;
        }

        var count = 0;
        float height = 0f;
        property.NextVisible(true); // startFromCurrent
        incrementHeight(); // startValue
        incrementHeight(); // endValue
        property.NextVisible(false);
        var result = height + 0 * (count - 1) + TweenSettingsPropDrawer.GetPropHeight(property);
        result += standardVerticalSpacing * 2; // extra space
        return result;

        void incrementHeight() {
            property.NextVisible(false);
            count++; // startFromCurrent
            height += EditorGUI.GetPropertyHeight(property, true);
        }
    }

    public override void OnGUI(Rect position, [NotNull] SerializedProperty property, GUIContent label) {
        var rect = new Rect(position) { height = singleLineHeight };
        PropertyField(rect, property, label);

        if (!property.isExpanded) {
            return;
        }

        rect.y += singleLineHeight + standardVerticalSpacing;
        indentLevel++;

        // startFromCurrent
        property.NextVisible(true);

        // startValue
        {
            var startFromCurrent = property.boolValue;
            property.NextVisible(false);

            if (!startFromCurrent || !k_DrawStartFromCurrent) {
                PropertyField(rect, property, true);
                MoveToNextLine(true);
            }
        }

        // endValue
        property.NextVisible(false);
        PropertyField(rect, property, true);
        MoveToNextLine(true);

        // duration
        {
            property.NextVisible(false); // settings
            property.NextVisible(true); // duration
            TweenSettingsPropDrawer.DrawDuration(rect, property);
            MoveToNextLine(false);
        }

        TweenSettingsPropDrawer.DrawEaseTillEnd(property, ref rect);

        indentLevel--;

        void MoveToNextLine(bool includeChildren) {
            rect.y += EditorGUI.GetPropertyHeight(property, includeChildren) + standardVerticalSpacing;
        }
    }
}