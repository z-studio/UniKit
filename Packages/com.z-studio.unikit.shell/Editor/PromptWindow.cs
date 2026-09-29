using System;
using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.Editor {
    /// <summary>独立的非模态输入窗口，供后台任务暂停时向 stdin 提交一行文本。</summary>
    internal class PromptWindow : EditorWindow {
        public Action<string> OnSubmit;
        public string Prompt;
        private string m_Input = "";
        private bool m_FocusInput = true;
        internal string SubmissionError { get; private set; }

        public static void Show(string prompt, Action<string> onSubmit) {
            if (onSubmit == null) {
                throw new ArgumentNullException(nameof(onSubmit));
            }

            // 非模态窗口彼此独立，各自创建实例，避免覆盖其他请求的回调。
            var window = CreateInstance<PromptWindow>();
            window.OnSubmit = onSubmit;
            window.Prompt = ConsoleUtils.ConvertToNoColor(prompt ?? "");
            window.titleContent = new GUIContent("Shell input");
            window.minSize = new Vector2(320, 110);
            window.ShowUtility();
        }

        /// <summary>回车或小键盘 Enter 视为提交；输入法组合中不算提交。</summary>
        internal static bool IsSubmitKey(Event e, string composition) =>
            e.type == EventType.KeyDown && string.IsNullOrEmpty(composition)
                                        && e.keyCode is KeyCode.Return or KeyCode.KeypadEnter;

        private void OnGUI() {
            var e = Event.current;

            // Esc 关闭窗口（输入法组合中不响应）。
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape
                                            && string.IsNullOrEmpty(Input.compositionString)) {
                e.Use();
                Close();
                return;
            }

            // 在 TextField 消费事件前截获回车；输入法确认候选不视为提交。
            var submit = IsSubmitKey(e, Input.compositionString);
            EditorGUILayout.LabelField(Prompt ?? "", EditorStyles.wordWrappedLabel);

            using (new EditorGUILayout.HorizontalScope()) {
                GUI.SetNextControlName(nameof(m_Input));
                m_Input = EditorGUILayout.TextField(m_Input);

                if (m_FocusInput && e.type == EventType.Repaint) {
                    EditorGUI.FocusTextInControl(nameof(m_Input));
                    m_FocusInput = false;
                }

                submit |= GUILayout.Button("Submit", GUILayout.Width(65));
            }

            if (!string.IsNullOrEmpty(SubmissionError)) {
                EditorGUILayout.HelpBox(SubmissionError, MessageType.Error);
            }

            // 在所有布局作用域关闭之后，才调用用户代码或关闭窗口。
            if (submit) {
                e.Use();

                if (TrySubmit(m_Input)) {
                    Close();
                }
            }
        }

        internal bool TrySubmit(string input) {
            try {
                if (OnSubmit == null) {
                    throw new InvalidOperationException("This input window is no longer active.");
                }

                OnSubmit(input ?? "");
                OnSubmit = null;
                SubmissionError = null;
                return true;
            } catch (Exception e) {
                // 提交失败时在窗口内显示错误，不关闭窗口。
                SubmissionError = e.Message;
                return false;
            }
        }

        private void OnDisable() => OnSubmit = null;
    }
}