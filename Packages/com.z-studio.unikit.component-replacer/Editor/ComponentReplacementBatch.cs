using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZStudio.UniKit.Editor {
    internal sealed class ComponentReplacementResult {
        internal int sceneComponents;
        internal int assetComponents;
        internal int assets;
        internal int inherited;
        internal int copiedFields;
        internal bool cancelled;
        internal readonly List<string> issues = new();
    }

    /// <summary>
    /// 对预览中勾选的条目按文件执行；每个 Prefab 只加载、保存一次。
    /// 场景对象保持 Unity Undo；资产保存前生成备份，单文件失败不写入部分迁移结果。
    /// </summary>
    internal static class ComponentReplacementBatch {
        internal static ComponentReplacementResult Apply(ComponentReplacementPlan plan,
            Action<string, float> progress = null) {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) {
                throw new InvalidOperationException("请在编辑模式且编译结束后使用。");
            }

            var result = new ComponentReplacementResult();
            var selected = plan.entries.Where(entry => entry.selected && entry.error == null).ToArray();
            var disk = selected.Where(entry => entry.IsAsset).GroupBy(entry => entry.assetPath)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var referenceAssets = plan.ReferenceAssets(selected).ToHashSet(StringComparer.Ordinal);

            foreach (var path in referenceAssets) {
                if (!disk.ContainsKey(path)) {
                    disk.Add(path, Array.Empty<ComponentReplacementEntry>());
                }
            }

            var paths = OrderDependencies(disk.Keys);
            var live = selected.Where(entry => !entry.IsAsset).ToArray();

            if (live.Any(entry => entry.source == null || EditorJsonUtility.ToJson(entry.source) != entry.sceneState)) {
                result.issues.Add("场景对象在扫描后已改变，本次未修改场景对象，请重新扫描。");
                live = Array.Empty<ComponentReplacementEntry>();
            }

            try {
                // 在保存任何源 Prefab 之前校验所有快照，避免继承传播被误判为用户修改。
                foreach (var path in paths.ToArray()) {
                    progress?.Invoke("检查 " + path, 0);
                    var error = ComponentReplacementPlan.AssetError(path);

                    if (error == null && ComponentReplacementPlan.Fingerprint(path) != plan.fingerprints[path]) {
                        error = "扫描后资产或依赖已改变，请重新扫描。";
                    }

                    if (error == null) {
                        continue;
                    }

                    if (referenceAssets.Contains(path)) {
                        throw new InvalidOperationException($"{path} 持有需迁移的引用：{error}");
                    }

                    result.issues.Add($"{path}：{error}");
                    disk.Remove(path);
                }

                paths.RemoveAll(path => !disk.ContainsKey(path));
                var assetIds = selected.Where(entry => entry.IsAsset && disk.ContainsKey(entry.assetPath))
                    .Select(entry => entry.sourceId).ToHashSet();

                foreach (var path in paths) {
                    plan.prefabs[path].ValidateReferences(assetIds, plan.targetType);
                }

                var backup = paths.Count == 0 ? null : new ComponentAssetBackup(paths);
                var completed = new HashSet<string>(StringComparer.Ordinal);
                var assetReferences = new Dictionary<int, Object>();

                for (var i = 0; i < paths.Count; i++) {
                    var path = paths[i];
                    progress?.Invoke("替换 " + path, (float)i / Math.Max(1, paths.Count));

                    try {
                        ApplyPrefab(path, disk[path], plan, completed, assetReferences, backup, result);
                        completed.Add(path);
                    } catch (Exception exception) {
                        result.issues.Add($"{path}：{exception.Message}");
                    }
                }

                if (live.Length > 0
                    || plan.sceneReferences.Any(reference => assetReferences.ContainsKey(reference.targetId))) {
                    progress?.Invoke("替换场景对象", 1);

                    try {
                        ApplyScenes(live, plan, assetReferences, result);
                    } catch (Exception exception) {
                        result.issues.Add($"场景对象：{exception.Message}");
                    }
                }
            } catch (OperationCanceledException) {
                result.cancelled = true;
            }

            return result;
        }

        private static void ApplyScenes(ComponentReplacementEntry[] entries, ComponentReplacementPlan plan,
            Dictionary<int, Object> assetReferences, ComponentReplacementResult result) {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(ComponentReplacement.UndoName);

            try {
                var direct = entries.Where(entry => entry.source != null && entry.source.GetType() == plan.sourceType)
                    .ToArray();
                var inherited = entries.Except(direct).ToArray();
                var map = new Dictionary<int, Object>(assetReferences);

                foreach (var entry in inherited) {
                    var target = entry.gameObject != null ? entry.gameObject.GetComponent(plan.targetType) : null;

                    if (target == null) {
                        throw new InvalidOperationException("场景组件已改变，请重新扫描。");
                    }

                    map[entry.sourceId] = target;
                }

                var replacements = new Dictionary<int, Component>();
                ComponentReplacement.Apply(direct.Select(entry => entry.source).ToArray(), plan.targetType, false,
                    outputReplacements: replacements);

                foreach (var pair in replacements) {
                    map[pair.Key] = pair.Value;
                }

                var copied = 0;

                foreach (var entry in entries) {
                    var target = (Component)map[entry.sourceId];

                    if (plan.copyFields) {
                        copied += entry.sceneData.Restore(target, replacements, true, map);
                    }

                    EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                }

                foreach (var reference in plan.sceneReferences) {
                    if (!map.TryGetValue(reference.targetId, out var target) || target == null) {
                        continue;
                    }

                    var owner = reference.owner;

                    if (owner == null && map.TryGetValue(reference.ownerId, out var mappedOwner)) {
                        owner = mappedOwner as Component;
                    }

                    if (owner == null
                        || !plan.copyFields && entries.Any(entry => entry.sourceId == reference.ownerId)) {
                        continue;
                    }

                    using var serialized = new SerializedObject(owner);
                    var property = serialized.FindProperty(reference.path);

                    // A 独有的字段不需要恢复到 B。
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) {
                        continue;
                    }

                    ComponentReplacement.CheckReference(owner.GetType(), property, target.GetType());
                    property.objectReferenceValue = target;
                    serialized.ApplyModifiedProperties();
                }

                Undo.CollapseUndoOperations(group);
                result.sceneComponents += entries.Length;
                result.inherited += inherited.Length;
                result.copiedFields += copied;
            } catch {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static void ApplyPrefab(string path, ComponentReplacementEntry[] entries, ComponentReplacementPlan plan,
            HashSet<string> completed, Dictionary<int, Object> assetReferences, ComponentAssetBackup backup,
            ComponentReplacementResult result) {
            GameObject root = null;
            var saveStarted = false;

            try {
                root = PrefabUtility.LoadPrefabContents(path);
                var sources = new List<Component>();
                var inherited = new List<ComponentReplacementEntry>();
                var map = plan.prefabs[path].Map(root, plan.targetType);

                foreach (var pair in assetReferences) {
                    if (!map.ContainsKey(pair.Key)) {
                        map.Add(pair.Key, pair.Value);
                    }
                }

                var originalSources = new Dictionary<int, int>();
                var inheritedChange = AssetDatabase.GetDependencies(path).Any(completed.Contains);

                foreach (var entry in entries) {
                    var source = entry.Resolve(root);

                    if (source != null && source.GetType() == plan.sourceType) {
                        sources.Add(source);
                        originalSources.Add(entry.sourceId, source.GetInstanceID());
                    } else if (inheritedChange) {
                        // 同时选中了源和 Variant/嵌套使用者时，源的变化可能已由 Unity 继承。
                        var transform = root.transform;

                        foreach (var index in entry.siblingPath) {
                            if (index >= transform.childCount) throw new InvalidOperationException("对象结构改变，请重新扫描。");
                            transform = transform.GetChild(index);
                        }

                        if (transform.GetComponent(plan.targetType) == null) {
                            throw new InvalidOperationException("组件结构改变，请重新扫描。");
                        }

                        inherited.Add(entry);
                    } else {
                        throw new InvalidOperationException("组件结构改变，请重新扫描。");
                    }
                }

                var context = root.GetComponentsInChildren<Transform>(true).Select(transform => transform.gameObject)
                    .ToArray();
                var replacements = new Dictionary<int, Component>();
                ComponentReplacement.Apply(sources, plan.targetType, false, context, replacements);

                foreach (var pair in originalSources) {
                    map[pair.Key] = replacements[pair.Value];
                }

                var copied = plan.prefabs[path].RestoreData(entries, map, plan.copyFields);
                var referenceChanges = plan.prefabs[path].RestoreReferences(map);

                if (entries.Length == 0 && referenceChanges == 0) {
                    return;
                }

                saveStarted = true;
                PrefabUtility.SaveAsPrefabAsset(root, path, out var success);

                if (!success) {
                    throw new InvalidOperationException("Prefab 保存失败。");
                }

                // 重新获取持久化目标，跨 Prefab 的引用不能指向另一个临时加载场景。
                var persistent = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var persistentMap = ComponentPrefabSnapshot.PersistentMap(root, persistent, map);
                backup.Saved(path);

                foreach (var pair in persistentMap) {
                    assetReferences[pair.Key] = pair.Value;
                }

                result.assets++;
                result.assetComponents += sources.Count;
                result.inherited += inherited.Count;
                result.copiedFields += copied;
            } catch {
                if (saveStarted) {
                    backup.RevertFailedSave(path);
                }

                throw;
            } finally {
                if (root != null) {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static List<string> OrderDependencies(IEnumerable<string> paths) {
            var included = paths.ToHashSet(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();

            void Visit(string path) {
                if (!visited.Add(path)) {
                    return;
                }

                foreach (var dependency in AssetDatabase.GetDependencies(path, false).Where(included.Contains)) {
                    Visit(dependency);
                }

                ordered.Add(path);
            }

            foreach (var path in included.OrderBy(path => path, StringComparer.Ordinal)) {
                Visit(path);
            }

            return ordered;
        }
    }
}