using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 屏蔽 TMP、输入框和旧版文本之间的字体接口差异，只负责单个组件的读写。
    /// 扫描只调用读取方法，不调用 setter，也不通过 material/fontMaterial 生成材质实例。
    /// 场景标脏、文件保存及整次操作的 Undo 分组由 Applier 负责。
    /// </summary>
    internal static class FontReplacementComponent {
        // 使用基类匹配，因此 TMP_Text 的派生组件无需再维护类型名单。
        internal static bool Supports(Component component, FontReplacementKind kind) {
            return kind == FontReplacementKind.TextMeshPro
                ? component is TMP_Text or TMP_InputField
                : component is Text or TextMesh;
        }

        internal static Object ReadFont(Component component) {
            return component switch {
                TMP_Text text => text.font,
                TMP_InputField input => input.fontAsset,
                Text text => text.font,
                TextMesh text => text.font,
                _ => null
            };
        }

        // 只记录本工具会直接替换的共享材质；输入框自身和 UGUI Text 不需要材质快照。
        internal static Material ReadMaterial(Component component) {
            if (component is TMP_Text text) {
                return text.fontSharedMaterial;
            }

            return component is TextMesh mesh ? mesh.GetComponent<Renderer>()?.sharedMaterial : null;
        }

        /// <summary>判断是否已继承或通过其他操作达到目标状态；不只比较字体，避免遗漏材质不匹配。</summary>
        internal static bool IsTarget(Component component, FontReplacementOptions options) {
            if (ReadFont(component) != options.targetFont) {
                return false;
            }

            if (component is TMP_Text text) {
                var material = options.targetMaterial != null
                    ? options.targetMaterial : ((TMP_FontAsset)options.targetFont).material;
                return text.fontSharedMaterial == material;
            }

            if (component is TextMesh mesh && mesh.GetComponent<Renderer>() != null) {
                return ReadMaterial(mesh) == ((Font)options.targetFont).material;
            }

            return true;
        }

        /// <summary>调用方须先完成参数和快照校验；undo 仅为存活的场景对象开启。</summary>
        internal static void Replace(Component component, FontReplacementOptions options, bool undo) {
            if (undo) {
                Undo.RecordObject(component, "Replace Font");
            }

            switch (component) {
                case TMP_Text text:
                    // 显式同步共享材质，避免沿用旧字体图集；其余字号、颜色和布局参数不主动修改。
                    text.font = (TMP_FontAsset)options.targetFont;
                    text.fontSharedMaterial = options.targetMaterial != null
                        ? options.targetMaterial : text.font.material;
                    break;
                case TMP_InputField input:
                    // fontAsset 的 setter 会连带修改 label/placeholder，破坏逐行勾选和 Undo。
                    // 此处仅更新输入框自身引用，文字组件由各自的预览行负责。
                    var serialized = new SerializedObject(input);
                    serialized.FindProperty("m_GlobalFontAsset").objectReferenceValue = options.targetFont;

                    if (undo) {
                        serialized.ApplyModifiedProperties();
                    } else {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    break;
                case Text text:
                    text.font = (Font)options.targetFont;
                    break;
                case TextMesh mesh:
                    mesh.font = (Font)options.targetFont;
                    var renderer = mesh.GetComponent<Renderer>();

                    if (renderer != null) {
                        // 3D TextMesh 的可见字形还依赖 Renderer，必须一并更新和记录 Undo。
                        if (undo) {
                            Undo.RecordObject(renderer, "Replace Font");
                        }

                        renderer.sharedMaterial = mesh.font.material;
                        MarkChanged(renderer);
                    }

                    break;
            }

            MarkChanged(component);
        }

        // SetDirty 不会替代 Prefab override 记录；实例上的属性修改必须另外登记才能正确保存。
        private static void MarkChanged(Object target) {
            EditorUtility.SetDirty(target);

            if (PrefabUtility.IsPartOfPrefabInstance(target)) {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }
    }
}