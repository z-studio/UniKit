using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.PackageManager;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ZStudio.UniKit.Editor {
    internal enum ComponentReplacementScope {
        Selection,
        Specified,
        OpenScenes,
        ProjectPrefabs
    }

    /// <summary>
    /// 预览只保留存活对象或资产内的定位信息；不长期持有 LoadPrefabContents 创建的临时场景。
    /// Locator 使用 sibling index 和组件槽位，允许同名对象，并能精确恢复用户勾选的组件。
    /// </summary>
    internal sealed class ComponentReplacementEntry {
        internal Component source;
        internal string assetPath;
        internal string objectPath;
        internal string fingerprint;
        internal int[] siblingPath;
        internal int componentIndex;
        internal bool selected = true;
        internal string error;
        internal ComponentData sceneData;
        internal string sceneState;
        internal int sourceId;
        internal GameObject gameObject;
        internal bool IsAsset => !string.IsNullOrEmpty(assetPath);

        internal Component Resolve(GameObject root) {
            if (!IsAsset) {
                return source;
            }

            var transform = root.transform;

            foreach (var index in siblingPath) {
                if (index >= transform.childCount) {
                    return null;
                }

                transform = transform.GetChild(index);
            }

            var components = transform.GetComponents<Component>();
            return componentIndex < components.Length ? components[componentIndex] : null;
        }
    }

    internal sealed class ComponentReplacementPlan {
        internal sealed class LiveReference {
            internal Component owner;
            internal int ownerId;
            internal string path;
            internal int targetId;
        }

        internal readonly List<LiveReference> sceneReferences = new();
        internal readonly List<ComponentReplacementEntry> entries = new();
        internal readonly List<string> issues = new();
        internal readonly Dictionary<string, ComponentPrefabSnapshot> prefabs = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
        internal Type sourceType;
        internal Type targetType;
        internal bool copyFields;

        internal static ComponentReplacementPlan Scan(Type sourceType, Type targetType, bool copyFields,
            ComponentReplacementScope scope, IEnumerable<Object> roots, Action<string, float> progress = null) {
            var typeError = ComponentReplacement.ValidateType(sourceType)
                            ?? ComponentReplacement.ValidateType(targetType);

            if (typeError != null || sourceType == targetType) {
                throw new InvalidOperationException(typeError ?? "A 和 B 不能相同。");
            }

            var plan = new ComponentReplacementPlan {
                sourceType = sourceType, targetType = targetType, copyFields = copyFields
            };
            var objects = new HashSet<GameObject>();
            var assets = new HashSet<string>(StringComparer.Ordinal);

            switch (scope) {
                case ComponentReplacementScope.OpenScenes:
                    objects.UnionWith(ComponentReplacement.ContextObjects());
                    break;
                case ComponentReplacementScope.ProjectPrefabs:
                    AddFolder("Assets", assets);
                    break;
                default: {
                    foreach (var input in roots.Where(value => value != null)) {
                        var gameObject = input is Component component ? component.gameObject : input as GameObject;

                        if (gameObject != null) {
                            if (!EditorUtility.IsPersistent(gameObject)) {
                                objects.UnionWith(gameObject.GetComponentsInChildren<Transform>(true)
                                    .Select(t => t.gameObject));
                            } else {
                                var path = AssetDatabase.GetAssetPath(gameObject);

                                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                                    assets.Add(path);
                                } else {
                                    plan.issues.Add($"不支持的资源：{path}（请选择 .prefab、文件夹或场景对象）");
                                }
                            }
                        } else {
                            var path = AssetDatabase.GetAssetPath(input);

                            if (AssetDatabase.IsValidFolder(path)) {
                                AddFolder(path, assets);
                            } else {
                                plan.issues.Add($"已忽略：{input.name}（请选择 Prefab、文件夹或场景对象）");
                            }
                        }
                    }

                    break;
                }
            }

            foreach (var gameObject in objects) {
                plan.AddObject(gameObject, null, null, null);
            }

            var paths = assets.OrderBy(path => path, StringComparer.Ordinal).ToArray();

            for (var i = 0; i < paths.Length; i++) {
                var path = paths[i];
                progress?.Invoke(path, (float)i / Math.Max(1, paths.Length));

                try {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (root == null) {
                        throw new InvalidOperationException("无法加载 Prefab。");
                    }

                    var error = AssetError(path);
                    var fingerprint = Fingerprint(path);
                    plan.prefabs.Add(path, new ComponentPrefabSnapshot(root, sourceType));
                    plan.fingerprints.Add(path, fingerprint);

                    foreach (var transform in root.GetComponentsInChildren<Transform>(true)) {
                        plan.AddObject(transform.gameObject, root, fingerprint, error);
                    }
                } catch (Exception exception) {
                    plan.issues.Add($"{path}：{exception.Message}");
                }
            }

            var sourceIds = plan.entries.Select(entry => entry.sourceId).ToHashSet();

            foreach (var owner in ComponentReplacement.ContextObjects().SelectMany(go => go.GetComponents<Component>())
                         .Where(c => c != null)) {
                using var serialized = new SerializedObject(owner);
                var property = serialized.GetIterator();

                while (property.Next(true)) {
                    if (ComponentData.IsMetadata(property.propertyPath)) {
                        continue;
                    }

                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && sourceIds.Contains(property.objectReferenceInstanceIDValue)) {
                        plan.sceneReferences.Add(new LiveReference {
                            owner = owner,
                            ownerId = owner.GetInstanceID(),
                            path = property.propertyPath,
                            targetId = property.objectReferenceInstanceIDValue
                        });
                    }
                }
            }

            return plan;
        }

        internal string[] ReferenceAssets(IEnumerable<ComponentReplacementEntry> selected) {
            var ids = selected.Where(entry => entry.IsAsset).Select(entry => entry.sourceId).ToHashSet();
            return prefabs.Where(pair => pair.Value.ReferencesAny(ids)).Select(pair => pair.Key).ToArray();
        }

        private static void AddFolder(string folder, HashSet<string> paths) {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder })) {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                    paths.Add(path);
                }
            }
        }

        private void AddObject(GameObject gameObject, GameObject assetRoot, string fingerprint, string assetError) {
            var components = gameObject.GetComponents<Component>();

            for (var i = 0; i < components.Length; i++) {
                var source = components[i];

                if (source == null || source.GetType() != sourceType) {
                    continue;
                }

                var indices = new List<int>();

                if (assetRoot != null) {
                    for (var parent = gameObject.transform; parent != assetRoot.transform; parent = parent.parent) {
                        indices.Add(parent.GetSiblingIndex());
                    }

                    indices.Reverse();
                }

                var entry = new ComponentReplacementEntry {
                    sceneData = assetRoot == null ? new ComponentData(source) : null,
                    sceneState = assetRoot == null ? EditorJsonUtility.ToJson(source) : null,
                    source = source,
                    sourceId = source.GetInstanceID(),
                    gameObject = gameObject,
                    assetPath = assetRoot == null ? null : AssetDatabase.GetAssetPath(assetRoot),
                    objectPath = HierarchyPath(gameObject.transform),
                    fingerprint = fingerprint,
                    siblingPath = indices.ToArray(),
                    componentIndex = i,
                    error = assetError ?? ComponentReplacement.Validate(source, targetType, assetRoot != null)
                };
                entry.selected = entry.error == null;
                entries.Add(entry);
            }
        }

        internal static string HierarchyPath(Transform transform) =>
            transform.parent == null
                ? transform.name : $"{HierarchyPath(transform.parent)} / {transform.name}";

        internal static string Fingerprint(string path) =>
            AssetDatabase.GetAssetDependencyHash(path) + ":" +
            Hash128.Compute(File.ReadAllBytes(FilePath(path)));

        internal static string FilePath(string path) {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) {
                return Path.GetFullPath(path);
            }

            var package = PackageInfo.FindForAssetPath(path);

            if (package == null) {
                throw new InvalidOperationException("无法定位包资源的磁盘路径。");
            }

            return Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length).TrimStart('/'));
        }

        internal static string AssetError(string path) {
            if (path.StartsWith("Packages/", StringComparison.Ordinal)) {
                var package = PackageInfo.FindForAssetPath(path);

                if (package == null
                    || package.source != PackageSource.Embedded && package.source != PackageSource.Local) {
                    return "只读包资源不支持修改；嵌入包和本地包可直接处理。";
                }
            }

            var file = FilePath(path);

            if (!File.Exists(file)) {
                return "资产已不存在，请重新扫描。";
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null && AssetDatabase.GetDependencies(stage.assetPath).Contains(path)) {
                return "该资产正在 Prefab Mode 中编辑或被当前 Prefab 依赖；请退出 Prefab Mode 后直接批量处理。";
            }

            if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0 ||
                !AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.ForceUpdate)) {
                return "资产只读或尚未从版本控制签出。";
            }

            return null;
        }
    }
}