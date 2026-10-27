using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.UI.Editor {
    internal static class EditorMenu {
        internal const string PackagePath = "Packages/com.z-studio.unikit.ui.card-curl";

        [MenuItem("Tools/UniKit/UI Card Curl/Create UI Card")]
        private static void CreateObject() {
            Transform parent = Selection.activeTransform;
            Canvas canvas = parent ? parent.GetComponentInParent<Canvas>() : Object.FindFirstObjectByType<Canvas>();

            if (!canvas) {
                Debug.LogError("Select a parent in an existing Canvas before creating a card.");
                return;
            }

            if (!parent || !parent.GetComponentInParent<Canvas>()) {
                parent = canvas.transform;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackagePath + "/Res/CardCurl.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Create UI Card Curl");
            Selection.activeGameObject = go;
        }
    }
}