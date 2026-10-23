using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 磁盘保存不属于 Unity Undo。每次资产批处理独立备份，最后一次成功批次可跨脚本重载还原。
    /// 还原前核对保存后的内容，防止覆盖用户在替换之后作出的其他修改。
    /// </summary>
    internal sealed class ComponentAssetBackup {
        [Serializable]
        private sealed class FileRecord {
            public string path;
            public string backup;
            public string guid;
            public string savedHash;
        }

        [Serializable]
        private sealed class Journal {
            public List<FileRecord> files = new();
        }

        private const string k_DirectoryPath = "Library/UniKit/ComponentReplacer";
        private const string k_LatestPath = k_DirectoryPath + "/latest.json";
        private readonly Journal m_Journal = new();
        internal static bool CanRestore => File.Exists(k_LatestPath);

        internal ComponentAssetBackup(IEnumerable<string> paths) {
            var directory = k_DirectoryPath + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-"
                            + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);

            foreach (var path in paths) {
                var backup = directory + "/" + m_Journal.files.Count + ".prefab";
                File.Copy(ComponentReplacementPlan.FilePath(path), backup);
                
                m_Journal.files.Add(new FileRecord {
                    path = path, backup = backup, guid = AssetDatabase.AssetPathToGUID(path)
                });
            }
        }

        internal void Saved(string path) {
            m_Journal.files.Single(record => record.path == path).savedHash = Hash(path);
            var temporary = k_LatestPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(m_Journal, true));

            if (File.Exists(k_LatestPath)) {
                File.Replace(temporary, k_LatestPath, null);
            } else {
                File.Move(temporary, k_LatestPath);
            }
        }

        internal void RevertFailedSave(string path) {
            File.Copy(m_Journal.files.Single(record => record.path == path).backup,
                ComponentReplacementPlan.FilePath(path), true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        internal static int Restore() {
            if (!CanRestore) {
                throw new InvalidOperationException("没有可还原的资产替换。");
            }

            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(k_LatestPath));
            var records = journal.files.Where(record => !string.IsNullOrEmpty(record.savedHash)).ToArray();

            foreach (var record in records) {
                var error = ComponentReplacementPlan.AssetError(record.path);

                if (error != null) {
                    throw new InvalidOperationException($"{record.path}：{error}");
                }

                if (!File.Exists(record.backup) || AssetDatabase.AssetPathToGUID(record.path) != record.guid) {
                    throw new InvalidOperationException($"{record.path}：资产身份或备份已改变，无法还原。");
                }

                if (Hash(record.path) != record.savedHash) {
                    throw new InvalidOperationException(
                        $"{record.path}：替换后又有修改，未覆盖该文件。可从 Library/UniKit/ComponentReplacer 中手动取回备份。");
                }
            }

            // 先校验全部文件，再统一还原；I/O 失败时恢复已经写入的文件。
            var current = records.ToDictionary(record => record.path,
                record => File.ReadAllBytes(ComponentReplacementPlan.FilePath(record.path)));

            try {
                foreach (var record in records) {
                    File.Copy(record.backup, ComponentReplacementPlan.FilePath(record.path), true);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            } catch {
                foreach (var pair in current) {
                    File.WriteAllBytes(ComponentReplacementPlan.FilePath(pair.Key), pair.Value);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                throw;
            }

            File.Delete(k_LatestPath);
            return records.Length;
        }

        private static string Hash(string path) =>
            Hash128.Compute(File.ReadAllBytes(ComponentReplacementPlan.FilePath(path))).ToString();
    }
}