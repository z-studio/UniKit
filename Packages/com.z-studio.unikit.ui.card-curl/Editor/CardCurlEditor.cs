using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ZStudio.UniKit.UI.Editor {
    [CustomEditor(typeof(CardCurl)), CanEditMultipleObjects]
    internal sealed class CardCurlEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            bool changed = DrawDefaultInspector();

            foreach (Object item in targets) {
                var card = (CardCurl)item;

                if (!card.ValidateConfiguration(out string error)) {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                    continue;
                }

                if (!changed || Application.isPlaying) {
                    continue;
                }

                // Inspector 回调处于主线程，派生的图片与几何预览不放进 OnValidate。
                Undo.RecordObjects(card.GetComponentsInChildren<Transform>(true), "Preview Card Curl");
                Undo.RecordObjects(card.GetComponentsInChildren<Image>(true), "Preview Card Curl");
                card.Reset();
            }
        }
    }
}