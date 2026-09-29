using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace ZStudio.UniKit.Editor {
    /// <summary>
    /// 一次 Shell 执行的请求句柄。公开 API 与事件回调均在 Editor 主线程使用，可等待、可作协程。
    /// </summary>
    public class ShellRequest : INotifyCompletion {
        /// <summary>按行输出（含最后一个未换行片段）回调，参数为事件类型与文本。</summary>
        public event Action<LogEventType, string> onLog;

        /// <summary>完成回调，参数为退出码；触发时 <see cref="IsCompleted"/> 已为 true。</summary>
        public event Action<int> OnComplete;

        private readonly string m_Command;
        private readonly ShellSettings m_Settings;

        /// <summary>保护进程句柄与输入状态的锁。</summary>
        private readonly object m_ProcessLock = new();

        private Process m_Process;

        /// <summary>当前排队的最后一个输入写任务，用于串行化写入顺序。</summary>
        private Task m_InputTail = Task.CompletedTask;

        private bool m_InputStarted;
        private Task m_InputCompletion;
        private ShellCompletion m_BackgroundCompletion;
        private readonly CancellationTokenSource m_OutputCancellation = new();
        private readonly ShellResult m_Result;

        /// <summary>用于拼接尚未换行的标准输出。</summary>
        private readonly StringBuilder m_LineBuilder = new();

        /// <summary>用于拼接尚未换行的标准错误。</summary>
        private readonly StringBuilder m_ErrorLineBuilder = new();

        private readonly TaskCompletionSource<bool> m_Cancellation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ExceptionDispatchInfo m_Error;
        private string m_OutputColor;
        private string m_ErrorColor;

        /// <summary>模拟进度值，随日志增长而趋近 1。</summary>
        private float m_PseudoProgress;

        private readonly int m_ProgressId;
        private volatile bool m_CancelRequested;

        /// <summary>后台工作任务的完成信号。</summary>
        internal Task WorkerCompletion { get; set; }

        internal Task Cancellation => m_Cancellation.Task;
        internal CancellationToken CancellationToken => m_OutputCancellation.Token;
        internal bool IsCancellationRequested => m_CancelRequested;

        /// <summary>最近一行（或最后预览片段）的标准输出。</summary>
        internal string PendingOutput { get; private set; }

        /// <summary>是否已经完成（正常、失败或取消）。</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// 获取执行结果；未完成时抛出异常。若有执行错误或非零退出码，
        /// 会在此处重新抛出。
        /// </summary>
        public ShellResult GetResult() {
            if (!IsCompleted) {
                throw new InvalidOperationException("The shell request has not completed.");
            }

            m_Error?.Throw();
            return m_Result;
        }

        internal ShellRequest(string command, ShellSettings settings, Process proc) {
            m_Process = proc;
            m_Command = command;
            m_Settings = settings;
            m_Result = new ShellResult(command);
            m_ProgressId = Progress.Start(command, command);

            // 后台任务窗口的“暂停”按钮打开独立的非模态输入窗口。
            Progress.RegisterPauseCallback(m_ProgressId, _ => {
                if (!IsCompleted) {
                    PromptWindow.Show(PendingOutput, Input);
                }

                return false;
            });

            if (settings.ProgressCancelable) {
                Progress.RegisterCancelCallback(m_ProgressId, TryCancel);
            }

            UpdateProgressBar(command, 0);
        }

        internal void TickProgress() {
            if (!IsCompleted && !m_CancelRequested) {
                UpdateProgressBar(PendingOutput ?? m_Command, m_PseudoProgress);
            }
        }

        private void UpdateProgressBar(string message, float progress) {
            if (!m_Settings.WithProgress) {
                return;
            }

            if (m_Settings.ProgressCancelable) {
                // 可取消时使用模态进度条，点取消即触发 TryCancel。
                if (EditorUtility.DisplayCancelableProgressBar(m_Command, message, progress)) {
                    TryCancel();
                }
            } else {
                EditorUtility.DisplayProgressBar(m_Command, message, progress);
            }
        }

        /// <summary>
        /// 向 stdin 排队写入一行，不阻塞 Editor。写入错误会记入日志（Quiet 时不打印）；
        /// 需要确认写入成功请改用 <see cref="InputAsync"/>。
        /// </summary>
        public void Input(string input) {
            ObserveInput(InputAsync(input));
        }

        private async void ObserveInput(Task write) {
            try {
                await write;
            } catch (OperationCanceledException) {
                // 取消已由请求的完成流程统一上报。
            } catch (Exception e) {
                ReportCallbackException(e);
            }
        }

        /// <summary>
        /// 按提交顺序写入并刷新一行，I/O 期间不持有进程锁；返回的 Task 可观察写入错误或取消。
        /// </summary>
        public Task InputAsync(string input) {
            if (input == null) {
                throw new ArgumentNullException(nameof(input));
            }

            lock (m_ProcessLock) {
                if (m_Process == null || m_Process.HasExited || m_CancelRequested || m_InputCompletion != null) {
                    throw new InvalidOperationException("The shell process is no longer accepting input.");
                }

                var writer = m_Process.StandardInput;
                var stream = writer.BaseStream;
                var encoding = m_Settings.InputEncoding ?? new UTF8Encoding(false);
                var newline = writer.NewLine;
                var previous = m_InputTail;

                // Mono 的管道 I/O 可能在首次 await 之前执行同步工作。
                // 即使第一次写入也放到线程池，并将所有写入串行化。
                m_InputTail = Task.Run(async () => {
                    try {
                        await previous.ConfigureAwait(false);
                    } catch {
                        /* 即使前一次写入失败，也保持后续提交的顺序。 */
                    }

                    try {
                        if (m_CancelRequested) {
                            throw new OperationCanceledException();
                        }

                        var bytes = encoding.GetBytes(input + newline);

                        // 仅在第一次实际写入时发送一次编码前导字节（如 BOM）。
                        if (!m_InputStarted) {
                            var preamble = encoding.GetPreamble();

                            if (preamble.Length > 0) {
                                await stream.WriteAsync(preamble, 0, preamble.Length).ConfigureAwait(false);
                            }

                            m_InputStarted = true;
                        }

                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);

                        if (m_CancelRequested) {
                            throw new OperationCanceledException();
                        }
                    } catch {
                        lock (m_ProcessLock) {
                            if (m_CancelRequested) {
                                throw new OperationCanceledException("Shell input was canceled.");
                            }
                        }

                        throw;
                    }
                });
                return m_InputTail;
            }
        }

        /// <summary>
        /// 等待已提交的写入完成后关闭 stdin 发送 EOF；重复调用返回同一个 Task。
        /// 首次调用后不可再提交新输入。
        /// </summary>
        public Task CompleteInputAsync() {
            lock (m_ProcessLock) {
                if (m_InputCompletion != null) {
                    return m_InputCompletion;
                }

                if (m_Process == null || m_Process.HasExited || m_CancelRequested) {
                    throw new InvalidOperationException("The shell process is no longer accepting input.");
                }

                var stream = m_Process.StandardInput.BaseStream;
                var previous = m_InputTail;

                m_InputCompletion = Task.Run(async () => {
                    try {
                        try {
                            await previous.ConfigureAwait(false);
                        }

                        // 写入直接落到该流且已完成刷新；StreamWriter 本身
                        // 留给进程持有者去释放。
                        finally {
                            stream.Dispose();
                        }

                        if (m_CancelRequested) {
                            throw new OperationCanceledException();
                        }
                    } catch {
                        lock (m_ProcessLock) {
                            if (m_CancelRequested) {
                                throw new OperationCanceledException("Shell input was canceled.");
                            }
                        }

                        throw;
                    }
                });
                return m_InputCompletion;
            }
        }

        internal void QueueCompletion(ShellCompletion completion) {
            lock (m_ProcessLock) {
                completion.Canceled = m_CancelRequested && completion.Error == null;
                m_BackgroundCompletion = completion;

                // 取消时的完成事件走优先级队列，优先于积压的普通日志分发。
                if (m_CancelRequested) {
                    Shell.PriorityQueue.Enqueue((this, LogEventType.EndStream, completion));
                } else {
                    Shell.Queue.Enqueue((this, LogEventType.EndStream, completion));
                }
            }
        }

        /// <summary>
        /// 请求终止当前进程。返回 true 表示已发起取消；进程释放与完成通知由工作线程负责。
        /// </summary>
        public bool TryCancel() {
            lock (m_ProcessLock) {
                if (IsCompleted) {
                    return false;
                }

                if (m_CancelRequested) {
                    return true;
                }

                try {
                    // 父进程可能已退出，但被继承的管道仍处于打开状态。
                    if (m_Process != null && !m_Process.HasExited) {
                        m_Process.Kill();
                    }

                    m_CancelRequested = true;
                    m_OutputCancellation.Cancel();
                    m_Cancellation.TrySetResult(true);

                    // 进程释放可能早于主线程的分发；即使完成事件已排队，
                    // 取消也必须生效。
                    if (m_BackgroundCompletion != null) {
                        m_BackgroundCompletion.Canceled = m_BackgroundCompletion.Error == null;
                        Shell.PriorityQueue.Enqueue((this, LogEventType.EndStream, m_BackgroundCompletion));
                    }

                    return true;
                } catch (InvalidOperationException) {
                    return false;
                } catch (System.ComponentModel.Win32Exception) {
                    return false;
                }
            }
        }

        internal void ReleaseProcess() {
            Process process;

            lock (m_ProcessLock) {
                process = m_Process;
                m_Process = null;
            }

            if (process == null) {
                return;
            }

            // 同步流属性由调用方持有；显式释放其管道，
            // 包括因取消而中断的读取器。
            var input = process.StandardInput;
            var inputStream = input.BaseStream;

            try {
                // 原始写入拥有该管道。先关闭它再释放 StreamWriter，
                // 否则后者会尝试写入另一个编码前导字节。
                try {
                    inputStream?.Dispose();
                } finally {
                    try {
                        input.Dispose();
                    } catch (Exception e) when (e is IOException or ObjectDisposedException) {
                    }
                }
            } finally {
                try {
                    process.StandardOutput.Dispose();
                } finally {
                    try {
                        process.StandardError.Dispose();
                    } finally {
                        process.Dispose();
                    }
                }
            }
        }

        public void OnCompleted(Action continuation) {
            if (continuation == null) {
                throw new ArgumentNullException(nameof(continuation));
            }

            if (IsCompleted) {
                continuation();
            } else {
                OnComplete += _ => continuation();
            }
        }

        /// <summary>以协程方式等待完成；完成后会调用 <see cref="GetResult"/> 以传播异常。</summary>
        public IEnumerator ToCoroutine() {
            while (!IsCompleted) {
                Shell.DumpQueue();

                if (!IsCompleted) {
                    yield return null;
                }
            }

            GetResult();
        }

        /// <summary>
        /// 阻塞当前线程，同时泵送日志与完成事件，直到请求完成。
        /// 通常在主线程使用 await 更合适。
        /// </summary>
        public ShellResult Wait() {
            if (Shell.LogCallbackDepth != 0) {
                throw new InvalidOperationException("Do not call Wait from an onLog callback; use await instead.");
            }

            while (!IsCompleted) {
                Shell.DumpQueue();

                if (!IsCompleted) {
                    Thread.Sleep(10);
                }
            }

            return GetResult();
        }

        private void ReportCallbackException(Exception error) {
            if (!m_Settings.Quiet) {
                UnityEngine.Debug.LogException(error);
            }
        }

        private void OnLog(LogEventType type, string log) {
            Shell.LogCallbackDepth++;

            try {
                // 逐个调用订阅者，单个回调异常不会中断其他回调。
                if (onLog != null) {
                    foreach (Action<LogEventType, string> callback in onLog.GetInvocationList()) {
                        try {
                            callback(type, log);
                        } catch (Exception e) {
                            ReportCallbackException(e);
                        }
                    }
                }
            } finally {
                Shell.LogCallbackDepth--;
            }

            var colored = type == LogEventType.ErrorLog
                ? ConsoleUtils.ConvertToUnityColor(log, ref m_ErrorColor)
                : ConsoleUtils.ConvertToUnityColor(log, ref m_OutputColor);

            if (m_Settings.Quiet) {
                return;
            }

            var message = "<color=#808080>[ Shell Output ]</color>" + colored;

            // stderr 也常被用于成功的命令输出进度或诊断信息，因此以 Warning 显示。
            if (type is LogEventType.ErrorLog or LogEventType.WarnLog) {
                UnityEngine.Debug.LogWarning(message);
            } else {
                UnityEngine.Debug.Log(message);
            }
        }

        internal void OnReceiveData(LogEventType type, object raw) {
            if (IsCompleted || (IsCancellationRequested && type != LogEventType.EndStream)) {
                return;
            }

            if (type == LogEventType.EndStream) {
                // 正常完成时把尚未换行的残余行冲刷为完整日志。
                if (!IsCancellationRequested) {
                    FlushLine(LogEventType.InfoLog, m_LineBuilder);
                    FlushLine(LogEventType.ErrorLog, m_ErrorLineBuilder);
                } else {
                    // 取消时丢弃未完成的残余内容。
                    m_LineBuilder.Clear();
                    m_ErrorLineBuilder.Clear();
                }

                NotifyComplete((ShellCompletion)raw);
                return;
            }

            var output = (string)raw;
            m_Result.AppendLine(type, output);

            var builder = type == LogEventType.ErrorLog ? m_ErrorLineBuilder : m_LineBuilder;
            var start = 0;

            for (var i = 0; i < output.Length; i++) {
                if (output[i] != '\n') {
                    continue;
                }

                builder.Append(output, start, i - start);
                var line = builder.ToString().TrimEnd('\r');
                builder.Clear();
                OnLog(type, line);

                if (IsCompleted || IsCancellationRequested) {
                    return;
                }

                PendingOutput = line;
                start = i + 1;
            }

            builder.Append(output, start, output.Length - start);

            if (builder.Length > 0) {
                // 预览不应反复复制不断增长且未换行的行，只截取其末尾部分。
                var previewLength = Math.Min(builder.Length, 1024);
                PendingOutput = builder.ToString(builder.Length - previewLength, previewLength);
            }

            // 伪进度向 1 渐进逼近，用于驱动进度条显示。
            m_PseudoProgress += (1 - m_PseudoProgress) * 0.1f;
            Progress.Report(m_ProgressId, m_PseudoProgress, ConsoleUtils.ConvertToNoColor(PendingOutput ?? ""));
        }

        private void FlushLine(LogEventType type, StringBuilder builder) {
            if (builder.Length == 0) {
                return;
            }

            var line = builder.ToString();
            builder.Clear();
            OnLog(type, line);
        }

        private void NotifyComplete(ShellCompletion completion) {
            if (IsCompleted) {
                return;
            }

            m_Result.NotifyComplete(completion.ExitCode);
            var error = completion.Error;

            if (error == null && completion.Canceled) {
                error = new OperationCanceledException("Shell execution was canceled.");
            }

            if (error == null && completion.ExitCode != 0 && m_Settings.ThrowOnNonZeroExitCode) {
                error = new InvalidOperationException(
                    $"Shell exited with code {completion.ExitCode}: {m_Command}\n{m_Result.Error}");
            }

            if (error != null) {
                // 捕获异常现场，供 GetResult 在主线程重新抛出。
                m_Error = ExceptionDispatchInfo.Capture(error);
            }

            IsCompleted = true;
            Shell.Forget(this);

            try {
                Progress.Remove(m_ProgressId);

                if (m_Settings.AutoClearProgress && m_Settings.WithProgress) {
                    EditorUtility.ClearProgressBar();
                }

                if (!m_Settings.Quiet && !completion.Canceled
                                      && (completion.Error != null || completion.ExitCode != 0)) {
                    UnityEngine.Debug.LogError(
                        $"[ Shell Output ] {m_Command} exited with code {completion.ExitCode}: {completion.Error?.Message ?? m_Result.Error}");
                }
            } finally {
                var callbacks = OnComplete;
                OnComplete = null;

                if (callbacks != null) {
                    foreach (Action<int> callback in callbacks.GetInvocationList()) {
                        try {
                            callback(completion.ExitCode);
                        } catch (Exception e) {
                            ReportCallbackException(e);
                        }
                    }
                }
            }
        }

        public ShellRequest GetAwaiter() => this;
    }
}