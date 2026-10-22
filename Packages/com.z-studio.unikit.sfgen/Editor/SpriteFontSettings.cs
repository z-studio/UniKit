using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace ZStudio.UniKit.Editor {
    internal enum SpriteFontSourceType {
        Texture,
        Sprites,
        SpriteAtlas
    }

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
        public string spriteId;
        public string character;
    }

    /// <summary>保存制作参数和输出引用；双击配置即可继续编辑，不参与运行时。</summary>
    internal sealed class SpriteFontSettings : ScriptableObject {
        public SpriteFontSourceType source;
        public Texture2D atlas;
        public List<Sprite> sourceSprites = new();
        public SpriteAtlas sourceAtlas;

        // 绑定 Sprite 的稳定 ID，列表排序和切片改名都不改变字符映射。
        public List<SpriteFontCharacter> mappings = new();
        public SpriteFontOrder order = SpriteFontOrder.Name;
        public SpriteFontOutput output = SpriteFontOutput.TextMeshPro;
        public int fontSize = 32;
        public bool normalizeHeight;
        public int baseline;
        public int letterSpacing;
        public int lineSpacing;
        public int spaceWidth = 16;

        [HideInInspector]
        public Font legacyFont;

        [HideInInspector]
        public TMP_FontAsset tmpFont;
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
                EditorGUILayout.EnumPopup("来源类型", settings.source);
                EditorGUILayout.ObjectField("Text 字体纹理", settings.legacyFont != null
                    ? settings.legacyFont.material?.mainTexture : null, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("TMP 字体纹理", settings.tmpFont != null
                    ? settings.tmpFont.atlasTexture : null, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("Text 字体", settings.legacyFont, typeof(Font), false);
                EditorGUILayout.ObjectField("TMP 字体", settings.tmpFont, typeof(TMP_FontAsset), false);
            }
        }
    }
}