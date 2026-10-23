using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 在删除 A 前保存序列化字段，在 B 上恢复兼容字段。
    /// 不复制组件身份、脚本和 Prefab 元数据；不同名或不同类型的字段保留 B 的默认值。
    /// </summary>
    internal sealed class ComponentData {
        private sealed class Value {
            internal string path;
            internal SerializedPropertyType kind;
            internal object data;
            internal int referenceId;
        }

        private sealed class Field {
            internal string path;
            internal string serializedType;
            internal Type fieldType;
            internal readonly List<Value> values = new();
        }

        private readonly List<Field> m_Fields = new();

        internal ComponentData(Component source) {
            using var serialized = new SerializedObject(source);
            var property = serialized.GetIterator();

            if (!property.Next(true)) {
                return;
            }

            do {
                if (IsMetadata(property.name)) {
                    continue;
                }

                // ManagedReference 的具体类型和对象图不做猜测性迁移。
                var field = new Field {
                    path = property.propertyPath,
                    serializedType = property.type,
                    fieldType = FindField(source.GetType(), property.name)?.FieldType
                };

                if (Capture(property.Copy(), field.values)) {
                    m_Fields.Add(field);
                }
            } while (property.Next(false));
        }

        internal int Restore(Component target, IReadOnlyDictionary<int, Component> replacements, bool useUndo = true,
            IReadOnlyDictionary<int, Object> contextReferences = null) {
            using var serialized = new SerializedObject(target);
            var count = 0;

            foreach (var field in m_Fields) {
                var destination = serialized.FindProperty(field.path);

                if (destination == null || destination.type != field.serializedType) {
                    continue;
                }

                var targetType = FindField(target.GetType(), field.path)?.FieldType;

                if (field.fieldType != targetType) {
                    continue;
                }

                foreach (var value in field.values) {
                    var property = serialized.FindProperty(value.path);

                    if (property == null || property.propertyType != value.kind) {
                        throw new InvalidOperationException($"字段结构不兼容：{target.name}.{value.path}");
                    }

                    switch (value.kind) {
                        case SerializedPropertyType.ArraySize:
                        case SerializedPropertyType.Enum:
                            property.intValue = (int)value.data;
                            break;
                        case SerializedPropertyType.ObjectReference
                            when replacements.TryGetValue(value.referenceId, out var replacement):
                            ComponentReplacement.CheckReference(target.GetType(), property, replacement.GetType());
                            property.objectReferenceValue = replacement;
                            break;
                        case SerializedPropertyType.ObjectReference when contextReferences != null
                                                                         && contextReferences.TryGetValue(
                                                                             value.referenceId, out var mapped): {
                            if (mapped != null) {
                                ComponentReplacement.CheckReference(target.GetType(), property, mapped.GetType());
                            }

                            property.objectReferenceValue = mapped;
                            break;
                        }
                        case SerializedPropertyType.ObjectReference:
                            property.objectReferenceValue = (Object)value.data;
                            break;
                        default:
                            property.boxedValue = value.data;
                            break;
                    }
                }

                count++;
            }

            if (useUndo) {
                serialized.ApplyModifiedProperties();
            } else {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return count;
        }

        private static bool Capture(SerializedProperty property, List<Value> values) {
            if (property.propertyType == SerializedPropertyType.ManagedReference || property.isFixedBuffer) {
                return false;
            }

            if (property.propertyType == SerializedPropertyType.Generic) {
                var end = property.GetEndProperty();

                if (!property.Next(true)) {
                    return true;
                }

                while (!SerializedProperty.EqualContents(property, end)) {
                    if (!Capture(property.Copy(), values)) {
                        return false;
                    }

                    if (!property.Next(false)) {
                        break;
                    }
                }

                return true;
            }

            var value = new Value { path = property.propertyPath, kind = property.propertyType };

            switch (value.kind) {
                case SerializedPropertyType.ObjectReference:
                    value.data = property.objectReferenceValue;
                    value.referenceId = property.objectReferenceInstanceIDValue;
                    break;
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                    value.data = property.intValue;
                    break;
                default:
                    value.data = property.boxedValue;
                    break;
            }

            values.Add(value);
            return true;
        }

        internal static bool IsMetadata(string name) =>
            name == "m_Script" || name == "m_ObjectHideFlags" ||
            name == "m_GameObject" || name == "m_EditorClassIdentifier" || name == "m_Name" ||
            name.StartsWith("m_CorrespondingSource") || name.StartsWith("m_Prefab");

        internal static FieldInfo FindField(Type type, string name) {
            while (type != null) {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (field != null) {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}