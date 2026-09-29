using System.Text;

namespace ZStudio.UniKit.Editor {
    /// <summary>一次 Shell 执行的最终结果，包含退出码与原始输出/错误文本。</summary>
    public class ShellResult {
        private string m_Output;
        private string m_Error;
        internal readonly StringBuilder outputBuilder;
        internal readonly StringBuilder errorBuilder;

        /// <summary>标准输出全文（懒计算，之后保持稳定）。</summary>
        public string Output => m_Output ??= outputBuilder.ToString();

        /// <summary>标准错误全文（懒计算，之后保持稳定）。</summary>
        public string Error => m_Error ??= errorBuilder.ToString();

        /// <summary>执行的命令文本（用于日志展示）。</summary>
        public string Command { get; private set; }

        /// <summary>进程退出码。</summary>
        public int ExitCode { get; private set; }

        internal ShellResult(string cmd) {
            Command = cmd;
            outputBuilder = new StringBuilder();
            errorBuilder = new StringBuilder();
            m_Output = m_Error = null;
        }

        internal void NotifyComplete(int exitCode) {
            ExitCode = exitCode;
        }

        // 原样保留输出的原始分块，包括原始的换行符与未换行的残余片段。
        internal void AppendLine(LogEventType type, string line) {
            if (type == LogEventType.InfoLog) {
                outputBuilder.Append(line);
                m_Output = null;
            }

            if (type == LogEventType.ErrorLog) {
                errorBuilder.Append(line);
                m_Error = null;
            }
        }
    }
}