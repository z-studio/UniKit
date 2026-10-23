using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 保存扫描时的 Prefab 数据和本地引用身份。源 Prefab 先替换后，Variant/嵌套对象中的 A
    /// 可能已随导入消失；仍使用这份快照恢复其覆盖值，避免只得到源 Prefab 的默认值。
    /// </summary>
    internal sealed class ComponentPrefabSnapshot {
        private sealed class Identity {
            internal int id;
            internal int[] hierarchy;
            internal Type type;
            internal int ordinal;
        }

        private sealed class Reference {
            internal Component owner;
            internal int ownerId;
            internal int targetId;
            internal string path;
        }

        private readonly Type m_SourceType;
        private readonly List<Identity> m_Identities = new();
        private readonly List<Reference> m_References = new();
        private readonly Dictionary<int, ComponentData> m_Data = new();

        internal ComponentPrefabSnapshot(GameObject root, Type sourceType) {
            m_SourceType = sourceType;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) {
                var indices = new List<int>();

                for (var current = transform; current != root.transform; current = current.parent) {
                    indices.Add(current.GetSiblingIndex());
                }

                indices.Reverse();
                var hierarchy = indices.ToArray();
                m_Identities.Add(new Identity { id = transform.gameObject.GetInstanceID(), hierarchy = hierarchy });
                var ordinals = new Dictionary<Type, int>();

                foreach (var component in transform.GetComponents<Component>().Where(c => c != null)) {
                    var type = component.GetType();
                    ordinals.TryGetValue(type, out var ordinal);
                    ordinals[type] = ordinal + 1;
                    var id = component.GetInstanceID();
                    m_Identities.Add(new Identity { id = id, hierarchy = hierarchy, type = type, ordinal = ordinal });

                    if (type == sourceType) {
                        m_Data.Add(id, new ComponentData(component));
                        continue;
                    }

                    using var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();

                    while (property.Next(true)) {
                        if (ComponentData.IsMetadata(property.propertyPath)) {
                            continue;
                        }

                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue is Component target && target.GetType() == sourceType) {
                            m_References.Add(new Reference {
                                owner = component,
                                ownerId = id,
                                targetId = target.GetInstanceID(),
                                path = property.propertyPath
                            });
                        }
                    }
                }
            }
        }

        internal bool ReferencesAny(HashSet<int> ids) =>
            m_References.Any(reference => ids.Contains(reference.targetId));

        internal void ValidateReferences(HashSet<int> ids, Type targetType) {
            foreach (var reference in m_References.Where(reference => ids.Contains(reference.targetId))) {
                var owner = reference.owner;

                if (owner == null) {
                    throw new InvalidOperationException("引用持有者已改变，请重新扫描。");
                }

                using var serialized = new SerializedObject(owner);
                var property = serialized.FindProperty(reference.path);

                if (property == null) {
                    throw new InvalidOperationException("引用字段已改变，请重新扫描。");
                }

                ComponentReplacement.CheckReference(owner.GetType(), property, targetType);
            }
        }

        internal Dictionary<int, Object> Map(GameObject root, Type targetType) {
            var map = new Dictionary<int, Object>();

            foreach (var identity in m_Identities) {
                var transform = root.transform;

                foreach (var index in identity.hierarchy) {
                    if (index >= transform.childCount) throw new InvalidOperationException("Prefab 层级已改变，请重新扫描。");
                    transform = transform.GetChild(index);
                }

                if (identity.type == null) {
                    map.Add(identity.id, transform.gameObject);
                    continue;
                }

                var components = transform.GetComponents(identity.type)
                    .Where(c => c != null && c.GetType() == identity.type).ToArray();

                if (components.Length == 0 && identity.type == m_SourceType) {
                    components = transform.GetComponents(targetType).Where(c => c != null && c.GetType() == targetType)
                        .ToArray();
                }

                if (identity.ordinal < components.Length) {
                    map.Add(identity.id, components[identity.ordinal]);
                }
            }

            return map;
        }

        internal int RestoreData(IEnumerable<ComponentReplacementEntry> entries, Dictionary<int, Object> map,
            bool copyFields) {
            if (!copyFields) {
                return 0;
            }

            var copied = 0;

            foreach (var entry in entries) {
                if (!map.TryGetValue(entry.sourceId, out var target) || target is not Component component) {
                    throw new InvalidOperationException("无法定位继承的目标组件，请重新扫描。");
                }

                copied += m_Data[entry.sourceId].Restore(component, new Dictionary<int, Component>(), false, map);
            }

            return copied;
        }

        internal static Dictionary<int, Object> PersistentMap(GameObject loaded, GameObject persistent,
            Dictionary<int, Object> map) {
            var result = new Dictionary<int, Object>();

            foreach (var pair in map) {
                var component = pair.Value as Component;
                var gameObject = component != null ? component.gameObject : pair.Value as GameObject;

                if (gameObject == null || gameObject.scene != loaded.scene) {
                    continue;
                }

                var indices = new List<int>();

                for (var transform = gameObject.transform; transform != loaded.transform;
                     transform = transform.parent) {
                    indices.Add(transform.GetSiblingIndex());
                }

                var target = persistent.transform;

                for (var i = indices.Count - 1; i >= 0; i--) {
                    target = target.GetChild(indices[i]);
                }

                result.Add(pair.Key, component == null ? target.gameObject :
                    target.GetComponents<Component>()[Array.IndexOf(gameObject.GetComponents<Component>(), component)]);
            }

            return result;
        }

        internal int RestoreReferences(Dictionary<int, Object> map) {
            var changed = 0;

            foreach (var reference in m_References) {
                if (!map.TryGetValue(reference.targetId, out var target) || target == null
                                                                         || target.GetType() == m_SourceType) {
                    continue;
                }

                if (!map.TryGetValue(reference.ownerId, out var owner) || owner == null) {
                    throw new InvalidOperationException("引用的持有组件已改变，请重新扫描。");
                }

                using var serialized = new SerializedObject(owner);
                var property = serialized.FindProperty(reference.path);

                if (property == null) {
                    throw new InvalidOperationException($"引用字段已改变：{reference.path}");
                }

                ComponentReplacement.CheckReference(owner.GetType(), property, target.GetType());

                if (property.objectReferenceValue == target) {
                    continue;
                }

                property.objectReferenceValue = target;
                changed++;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }
    }
}