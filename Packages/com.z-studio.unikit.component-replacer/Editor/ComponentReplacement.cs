using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 替换事务：重新校验 → 保存数据/引用 → 删除 A → 创建全部 B → 恢复数据/引用。
    /// 全部成功才提交 Undo 组；任何失败均回滚整批，不留下半完成的替换。
    /// 默认处理当前编辑上下文；资产批处理可传入隔离加载的 Prefab 上下文，保存由调用方负责。
    /// </summary>
    internal static class ComponentReplacement {
        internal const string UndoName = "Replace Components";

        // 不能靠“引用了 UnityEditor”判断程序集用途：运行时脚本也可能包含 #if UNITY_EDITOR 代码。
        private static readonly HashSet<string> s_EditorAssemblies = CompilationPipeline
            .GetAssemblies(AssembliesType.Editor)
            .Where(assembly => (assembly.flags & AssemblyFlags.EditorAssembly) != 0)
            .Select(assembly => assembly.name).ToHashSet();

        internal static IEnumerable<GameObject> ContextObjects() {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null) {
                return Descendants(stage.prefabContentsRoot);
            }

            return Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded)
                .SelectMany(scene => scene.GetRootGameObjects()).SelectMany(Descendants);
        }

        private static bool IsContextObject(GameObject gameObject) {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null) {
                return gameObject.scene == stage.scene;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++) {
                if (SceneManager.GetSceneAt(i) == gameObject.scene) {
                    return gameObject.scene.isLoaded;
                }
            }

            return false;
        }

        private static IEnumerable<GameObject> Descendants(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject);

        internal static Component[] Scan(Type sourceType, bool selectionOnly) {
            if (sourceType == null) {
                return Array.Empty<Component>();
            }

            var context = ContextObjects().ToHashSet();
            var objects = selectionOnly
                ? Selection.gameObjects.Where(context.Contains).SelectMany(Descendants).Distinct()
                : context;

            // 精确匹配，避免把未知的派生类一并替换。
            return objects.SelectMany(go => go.GetComponents(sourceType))
                .Where(component => component != null && component.GetType() == sourceType).ToArray();
        }

        internal static string ValidateType(Type type) {
            if (type == null) {
                return "请选择组件类型。";
            }

            if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters ||
                typeof(Transform).IsAssignableFrom(type)) {
                return "请选择可添加的组件，Transform / RectTransform 不支持替换。";
            }

            if (s_EditorAssemblies.Contains(type.Assembly.GetName().Name) ||
                type.Assembly.GetName().Name.StartsWith("UnityEditor")) {
                return "不能添加编辑器专用组件。";
            }

            return null;
        }

        internal static string Validate(Component source, Type targetType, bool isolated = false) {
            var error = ValidateType(targetType);

            if (error != null) {
                return error;
            }

            if (source == null) {
                return "组件已不存在，请重新扫描。";
            }

            error = ValidateType(source.GetType());

            if (error != null) {
                return error;
            }

            if (source.GetType() == targetType) {
                return "A 与 B 类型相同。";
            }

            if (!isolated && (EditorUtility.IsPersistent(source) || !IsContextObject(source.gameObject))) {
                return "请在场景或 Prefab Mode 中编辑该对象。";
            }

            if ((source.hideFlags & HideFlags.NotEditable) != 0 ||
                (source.gameObject.hideFlags & HideFlags.NotEditable) != 0) {
                return "对象不可编辑。";
            }

            if (PrefabUtility.IsPartOfImmutablePrefab(source)) {
                return "该 Prefab 不可编辑。";
            }

            if (source.gameObject.GetComponent(targetType) != null) {
                return "对象上已存在 B，避免覆盖现有数据。";
            }

            // Unity 不允许先移除仍被 RequireComponent 依赖的组件。
            foreach (var other in source.GetComponents<Component>().Where(c => c != null && c != source)) {
                foreach (RequireComponent requirement in other.GetType()
                             .GetCustomAttributes(typeof(RequireComponent), true)) {
                    foreach (var required in new[] { requirement.m_Type0, requirement.m_Type1, requirement.m_Type2 }) {
                        if (required != null && required.IsInstanceOfType(source) &&
                            !source.GetComponents(required).Any(c => c != source)) {
                            return $"{other.GetType().Name} 通过 RequireComponent 依赖 A，请先处理该依赖。";
                        }
                    }
                }
            }

            return null;
        }

        private sealed class Reference {
            internal Component owner;
            internal string path;
            internal int sourceId;
        }

        internal static int Apply(IReadOnlyList<Component> sources, Type targetType, bool copyFields,
            IReadOnlyCollection<GameObject> isolatedContext = null,
            Dictionary<int, Component> outputReplacements = null) {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) {
                throw new InvalidOperationException("请在编辑模式且编译完成后替换。");
            }

            if (sources.Count == 0) {
                return 0;
            }

            if (sources.Distinct().Count() != sources.Count) {
                throw new InvalidOperationException("替换列表含重复组件。");
            }

            foreach (var source in sources) {
                var error = Validate(source, targetType, isolatedContext != null);

                if (source != null && (EditorUtility.IsPersistent(source) ||
                                       isolatedContext != null && !isolatedContext.Contains(source.gameObject))) {
                    error = "替换对象不在可编辑的加载上下文中。";
                }

                if (error != null) {
                    throw new InvalidOperationException($"{source?.name}：{error}");
                }
            }

            // 删除前收集所有外部引用位置；删除后 Unity 会把旧引用显示为 Missing。
            var ids = sources.Select(c => c.GetInstanceID()).ToHashSet();
            var references = new List<Reference>();

            foreach (var owner in (isolatedContext ?? ContextObjects().ToArray())
                     .SelectMany(go => go.GetComponents<Component>()).Where(c => c != null)) {
                if (ids.Contains(owner.GetInstanceID())) {
                    continue;
                }

                using var serialized = new SerializedObject(owner);
                var property = serialized.GetIterator();

                while (property.Next(true)) {
                    if (ComponentData.IsMetadata(property.propertyPath)) {
                        continue;
                    }

                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        !ids.Contains(property.objectReferenceInstanceIDValue)) {
                        continue;
                    }

                    CheckReference(owner.GetType(), property, targetType);
                    references.Add(new Reference {
                        owner = owner, path = property.propertyPath, sourceId = property.objectReferenceInstanceIDValue
                    });
                }
            }

            var objects = sources.Select(c => c.gameObject).ToArray();
            var remaining = sources.GroupBy(c => (c.gameObject, type: c.GetType()))
                .Select(group => (group.Key.gameObject, group.Key.type,
                    count: group.Key.gameObject.GetComponents(group.Key.type).Count(c => c.GetType() == group.Key.type)
                           - group.Count()))
                .ToArray();
            var sourceIds = sources.Select(c => c.GetInstanceID()).ToArray();
            var data = copyFields ? sources.Select(c => new ComponentData(c)).ToArray() : null;
            var replacements = new Dictionary<int, Component>();
            var useUndo = isolatedContext == null;
            var group = -1;

            if (useUndo) {
                Undo.IncrementCurrentGroup();
                group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
            }

            try {
                foreach (var source in sources) {
                    if (useUndo) {
                        Undo.DestroyObjectImmediate(source);
                    } else {
                        Object.DestroyImmediate(source);
                    }

                    if (source != null) {
                        throw new InvalidOperationException("Unity 拒绝移除组件，替换已取消。");
                    }
                }

                // 先创建全部 B，再恢复字段，确保自引用和 A 之间的互相引用都能找到目标。
                for (var i = 0; i < objects.Length; i++) {
                    var target = useUndo ? Undo.AddComponent(objects[i], targetType)
                        : objects[i].AddComponent(targetType);

                    if (target == null) {
                        throw new InvalidOperationException($"{objects[i].name} 无法添加 {targetType.Name}。");
                    }

                    replacements.Add(sourceIds[i], target);
                }

                // B 的 RequireComponent 可能又创建一个 A，不能把这种情况报告为替换成功。
                foreach (var expected in remaining) {
                    if (expected.gameObject.GetComponents(expected.type).Count(c => c.GetType() == expected.type)
                        != expected.count) {
                        throw new InvalidOperationException($"{targetType.Name} 的依赖重新添加了 A，无法完成替换。");
                    }
                }

                var copied = 0;

                for (var i = 0; i < objects.Length; i++) {
                    var target = replacements[sourceIds[i]];

                    if (data != null) {
                        copied += data[i].Restore(target, replacements, useUndo);
                    }

                    EditorUtility.SetDirty(target);

                    if (PrefabUtility.IsPartOfPrefabInstance(target)) {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    }
                }

                foreach (var reference in references) {
                    using var serialized = new SerializedObject(reference.owner);
                    var property = serialized.FindProperty(reference.path);

                    if (property == null) {
                        throw new InvalidOperationException($"引用字段已改变：{reference.path}");
                    }

                    property.objectReferenceValue = replacements[reference.sourceId];

                    if (useUndo) {
                        serialized.ApplyModifiedProperties();
                    } else {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    serialized.Update();

                    if (serialized.FindProperty(reference.path).objectReferenceValue
                        != replacements[reference.sourceId]) {
                        throw new InvalidOperationException($"引用无法承接 B：{reference.owner.name}.{reference.path}");
                    }
                }

                if (useUndo) {
                    foreach (var scene in objects.Select(go => go.scene).Distinct()) {
                        EditorSceneManager.MarkSceneDirty(scene);
                    }

                    Undo.CollapseUndoOperations(group);
                }

                if (outputReplacements != null) {
                    foreach (var pair in replacements) {
                        outputReplacements.Add(pair.Key, pair.Value);
                    }
                }

                return copied;
            } catch {
                if (useUndo) {
                    Undo.RevertAllDownToGroup(group);
                }

                throw;
            }
        }

        /// <summary>沿字段、数组/List 路径求实际引用类型。原生组件则使用 Unity 的 PPtr 类型信息。</summary>
        internal static void CheckReference(Type ownerType, SerializedProperty property, Type replacementType) {
            var type = ownerType;

            foreach (var part in property.propertyPath.Replace(".Array.data[", "[").Split('.')) {
                var bracket = part.IndexOf('[');
                var field = ComponentData.FindField(type, bracket < 0 ? part : part.Substring(0, bracket));

                if (field == null) {
                    type = null;
                    break;
                }

                type = field.FieldType;

                if (bracket >= 0) {
                    type = type.IsArray
                        ? type.GetElementType()
                        : type.IsGenericType && typeof(IList).IsAssignableFrom(type)
                            ? type.GetGenericArguments()[0]
                            : null;
                }

                if (type == null) {
                    break;
                }
            }

            if (type == null) {
                var name = property.type.Replace("PPtr<", "").Replace(">", "").TrimStart('$');
                type = name == "Object" ? typeof(Object) : TypeCache.GetTypesDerivedFrom<Object>()
                    .FirstOrDefault(candidate => candidate.Name == name);
            }

            if (type == null || !type.IsAssignableFrom(replacementType)) {
                throw new InvalidOperationException(
                    $"引用不兼容：{property.serializedObject.targetObject.name}.{property.propertyPath} " +
                    $"需要 {type?.Name ?? property.type}，不能改为 {replacementType.Name}。请先调整字段类型或解除该引用。");
            }
        }
    }
}