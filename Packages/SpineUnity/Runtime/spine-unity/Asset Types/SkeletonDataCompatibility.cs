/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated April 5, 2025. Replaces all prior versions.
 *
 * Copyright (c) 2013-2025, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software
 * or otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THE SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

//#define SPINE_ALLOW_UNSAFE // note: this define can be set via Edit - Preferences - Spine.

#if SPINE_ALLOW_UNSAFE
#define UNSAFE_DIRECT_ACCESS_TEXT_ASSET_DATA
#endif

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using System.Globalization;
using System.Text.RegularExpressions;
#endif

namespace Spine.Unity {
    public static class SkeletonDataCompatibility {
#if UNITY_EDITOR
        private static readonly int[][] s_CompatibleBinaryVersions = { new[] { 4, 2, 0 } };
        private static readonly int[][] s_CompatibleJsonVersions = { new[] { 4, 2, 0 } };

        private static bool s_WasVersionDialogShown = false;
        
        private static readonly Regex s_JsonVersionRegex = new Regex(
            @"""spine""\s*:\s*""([^""]+)""",
            RegexOptions.CultureInvariant
        );
#endif

        public enum SourceType {
            Json,
            Binary
        }

        [System.Serializable]
        public class VersionInfo {
            public string rawVersion = null;
            public int[] version = null;
            public SourceType sourceType;
        }

        [System.Serializable]
        public class CompatibilityProblemInfo {
            public VersionInfo actualVersion;
            public int[][] compatibleVersions;
            public string explicitProblemDescription = null;

            public string DescriptionString() {
                if (!string.IsNullOrEmpty(explicitProblemDescription)) {
                    return explicitProblemDescription;
                }

                string compatibleVersionString = "";
                string optionalOr = null;

                foreach (int[] version in compatibleVersions) {
                    compatibleVersionString += $"{optionalOr}{version[0]}.{version[1]}";
                    optionalOr = " or ";
                }

                return string.Format(
                    "Skeleton data could not be loaded. Data version: {0}. Required version: {1}.\nPlease re-export skeleton data with Spine {1} or change runtime to version {2}.{3}.",
                    actualVersion.rawVersion,
                    compatibleVersionString,
                    actualVersion.version[0],
                    actualVersion.version[1]
                );
            }
        }

#if UNITY_EDITOR
        public static VersionInfo GetVersionInfo(
            TextAsset asset,
            out bool isSpineSkeletonData,
            ref string problemDescription
        ) {
            isSpineSkeletonData = false;

            if (asset == null) {
                return null;
            }

            VersionInfo fileVersion = new VersionInfo();
            bool hasBinaryExtension = asset.name.Contains(".skel");
            fileVersion.sourceType = hasBinaryExtension ? SourceType.Binary : SourceType.Json;

            bool isJsonFileByContent = IsJsonFile(asset);

            if (hasBinaryExtension == isJsonFileByContent) {
                if (hasBinaryExtension) {
                    problemDescription = string.Format(
                        "Failed to read '{0}'. Extension is '.skel.bytes' but content looks like a '.json' file.\n"
                        + "Did you choose the wrong extension upon export?\n",
                        asset.name
                    );
                } else {
                    problemDescription = string.Format(
                        "Failed to read '{0}'. Extension is '.json' but content looks like binary 'skel.bytes' file.\n"
                        + "Did you choose the wrong extension upon export?\n",
                        asset.name
                    );
                }

                isSpineSkeletonData = false;
                return null;
            }

            if (fileVersion.sourceType == SourceType.Binary) {
                try {
#if UNSAFE_DIRECT_ACCESS_TEXT_ASSET_DATA
                    using (Stream stream = asset.GetStreamUnsafe()) {
#else
                    using (var stream = new MemoryStream(asset.bytes)) {
#endif
                        fileVersion.rawVersion = SkeletonBinary.GetVersionString(stream);
                    }
                } catch (System.Exception e) {
                    problemDescription = string.Format(
                        "Failed to read '{0}'. It is likely not a binary Spine SkeletonData file.\n{1}",
                        asset.name,
                        e
                    );

                    isSpineSkeletonData = false;
                    return null;
                }
            } else {
                Match match = s_JsonVersionRegex.Match(asset.text);

                if (match != null) {
                    fileVersion.rawVersion = match.Groups[1].Value;
                } else {
                    object obj = Json.Deserialize(new StringReader(asset.text));

                    if (obj == null) {
                        problemDescription = $"'{asset.name}' is not valid JSON.";
                        isSpineSkeletonData = false;
                        return null;
                    }

                    Dictionary<string, object> root = obj as Dictionary<string, object>;

                    if (root == null) {
                        problemDescription =
                            $"'{asset.name}' is not compatible JSON. Parser returned an incorrect type while parsing version info.";

                        isSpineSkeletonData = false;
                        return null;
                    }

                    if (root.ContainsKey("skeleton")) {
                        Dictionary<string, object> skeletonInfo = (Dictionary<string, object>)root["skeleton"];
                        object jv;
                        skeletonInfo.TryGetValue("spine", out jv);
                        fileVersion.rawVersion = jv as string;
                    }
                }
            }

            if (string.IsNullOrEmpty(fileVersion.rawVersion)) {
                // very likely not a Spine skeleton json file at all. Could be another valid json file, don't report errors.
                isSpineSkeletonData = false;
                return null;
            }

            string[] versionSplit = fileVersion.rawVersion.Split('.');

            try {
                fileVersion.version = new[] {
                    int.Parse(versionSplit[0], CultureInfo.InvariantCulture),
                    int.Parse(versionSplit[1], CultureInfo.InvariantCulture)
                };
            } catch (System.Exception e) {
                problemDescription = string.Format(
                    "Failed to read version info at skeleton '{0}'. It is likely not a valid Spine SkeletonData file.\n{1}",
                    asset.name,
                    e
                );

                isSpineSkeletonData = false;
                return null;
            }

            isSpineSkeletonData = true;
            return fileVersion;
        }

        public static bool IsJsonFile(TextAsset file) {
            var content = file.GetData<byte>();

            // check for binary skeleton version number string, starts after 8 byte hash
            char majorVersionChar = s_CompatibleBinaryVersions[0][0].ToString()[0];

            if (content.Length > 10 && content[9] == majorVersionChar && content[10] == '.') {
                return false;
            }

            const int maxCharsToCheck = 256;
            int numCharsToCheck = Math.Min(content.Length, maxCharsToCheck);
            int i = 0;

            // skip potential BOM
            if (content.Length >= 3
                && content[0] == 0xEF
                && content[1] == 0xBB
                && content[2] == 0xBF) {
                i = 3;
            }

            bool openingBraceFound = false;

            for (; i < numCharsToCheck; ++i) {
                char c = (char)content[i];

                if (char.IsWhiteSpace(c)) {
                    continue;
                }

                if (!openingBraceFound) {
                    if (c == '{' || c == '[') {
                        openingBraceFound = true;
                    } else {
                        return false;
                    }
                } else if (c == '{' || c == '[' || c == ']' || c == '}' || c == ',') {
                    continue;
                } else {
                    return c == '"';
                }
            }

            return true;
        }

        public static CompatibilityProblemInfo GetCompatibilityProblemInfo(VersionInfo fileVersion) {
            if (fileVersion == null) {
                return
                    null; // it's most likely not a Spine skeleton file, e.g. another json file. don't report problems.
            }

            var info = new CompatibilityProblemInfo();
            info.actualVersion = fileVersion;

            info.compatibleVersions = (fileVersion.sourceType == SourceType.Binary) ? s_CompatibleBinaryVersions
                : s_CompatibleJsonVersions;

            foreach (int[] compatibleVersion in info.compatibleVersions) {
                bool majorMatch = fileVersion.version[0] == compatibleVersion[0];
                bool minorMatch = fileVersion.version[1] == compatibleVersion[1];

                if (majorMatch && minorMatch) {
                    return null; // is compatible, thus no problem info returned
                }
            }

            return info;
        }

        public static void DisplayCompatibilityProblem(string descriptionString, TextAsset spineJson) {
            if (!s_WasVersionDialogShown) {
                s_WasVersionDialogShown = true;
                UnityEditor.EditorUtility.DisplayDialog("Version mismatch!", descriptionString, "OK");
            }

            Debug.LogError($"Error importing skeleton '{spineJson.name}': {descriptionString}", spineJson);
        }
#endif // UNITY_EDITOR
    }
}