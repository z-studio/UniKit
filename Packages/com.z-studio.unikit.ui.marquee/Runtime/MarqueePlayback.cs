using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ZStudio.UniKit.UI {
    /// <summary>播放会话的唯一所有者。先脱离旧会话，再执行可能重入的回调或 await continuation。</summary>
    internal sealed class MarqueePlayback {
        private sealed class Session {
            public Coroutine Routine;
            public MonoBehaviour Owner;
            public AwaitableCompletionSource Completion;
            public CancellationTokenRegistration Registration;
            public volatile bool CancellationRequested;
        }

        private Session m_Session;
        public int Version { get; private set; }
        public bool IsRunning => m_Session != null;
        public bool IsPaused { get; set; }
        public bool CancellationRequested => m_Session != null && m_Session.CancellationRequested;

        public int Begin() {
            int expected = Version + 1;
            Cancel(null);

            // 取消旧等待可能同步启动另一次播放，该请求优先。
            if (Version == expected) {
                m_Session = new Session();
            }

            return expected;
        }

        public void Register(AwaitableCompletionSource completion, CancellationToken token) {
            Session session = m_Session;
            session.Completion = completion;

            if (token.CanBeCanceled) {
                session.Registration = token.Register(
                    static value => ((Session)value).CancellationRequested = true, session);
            }
        }

        public void Start(MonoBehaviour owner, IEnumerator routine, int version, Action onFailure) {
            Session session = m_Session;

            if (version != Version || session == null) {
                return;
            }

            session.Owner = owner;

            if (session.CancellationRequested) {
                Cancel(owner);
                return;
            }

            Coroutine coroutine = owner.StartCoroutine(Drive(routine, version, owner, onFailure));

            // StartCoroutine 会同步执行到第一个 yield，期间可能已经发生重入。
            if (m_Session == session && version == Version) {
                session.Routine = coroutine;
            } else if (coroutine != null) {
                owner.StopCoroutine(coroutine);
            }
        }

        public void Cancel(MonoBehaviour owner) {
            ++Version;
            Session session = Detach();

            if (session == null) {
                return;
            }

            MonoBehaviour runner = session.Owner != null ? session.Owner : owner;

            if (session.Routine != null && runner != null) {
                runner.StopCoroutine(session.Routine);
            }

            session.Completion?.TrySetCanceled();
        }

        public void Finish(int version, Action onComplete) {
            if (version != Version || m_Session == null) {
                return;
            }

            Session session = Detach();

            if (session.CancellationRequested) {
                session.Completion?.TrySetCanceled();
                return;
            }

            try {
                onComplete?.Invoke();
            } finally {
                session.Completion?.TrySetResult();
            }
        }

        private Session Detach() {
            Session session = m_Session;
            m_Session = null;
            IsPaused = false;
            session?.Registration.Dispose();
            return session;
        }

        // 展开嵌套迭代器，让事件和渲染器异常都结束所属等待，并回滚显示资源。
        private IEnumerator Drive(IEnumerator routine, int version, MonoBehaviour owner, Action onFailure) {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);

            try {
                while (stack.Count > 0 && version == Version) {
                    object yielded = null;
                    bool hasNext = false;
                    Exception failure = null;

                    try {
                        IEnumerator iterator = stack.Peek();
                        hasNext = iterator.MoveNext();

                        if (hasNext && version == Version) {
                            yielded = iterator.Current;
                        }
                    } catch (Exception exception) {
                        failure = exception;
                    }

                    if (failure != null) {
                        if (version == Version && m_Session != null) {
                            Session session = Detach();

                            try {
                                onFailure?.Invoke();
                            } catch (Exception cleanupFailure) {
                                Debug.LogException(cleanupFailure, owner);
                            }

                            if (session.Completion != null) {
                                session.Completion.TrySetException(failure);
                            } else {
                                Debug.LogException(failure, owner);
                            }
                        } else {
                            Debug.LogException(failure, owner);
                        }

                        yield break;
                    }

                    if (version != Version) {
                        yield break;
                    }

                    if (!hasNext) {
                        (stack.Pop() as IDisposable)?.Dispose();
                    } else if (yielded is IEnumerator nested) {
                        stack.Push(nested);
                    } else {
                        yield return yielded;
                    }
                }
            } finally {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }
    }
}