using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ZStudio.UniKit.Editor {
    // 引用枚举成员，便于直接书写 Default、Red 等短名称。
    using static ConsoleUtils.ConsoleForeColor;

    /// <summary>终端 ANSI 颜色/转义序列与 Unity 富文本之间的转换工具。</summary>
    public static class ConsoleUtils {
        /// <summary>ANSI 基本前景色对应的整数值。</summary>
        public enum ConsoleForeColor {
            Default = 0,
            Black = 30,
            Red = 31,
            Green = 32,
            Yellow = 33,
            Blue = 34,
            Magenta = 35,
            Cyan = 36,
            White = 37,
        }

        /// <summary>颜色访问回调，输入前景色，返回替换后的富文本片段。</summary>
        public delegate string ColorMarkVisitor(ConsoleForeColor foreColor);

        /// <summary>当前平台的 PATH 环境变量分隔符。</summary>
        public const char PathSplitter =
#if UNITY_EDITOR_WIN
            ';';
#else
            ':';
#endif

        /// <summary>匹配 ANSI SGR 转义序列（如 ESC[31m、ESC[0m）。</summary>
        private static readonly Regex s_ColorPattern = new(@"\x1b\[([0-9;]*)m");

        /// <summary>严格 UTF-8 编码，用于校验字节序列的合法性。</summary>
        private static readonly Encoding s_StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>基本色 + 亮色共 16 色的十六进制表示。</summary>
        private static readonly string[] s_Colors = {
            "000000", "FF0000", "00FF00", "FFFF00", "0000FF", "FF00FF", "00FFFF", "FFFFFF", "808080", "FF5555",
            "55FF55", "FFFF55", "5555FF", "FF55FF", "55FFFF", "FFFFFF",
        };

        /// <summary>把 SGR 前景色转换为配对的 Unity 富文本。</summary>
        public static string ConvertToUnityColor(string input) {
            string color = null;
            return ConvertToUnityColor(input, ref color);
        }

        /// <summary>移除 SGR 颜色与样式转义序列。</summary>
        public static string ConvertToNoColor(string input) {
            if (input == null) {
                throw new ArgumentNullException(nameof(input));
            }

            return s_ColorPattern.Replace(input, "");
        }

        // 跨日志行保留前景色状态，同时为每一条发出的标签都做闭合。
        internal static string ConvertToUnityColor(string input, ref string color) {
            if (input == null) {
                throw new ArgumentNullException(nameof(input));
            }

            var result = new StringBuilder();
            var current = color;
            var position = 0;

            void AppendText(string text) {
                if (text.Length == 0) {
                    return;
                }

                // 每行是一条独立的 Unity 日志，需各自保证标签配对闭合。
                var lines = text.Split('\n');

                for (var i = 0; i < lines.Length; i++) {
                    if (i > 0) {
                        result.Append('\n');
                    }

                    if (lines[i].Length == 0) {
                        continue;
                    }

                    if (current != null) {
                        result.Append("<color=#").Append(current).Append('>');
                    }

                    result.Append(lines[i]);

                    if (current != null) {
                        result.Append("</color>");
                    }
                }
            }

            foreach (Match match in s_ColorPattern.Matches(input)) {
                AppendText(input.Substring(position, match.Index - position));
                VisitCodes(match.Groups[1].Value, code => {
                    if (code == 0 || code == 39) {
                        // 重置前景色。
                        current = null;
                    } else if (code >= 30 && code <= 37) {
                        // 基本色。
                        current = s_Colors[code - 30];
                    } else if (code >= 90 && code <= 97) {
                        // 亮色。
                        current = s_Colors[code - 90 + 8];
                    }
                }, value => current = value);
                position = match.Index + match.Length;
            }

            AppendText(input.Substring(position));
            color = current;
            return result.ToString();
        }

        /// <summary>按原始顺序访问受支持的基本前景色/重置指令，由 visitor 自行管理标签。</summary>
        public static string ScanColorLog(string input, ColorMarkVisitor visitor) {
            if (input == null) {
                throw new ArgumentNullException(nameof(input));
            }

            if (visitor == null) {
                throw new ArgumentNullException(nameof(visitor));
            }

            return s_ColorPattern.Replace(input, match => {
                var result = new StringBuilder();

                VisitCodes(match.Groups[1].Value, code => {
                    if (code is 0 or 39) {
                        result.Append(visitor(Default));
                    } else if (code is >= 30 and <= 37) {
                        result.Append(visitor((ConsoleForeColor)code));
                    }
                }, _ => { });

                return result.ToString();
            });
        }

        /// <summary>
        /// 解析 SGR 参数分号列表，对每个前景色码调用 visit，对 256 色 / RGB 调用 extendedColor。
        /// </summary>
        private static void VisitCodes(string parameters, Action<int> visit, Action<string> extendedColor) {
            var codes = parameters.Split(';');

            for (var i = 0; i < codes.Length; i++) {
                var code = 0;

                if (codes[i].Length != 0 && !int.TryParse(codes[i], out code)) {
                    continue;
                }

                // 38=前景扩展色、48=背景扩展色、58=下划线扩展色。
                if (code != 38 && code != 48 && code != 58) {
                    visit(code);
                    continue;
                }

                // 一并消费掉背景色/下划线色的参数，避免它们的数值
                // 意外被当作前景色重置或改变颜色。
                if (++i >= codes.Length) {
                    break;
                }

                if (codes[i] == "5") {
                    // 256 色（索引色）。
                    if (++i >= codes.Length) {
                        break;
                    }

                    if (code == 38 && byte.TryParse(codes[i], out var index)) {
                        if (index < 16) {
                            extendedColor(s_Colors[index]);
                        } else if (index >= 232) {
                            // 灰度区。
                            var gray = 8 + (index - 232) * 10;
                            extendedColor($"{gray:X2}{gray:X2}{gray:X2}");
                        } else {
                            // 6x6x6 色立方。
                            var n = index - 16;
                            int Component(int value) => value == 0 ? 0 : 55 + value * 40;
                            extendedColor($"{Component(n / 36):X2}{Component(n / 6 % 6):X2}{Component(n % 6):X2}");
                        }
                    }
                } else if (codes[i] == "2") {
                    // 真彩色（RGB），需要再消费 3 个数值。
                    if (i + 3 >= codes.Length) {
                        break;
                    }

                    if (code == 38 && byte.TryParse(codes[i + 1], out var r)
                                   && byte.TryParse(codes[i + 2], out var g)
                                   && byte.TryParse(codes[i + 3], out var b)) {
                        extendedColor($"{r:X2}{g:X2}{b:X2}");
                    }

                    i += 3;
                } else {
                    break;
                }
            }
        }

        /// <summary>默认的 visitor：把前景色转成 Unity 富文本的 &lt;color&gt; 标签。</summary>
        public static string DefaultColorMarkVisitor(ConsoleForeColor foreColor) {
            if (foreColor == Default) {
                return "</color>";
            }

            var index = (int)foreColor - 30;
            return index >= 0 && index < 8 ? $"<color=#{s_Colors[index]}>" : "";
        }

        /// <summary>校验一段完整的 UTF-8 字节区间，包括 Unicode 标量边界。允许 NUL。</summary>
        public static bool IsValidUTF8(byte[] bytes, int index, int count) {
            // GetCharCount 会校验参数并拒绝不完整/非法的序列。
            try {
                var charCount = s_StrictUtf8.GetCharCount(bytes, index, count);
                return true;
            } catch (DecoderFallbackException) {
                return false;
            }
        }
    }
}