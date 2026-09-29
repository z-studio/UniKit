using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 日志事件的类型。
    /// </summary>
    public enum LogEventType {
        /// <summary>标准输出（stdout）普通信息。</summary>
        InfoLog,

        /// <summary>警告信息。</summary>
        WarnLog,

        /// <summary>标准错误（stderr）信息。</summary>
        ErrorLog,

        /// <summary>流结束标记，代表某次请求的全部输出已交付。</summary>
        EndStream,
    }

    /// <summary>
    /// 一次 Shell 执行的可配置参数。未显式赋值时使用 <see cref="Default"/> 的默认值。
    /// </summary>
    public struct ShellSettings {
        /// <summary>工作目录。默认 "."（Unity 当前目录）。</summary>
        public string WorkDirectory;

        /// <summary>额外的环境变量，键值对形式；同名键会覆盖默认环境。</summary>
        public Dictionary<string, string> Environment;

        /// <summary>为 true 时关闭本库自动输出的日志。</summary>
        public bool Quiet;

        /// <summary>stdout/stderr 的解码编码。为 null 时使用 UTF-8。</summary>
        public Encoding OutputEncoding;

        /// <summary>stdin 的写入编码。为 null 时使用不带 BOM 的 UTF-8。</summary>
        public Encoding InputEncoding;

        /// <summary>为 true 时，进程以非零退出码结束时抛出异常。</summary>
        public bool ThrowOnNonZeroExitCode;

        /// <summary>执行期间显示 Unity 进度条。</summary>
        public bool WithProgress;

        /// <summary>进度条是否允许手动取消。</summary>
        public bool ProgressCancelable;

        /// <summary>Shell 结束后是否自动清除当前显示的进度条。</summary>
        public bool AutoClearProgress;

        /// <summary>返回一组推荐的默认配置。</summary>
        public static ShellSettings Default =>
            new() {
                WorkDirectory = ".",
                Environment = null,
                Quiet = false,
                WithProgress = false,
                ProgressCancelable = true,
                AutoClearProgress = true,
                ThrowOnNonZeroExitCode = true,
            };
    }


    public static class Shell {
        /// <summary>全局默认环境变量，需在主线程配置。</summary>
        public static readonly Dictionary<string, string> DefaultEnvironment = new();

        internal static readonly ConcurrentQueue<(ShellRequest req, LogEventType type, object arg)> Queue = new();

        internal const int OutputQueueCapacity = 1024;

        /// <summary>
        /// 待分发输出事件的信号量槽位。槽位耗尽时后台读取会阻塞，
        /// 从而限制“生产快于消费”时的内存积压。
        /// </summary>
        private static readonly SemaphoreSlim s_OutputSlots = new(OutputQueueCapacity, OutputQueueCapacity);

        internal static readonly ConcurrentQueue<(ShellRequest req, LogEventType type, object arg)> PriorityQueue =
            new();

        /// <summary>
        /// 将一条输出文本放入待分发队列。先等待可用槽位，若等待期间被取消则抛出
        /// <see cref="OperationCanceledException"/>。
        /// </summary>
        internal static async Task EnqueueOutputAsync(ShellRequest request, LogEventType type, string text) {
            await s_OutputSlots.WaitAsync(request.CancellationToken).ConfigureAwait(false);

            if (request.IsCancellationRequested) {
                s_OutputSlots.Release();
                throw new OperationCanceledException(request.CancellationToken);
            }

            Queue.Enqueue((request, type, text));
        }

        // 保留用于源码兼容；实际的输出缓冲由每个请求自行持有。
        private static readonly HashSet<ShellRequest> s_Requests = new();

        /// <summary>当前处于 onLog 回调中的嵌套深度，用于禁止在回调里同步调用 Wait()。</summary>
        internal static int LogCallbackDepth;

        static Shell() {
            // 每帧主线程回调：把后台排队的事件分发到对应请求。
            EditorApplication.update += DumpQueue;
            
            // 程序集重载与 Editor 退出前，请求取消所有活动进程。
            AssemblyReloadEvents.beforeAssemblyReload += CancelAll;
            EditorApplication.quitting += CancelAll;
        }

        private static void CancelAll() {
            var requests = new List<ShellRequest>(s_Requests);

            foreach (var request in requests) {
                request.TryCancel();
            }

            // 域卸载时必须给被取消的工作线程一定时间关闭管道、删除临时脚本；
            // 但无需等待主线程侧的日志分发完成。
            var workers = requests.ConvertAll(r => r.WorkerCompletion ?? Task.CompletedTask).ToArray();

            try {
                Task.WaitAll(workers, TimeSpan.FromSeconds(2));
            } catch (AggregateException e) {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal static void Forget(ShellRequest request) => s_Requests.Remove(request);

        internal static void DumpQueue() {
            // 协作式预算：单个回调本身不可被抢占，但繁忙的生产者不能永远
            // 把 Editor 困在本 update 里。设置约 4ms 的截止时间（250 次更新/秒）。
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 250;
            var processed = 0;

            // 每次最多处理 128 个事件，且只要处理过事件就受时间预算约束；
            // 优先分发取消相关事件（PriorityQueue），再分发普通输出。
            while (processed < 128 && (processed == 0 || Stopwatch.GetTimestamp() < deadline)
                                   && (PriorityQueue.TryDequeue(out var res) || Queue.TryDequeue(out res))) {
                processed++;

                if (res.type != LogEventType.EndStream) {
                    // 普通输出事件出队后归还一个槽位，允许后台继续读取。
                    s_OutputSlots.Release();
                }

                try {
                    res.req.OnReceiveData(res.type, res.arg);
                } catch (Exception e) {
                    UnityEngine.Debug.LogException(e);
                }
            }

            // 静默（Quiet）命令也需要轮询取消按钮；不要因为
            // stdout/stderr 暂时没有新行而失去 UI 响应。
            if (s_Requests.Count > 0) {
                foreach (var request in new List<ShellRequest>(s_Requests)) request.TickProgress();
            }
        }

        /// <summary>
        /// 检查某个命令是否存在：既支持直接文件路径，也会在合并后的默认 PATH
        /// （Windows 额外检查 PATHEXT）中搜索。
        /// </summary>
        public static bool ExistsCommand(string command) {
            if (string.IsNullOrWhiteSpace(command)) {
                return false;
            }

            var start = new ProcessStartInfo();
            ApplyEnviron(start, DefaultEnvironment);
            return FindExecutable(command, start) != null;
        }

        private static string FindExecutable(string command, ProcessStartInfo start) {
            var workingDirectory = Path.GetFullPath(string.IsNullOrEmpty(start.WorkingDirectory)
                ? "." : start.WorkingDirectory);
            // Windows 可执行文件通常带扩展名，因此在无扩展名时依次尝试 PATHEXT 中的后缀。
            var extensions = new List<string> { "" };
#if UNITY_EDITOR_WIN
            extensions.AddRange((start.EnvironmentVariables["PATHEXT"] ?? ".COM;.EXE;.BAT;.CMD").Split(';'));
#endif
            // 局部函数：在指定路径下按扩展名列表依次查找实际存在的可执行文件。
            string Find(string path) {
                var absolute = Path.GetFullPath(Path.Combine(workingDirectory, path));

                foreach (var extension in extensions) {
                    var candidate = absolute + extension;

                    if (!File.Exists(candidate)) {
                        continue;
                    }
#if !UNITY_EDITOR_WIN
                    // X_OK 可执行位检查，涵盖目录与 ACL 等访问控制情况。
                    if (Access(candidate, 1) != 0) {
                        continue;
                    }
#endif
                    return candidate;
                }

                return null;
            }

            // 命令本身就是绝对路径或包含分隔符时，直接按当前工作目录解析。
            if (Path.IsPathRooted(command) || command.IndexOf(Path.DirectorySeparatorChar) >= 0
                                           || command.IndexOf(Path.AltDirectorySeparatorChar) >= 0) {
                return Find(command);
            }

            // 否则沿 PATH 的各目录依次查找。
            foreach (var entry in (start.EnvironmentVariables["PATH"] ?? "").Split(ConsoleUtils.PathSplitter)) {
                var directory = entry.Trim('"');

                // 空的 PATH 项表示子进程的工作目录。
                if (directory.Length == 0) {
                    directory = ".";
                }

                var found = Find(Path.Combine(directory, command));

                if (found != null) {
                    return found;
                }
            }

            return null;
        }

#if !UNITY_EDITOR_WIN
        // 通过 libc 的 access 系统调用检查文件的可执行权限（X_OK）。
        [DllImport("libc", EntryPoint = "access", SetLastError = true)]
        private static extern int Access(string path, int mode);
#endif

        /// <summary>
        /// 把给定的环境变量字典写入 <see cref="ProcessStartInfo"/>；PATH 特殊处理为
        /// 在原有 PATH 前追加（分隔符按当前系统自动选择）。
        /// </summary>
        private static void ApplyEnviron(ProcessStartInfo start, Dictionary<string, string> environ) {
            if (environ == null) {
                return;
            }

            foreach (var (name, value) in environ) {
                if (string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase)) {
                    var inherited = start.EnvironmentVariables["PATH"];
                    start.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(value)
                        ? inherited
                        : string.IsNullOrEmpty(inherited)
                            ? value
                            : value + ConsoleUtils.PathSplitter + inherited;
                } else {
                    start.EnvironmentVariables[name] = value;
                }
            }
        }

        // Process 在 Start 时会创建一个自动刷新的 StreamWriter。此包装编码用于抑制它
        // 提前写入的 BOM：真正的有序输入写入器会在第一次实际写入时发送一次请求的
        // 前导字节，且不会阻塞 Editor 线程去做管道 I/O。
        private sealed class InputPipeEncoding : Encoding {
            private readonly Encoding m_Encoding;

            internal InputPipeEncoding(Encoding encoding) : base(encoding.CodePage) => m_Encoding = encoding;

            // 关键：返回空前导字节，避免 ProcessStartInfo 在创建 StreamWriter 时自动写入 BOM。
            public override byte[] GetPreamble() => Array.Empty<byte>();
            public override Encoder GetEncoder() => m_Encoding.GetEncoder();
            public override Decoder GetDecoder() => m_Encoding.GetDecoder();

            public override int GetByteCount(char[] chars, int index, int count) =>
                m_Encoding.GetByteCount(chars, index, count);

            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
                m_Encoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

            public override int GetCharCount(byte[] bytes, int index, int count) =>
                m_Encoding.GetCharCount(bytes, index, count);

            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
                m_Encoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

            public override int GetMaxByteCount(int count) => m_Encoding.GetMaxByteCount(count);
            public override int GetMaxCharCount(int count) => m_Encoding.GetMaxCharCount(count);
        }

        /// <summary>
        /// 根据可执行文件、配置与参数创建并启动一个进程（重定向标准输入/输出/错误，
        /// 交由后台线程读取）。启动失败时负责释放进程资源后重新抛出异常。
        /// </summary>
        private static Process CreateProcess(string executable, ShellSettings settings, params object[] args) {
            if (string.IsNullOrWhiteSpace(executable)) {
                throw new ArgumentException("An executable is required.", nameof(executable));
            }

            if (args == null) {
                throw new ArgumentNullException(nameof(args));
            }

            // 未指定工作目录时回退到 "."；指定的目录必须真实存在。
            if (!string.IsNullOrEmpty(settings.WorkDirectory) && !Directory.Exists(settings.WorkDirectory)) {
                throw new DirectoryNotFoundException($"Working directory does not exist: {settings.WorkDirectory}");
            }

            var start = new ProcessStartInfo(executable) {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = settings.WorkDirectory ?? ".",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                
                // stdin 默认不带 BOM，且通过包装编码抑制进程启动时的自动 BOM。
                StandardInputEncoding = new InputPipeEncoding(settings.InputEncoding ?? new UTF8Encoding(false)),
                
                // stdout/stderr 默认使用 UTF-8。
                StandardOutputEncoding = settings.OutputEncoding ?? Encoding.UTF8,
                StandardErrorEncoding = settings.OutputEncoding ?? Encoding.UTF8,
            };

            foreach (var arg in args) {
                if (arg == null) {
                    throw new ArgumentException("Arguments cannot contain null.", nameof(args));
                }

                start.ArgumentList.Add(arg.ToString());
            }

            ApplyEnviron(start, DefaultEnvironment);
            ApplyEnviron(start, settings.Environment);

            // Process.Start 可能在父进程的 PATH 而非子进程环境中搜索；
            // 这里先自行解析出真实路径，避免找不到可执行文件。
            start.FileName = FindExecutable(executable, start)
                             ?? throw new FileNotFoundException(
                                 "Executable not found in the requested working directory or PATH.", executable);
            var process = new Process { StartInfo = start };

            try {
                process.Start();
                return process;
            } catch {
                process.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 以脚本方式执行命令：Windows 使用 cmd.exe，macOS/Linux 使用 /bin/bash。
        /// 脚本写入临时文件，完成、失败或取消后自动删除。
        /// </summary>
        public static ShellRequest RunCommand(string cmd, ShellSettings? settings = null) {
            if (cmd == null) {
                throw new ArgumentNullException(nameof(cmd));
            }

            var actual = settings ?? ShellSettings.Default;
            var script = Path.Combine(Path.GetTempPath(), "unikit-shell-" + Guid.NewGuid().ToString("N") +
#if UNITY_EDITOR_WIN
                ".bat");
#else
                                                          ".sh");
#endif
            try {
#if UNITY_EDITOR_WIN
                var codePage = (actual.OutputEncoding ?? Encoding.UTF8).CodePage;

                // Windows：设置脚本编码，并通过 chcp 把控制台代码页与之一致。
                File.WriteAllText(script, "@echo off\r\n@chcp " + codePage + ">nul\r\n" + cmd,
                    codePage == 65001 ? new UTF8Encoding(false) : actual.OutputEncoding);
                var process = CreateProcess(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    actual, "/d", "/c", script);
#else
                // 其他平台：以 UTF-8（不带 BOM）写入脚本，交给 /bin/bash 执行。
                File.WriteAllText(script, cmd, new UTF8Encoding(false));
                var process = CreateProcess("/bin/bash", actual, script);
#endif
                return QueueUpProcess(process, cmd, actual, script);
            } catch {
                // 启动失败时清理临时脚本后重新抛出。
                File.Delete(script);
                throw;
            }
        }

        /// <summary>
        /// 将可执行文件与参数分开执行，参数中的空格由 ProcessStartInfo 自动处理，
        /// 无需手动加引号；不经过 shell 展开管道或通配符。
        /// </summary>
        public static ShellRequest RunCommandLine(string executable, ShellSettings settings, params object[] args) {
            if (args == null) {
                throw new ArgumentNullException(nameof(args));
            }

            // 在启动进程前一次性把用户对象转换为字符串。这样即便之后格式化成
            // 显示标签也不会在子进程已启动后抛出异常。
            var arguments = Array.ConvertAll(args, arg => arg?.ToString()
                                                          ?? throw new ArgumentException(
                                                              "Arguments cannot contain null.", nameof(args)));
            var command = executable + ' ' + string.Join(" ", arguments);
            var process = CreateProcess(executable, settings, arguments);
            return QueueUpProcess(process, command, settings);
        }

        /// <summary>使用默认配置执行一条命令行。</summary>
        public static ShellRequest RunCommandLine(string executable, params object[] args) =>
            RunCommandLine(executable, ShellSettings.Default, args);

        /// <summary>
        /// 在后台持续读取某个输出流，按换行拆分为事件投递到主线程；
        /// 跨读取块保留解码状态，并在流结束时退出。
        /// </summary>
        private static async Task ReadOutput(Stream stream, Encoding encoding, ShellRequest request,
            LogEventType type) {
            var bytes = new byte[4096];
            var chars = new char[encoding.GetMaxCharCount(bytes.Length)];
            var decoder = encoding.GetDecoder();

            while (true) {
                var count = await stream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                // flush 参数仅在流结束（count==0）时为 true，用来冲洗解码器缓冲。
                var charCount = decoder.GetChars(bytes, 0, count, chars, 0, count == 0);

                if (charCount > 0) {
                    // 每个事件最多包含一个完整行，使主线程的时间预算
                    // 同样适用于包含成千上万短行的数据块。
                    var start = 0;

                    for (var i = 0; i < charCount; i++) {
                        if (chars[i] != '\n') {
                            continue;
                        }

                        await EnqueueOutputAsync(request, type, new string(chars, start, i - start + 1))
                            .ConfigureAwait(false);
                        start = i + 1;
                    }

                    // 剩余的未换行片段也入队，保留在请求的缓冲中等待补全。
                    if (start < charCount) {
                        await EnqueueOutputAsync(request, type, new string(chars, start, charCount - start))
                            .ConfigureAwait(false);
                    }
                }

                if (count == 0) {
                    break;
                }
            }
        }

        /// <summary>
        /// 创建请求并启动后台读取任务。返回的 <see cref="ShellRequest"/> 负责进程的
        /// 清理与完成事件投递。
        /// </summary>
        private static ShellRequest QueueUpProcess(Process process, string cmd, ShellSettings settings,
            string script = null) {
            ShellRequest request;

            try {
                request = new ShellRequest(cmd, settings, process);
                s_Requests.Add(request);
            } catch {
                // 请求创建失败时结束并释放进程。
                try {
                    if (!process.HasExited) {
                        process.Kill();
                    }
                } finally {
                    process.Dispose();
                }

                throw;
            }

            request.WorkerCompletion = Task.Run(async () => {
                var completion = new ShellCompletion { ExitCode = -1 };

                try {
                    var encoding = settings.OutputEncoding ?? Encoding.UTF8;
                    var stdout = ReadOutput(process.StandardOutput.BaseStream, encoding, request, LogEventType.InfoLog);
                    var stderr = ReadOutput(process.StandardError.BaseStream, encoding, request, LogEventType.ErrorLog);
                    var readers = Task.WhenAll(stdout, stderr);

                    // 仅观察异常，防止未观察的 Task 异常在终结器阶段崩溃。
                    _ = readers.ContinueWith(t => {
                        var ignored = t.Exception;
                    }, TaskContinuationOptions.OnlyOnFaulted);

                    // 某个读取器失败时不能让另一个读取器一直等待被阻塞的子进程，
                    // 因此等待任一先完成并传播其异常。
                    var first = await Task.WhenAny(stdout, stderr, request.Cancellation).ConfigureAwait(false);

                    if (first != request.Cancellation) {
                        await first.ConfigureAwait(false);
                    }

                    var finished = await Task.WhenAny(readers, request.Cancellation).ConfigureAwait(false);

                    if (finished == readers) {
                        await readers.ConfigureAwait(false);
                    }

                    process.WaitForExit();
                    completion.ExitCode = process.ExitCode;
                } catch (Exception e) {
                    // 非取消导致的异常记录为错误，并请求取消以触发清理。
                    if (!request.IsCancellationRequested) {
                        completion.Error = e;
                        request.TryCancel();
                    }
                } finally {
                    completion.Canceled = request.IsCancellationRequested && completion.Error == null;

                    try {
                        request.ReleaseProcess();
                    } catch (Exception e) {
                        completion.Error ??= e;
                    }

                    try {
                        if (script != null) File.Delete(script);
                    } catch (Exception e) {
                        completion.Error ??= e;
                    }

                    request.QueueCompletion(completion);
                }
            });
            return request;
        }
    }

    /// <summary>后台工作线程交付给主线程的完成信息。</summary>
    internal sealed class ShellCompletion {
        internal int ExitCode;
        internal Exception Error;
        internal bool Canceled;
    }
}