using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    internal enum SpriteFontOutput {
        LegacyText,
        TextMeshPro,
        Both
    }

    internal enum SpriteFontOrder {
        SpriteEditor,
        Name,
        Position
    }

    [Serializable]
    internal sealed class SpriteFontCharacter {
        public string SpriteId;
        public string Character;
    }

    /// <summary>保存制作参数和输出引用；双击配置即可继续编辑，不参与运行时。</summary>
    internal sealed class SpriteFontSettings : ScriptableObject {
        public Texture2D Atlas;
        // 绑定 Sprite 的稳定 ID，列表排序和切片改名都不改变字符映射。
        public List<SpriteFontCharacter> Mappings = new();
        public SpriteFontOrder Order = SpriteFontOrder.Name;
        public SpriteFontOutput Output = SpriteFontOutput.TextMeshPro;
        public int FontSize = 32;
        public bool NormalizeHeight;
        public int Baseline;
        public int LetterSpacing;
        public int LineSpacing;
        public int SpaceWidth = 16;
        
        [HideInInspector] 
        public Font LegacyFont;
        
        [HideInInspector] 
        public TMP_FontAsset TMPFont;
    }

    [CustomEditor(typeof(SpriteFontSettings))]
    internal sealed class SpriteFontSettingsEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            var settings = (SpriteFontSettings)target;
            EditorGUILayout.HelpBox("此资源保存 Sprite Font 的制作参数。双击资源，或点击下方按钮继续编辑。", MessageType.Info);

            if (GUILayout.Button("打开字体制作工具", GUILayout.Height(30))) {
                AssetDatabase.OpenAsset(settings);
            }

            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("图集", settings.Atlas, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("Text 字体", settings.LegacyFont, typeof(Font), false);
                EditorGUILayout.ObjectField("TMP 字体", settings.TMPFont, typeof(TMP_FontAsset), false);
            }
        }
    }
}