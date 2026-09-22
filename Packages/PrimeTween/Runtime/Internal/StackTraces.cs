#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using UnityEngine;

namespace PrimeTween {
    internal static class StackTraces {
        private static readonly List<int> s_IdToHash = new(1000);
        private static readonly Dictionary<int, List<byte[]>> s_HashToTraces = new(100);
        private static bool s_DidWarn;

        /// https://github.com/Unity-Technologies/UnityCsReference/blob/6230ef8f9bed142ddf6a5e338d6e0faf3368d313/Runtime/Export/Scripting/StackTrace.cs#L31-L47
        internal static unsafe void Record(long id) {
            if (!s_DidWarn && EnableIL2CPP) {
                s_DidWarn = true;

                Debug.LogWarning(
                    "PRIME_TWEEN_SAFETY_CHECKS is used in IL2CPP build, which has negative performance impact in Development Builds. "
                    + "Please remove the PRIME_TWEEN_SAFETY_CHECKS from 'Project Settings/Player/Scripting Define Symbols' if you no longer need deep debugging support."
                );
            }

            if (id == 1) {
                s_IdToHash.Clear();
                s_IdToHash.Add(0);
            }

            const int bufLength = 16 * 1024;
            byte* buf = stackalloc byte[bufLength];
            int len = Debug.ExtractStackTraceNoAlloc(buf, bufLength, Application.dataPath);

            if (len <= 0) {
                // ExtractStackTraceNoAlloc() doesn't work with IL2CPP, so use the allocating version instead
                Fallback();
            }

            void Fallback() {
                string trace = StackTraceUtility.ExtractStackTrace();

                fixed (char* chars = trace) {
                    Encoding.UTF8.GetEncoder()
                            .Convert(chars, trace.Length, buf, bufLength, true, out _, out len, out _);
                }
            }

            int hash = ComputeHash(buf, len);
            Assert.AreEqual(id, s_IdToHash.Count);
            s_IdToHash.Add(hash);

            if (s_HashToTraces.TryGetValue(hash, out var traces)) {
                if (!Contains(traces, buf, len)) {
                    traces.Add(BufToArray());
                }
            } else {
                s_HashToTraces.Add(hash, new List<byte[]> { BufToArray() });
            }

            byte[] BufToArray() {
                var result = new byte[len];

                for (int i = 0; i < len; i++) {
                    result[i] = buf[i];
                }

                return result;
            }
        }

        private static bool EnableIL2CPP {
            get {
#if ENABLE_IL2CPP
                return true;
#else
                return false;
#endif
            }
        }

        private static unsafe bool Contains([NotNull] List<byte[]> arrays, byte* data, int length) {
            foreach (var arr in arrays) {
                if (arr.Length == length) {
                    if (SequenceEqual()) {
                        return true;
                    }

                    bool SequenceEqual() {
                        for (int i = 0; i < length; i++) {
                            if (arr[i] != data[i]) {
                                return false;
                            }
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        /// https://stackoverflow.com/a/468084
        private static unsafe int ComputeHash(byte* data, int length) {
            unchecked {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < length; i++) {
                    hash = (hash ^ data[i]) * p;
                }

                return hash;
            }
        }

        [NotNull]
        internal static string Get(long id) {
            Assert.IsTrue(id < s_IdToHash.Count);

            // p1 todo limit the max number of stack traces or wrap them around max value
            bool isSuccess = s_HashToTraces.TryGetValue(s_IdToHash[(int)id], out var traces);
            Assert.IsTrue(isSuccess);
            Assert.IsNotNull(traces);

            return string.Join(
                "\n\n",
                traces.Select(bytes => {
                        string str = Encoding.UTF8.GetString(bytes);
                        Assert.IsFalse(string.IsNullOrEmpty(str));
                        int i = 0;

                        while (true) {
                            var prev = i;
                            i = str.IndexOf('\n', i);

                            if (i == -1) {
                                return str;
                            }

                            i++;
                            int j = str.IndexOf("PrimeTween.", i, StringComparison.Ordinal);

                            if (j != i) {
                                return str.Substring(prev);
                            }
                        }
                    }
                )
            );
        }
    }
}
#endif