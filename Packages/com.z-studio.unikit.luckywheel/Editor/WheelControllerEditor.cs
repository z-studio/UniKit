using System;
using UnityEditor;
using UnityEngine;

namespace ZStudio.UniKit.LuckyWheel.Editor {
    [CustomEditor(typeof(WheelController))]
    public class WheelControllerEditor : UnityEditor.Editor {
        // 分组只属于编辑器展示，展开状态保存在本机，不写入场景或 Prefab。
        private const string k_PreferencePrefix = "ZStudio.UniKit.LuckyWheel.Inspector.";

        private static readonly InspectorGroup[] s_Groups = {
            new("Setup", "基本设置", true, "m_Reel", "m_PointerController", "m_SectorsCount", "m_UseUnscaledTime"),
            new("Rewards", "奖格与奖励", true, "m_Rewards"), 
            new("Sectors", "每格大小（默认等分）", false, "m_CustomSectors"),
            new("Calibration", "对齐奖格与指针（首次设置）", true), 
            new("SpinIn", "起转", false, "m_SpinStartDelay",
                "m_AccelerationTime",
                "m_CustomAccelerationRhythm", "m_AccelerationRhythm", "m_InRotationAngle", "m_InRotationTime",
                "m_CustomInRhythm", "m_InRotationCurve"),
            new("Motion", "转动", false, "m_SpinDirection", "m_WaitingSpeed", "m_MainRotationTime", "m_MinimumTurns"),
            new("SpinOut", "停转", false, "m_DecelerationTime", "m_CustomDecelerationRhythm",
                "m_DecelerationRhythm", "m_LandingMode", "m_OutRotationAngle", "m_OutRotationTime",
                "m_CustomOutRhythm", "m_OutRotationCurve"),
            new("Audio", "音效（可选）", false, "m_SpinSound", "m_HitTargetSound"),
            new("Events", "事件（可选）", false,
                "m_StartedEvent", "m_ResultEvent", "m_RewardEvent", "m_CompleteEvent",
                "m_CancelledEvent")
        };

        private sealed class InspectorGroup {
            public readonly string key;
            public readonly string title;
            public readonly bool defaultExpanded;
            public readonly string[] propertyNames;

            public InspectorGroup(string key, string title, bool defaultExpanded, params string[] propertyNames) {
                this.key = key;
                this.title = title;
                this.defaultExpanded = defaultExpanded;
                this.propertyNames = propertyNames;
            }
        }

        [SerializeField]
        private int m_CalibrationIndex;

        [SerializeField]
        private Transform m_CalibrationCenter;

        [SerializeField]
        private Transform m_CalibrationPointer;

        private int m_TestIndex;
        private float m_TestOffset;
        private bool m_OverrideTestOffset;
        private bool m_RandomTarget;

        private void OnEnable() {
            var wheel = (WheelController)target;
            
            if (!wheel) {
                return;
            }

            m_CalibrationIndex = Mathf.Clamp(m_CalibrationIndex, 0, wheel.SectorsCount - 1);
            
            if (!m_CalibrationCenter) {
                m_CalibrationCenter = wheel.GetReward(m_CalibrationIndex)?.View?.transform;
            }

            if (!m_CalibrationPointer) {
                var pointer =
                    serializedObject.FindProperty("m_PointerController").objectReferenceValue as PointerController;
                
                if (pointer) {
                    m_CalibrationPointer = pointer.transform;
                }
            }
        }

        public override void OnInspectorGUI() {
            var wheel = (WheelController)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("首次使用：绑定圆盘 → 按顺时针配置奖格 → 对齐指针 → 进入运行模式试转。中奖结果由你的代码指定。", MessageType.Info);

            foreach (var group in s_Groups) {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    if (!DrawFoldout(group.key, group.title, group.defaultExpanded)) {
                        continue;
                    }

                    using (new EditorGUI.IndentLevelScope()) {
                        // 只锁定配置项，不锁定折叠标题，运行时仍可查看各组。
                        using (new EditorGUI.DisabledScope(Application.isPlaying && wheel.IsSpinning)) {
                            foreach (var propertyName in group.propertyNames) {
                                if (IsRhythmVisible(propertyName)) {
                                    var property = serializedObject.FindProperty(propertyName);
                                    DrawField(property);
                                }
                            }
                        }

                        if (group.key == "Calibration") {
                            serializedObject.ApplyModifiedProperties();
                            DrawCalibration(wheel);
                        }

                        var hint = group.key switch {
                            "Setup" => "奖格从 0 开始编号。配置奖励后，奖格数量由奖励列表决定。首次接入时，展开“对齐奖格与指针”。",
                            "Rewards" =>
                                "从上到下对应顺时针排列的第 0、1、2……格，与转盘转动方向无关。表现对象可包含图片、Spine、特效或混合内容。组件不创建或排列美术。运行时可用 ConfigureRewards 配置。",
                            "SpinIn" => "起转用时越短越利落，越长越轻柔。回拉角度设为 0，可关闭起转前的反向小摆动。",
                            "Motion" => "这里设置正常转动的速度和最短时间，起转、停转另计。为停准奖格，实际可能稍微多转一点。",
                            "SpinOut" => "停转用时越短越干脆，越长越缓慢。回摆角度设为 0，可直接停准，不做小幅回摆。",
                            "Events" => "配置了奖励，推荐只使用“停准后（返回奖励）”；仅按编号使用时，选择“停准后（返回编号）”。多个停准事件都会触发，同一份业务处理只绑定一个入口。",
                            _ => null
                        };
                        
                        if (hint != null) {
                            EditorGUILayout.HelpBox(hint, MessageType.None);
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (!wheel.Reel) {
                EditorGUILayout.HelpBox("请在“基本设置”中绑定“旋转的圆盘”；指针和音效可不配置。", MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                if (DrawFoldout("SpinTest", "试转（运行模式）", false)) {
                    using (new EditorGUI.IndentLevelScope()) {
                        DrawDebug(wheel);
                    }
                }
            }

            if (Application.isPlaying && wheel.IsSpinning) {
                Repaint();
            }
        }

        private static void DrawField(SerializedProperty property) {
            var label = FieldLabel(property);

            if (property.name == "m_CustomSectors") {
                DrawSectorLayout(property);
                return;
            }

            if (property.name == "m_LandingMode") {
                DrawLanding(property);
                return;
            }

            if (property.name == "m_Rewards") {
                DrawRewards(property);
                return;
            }

            if (property.name == "m_SpinDirection") {
                property.enumValueIndex = EditorGUILayout.Popup(label, property.enumValueIndex,
                    new[] { "逆时针", "顺时针" });
                return;
            }

            if (property.name == "m_SectorsCount") {
                var count = property.serializedObject.FindProperty("m_Rewards").arraySize;

                if (count > 0) {
                    EditorGUILayout.LabelField(label, new GUIContent($"{count}（跟随奖励列表）"));
                    return;
                }
            }

            if (property.name != "m_WaitingSpeed") {
                EditorGUILayout.PropertyField(property, label, true);
                return;
            }

            // 显示为直观的圈/秒；只有用户编辑时才换算写入，运行时仍使用度/秒。
            var rect = EditorGUILayout.GetControlRect();

            using (new EditorGUI.PropertyScope(rect, label, property)) {
                EditorGUI.BeginChangeCheck();
                var turnsPerSecond = EditorGUI.FloatField(rect, label, property.floatValue / 360f);
                
                if (EditorGUI.EndChangeCheck()) {
                    property.floatValue = Mathf.Max(1, turnsPerSecond * 360f);
                }
            }
        }

        private static void DrawSectorLayout(SerializedProperty custom) {
            var data = custom.serializedObject;
            var rewards = data.FindProperty("m_Rewards");
            var count = Mathf.Max(1,
                rewards.arraySize > 0 ? rewards.arraySize : data.FindProperty("m_SectorsCount").intValue);
            var widths = data.FindProperty("m_SectorWidths");
            EditorGUI.BeginChangeCheck();
            var mode = EditorGUILayout.Popup("奖格大小", custom.boolValue ? 1 : 0, new[] { "全部等分", "每格单独设置" });

            if (EditorGUI.EndChangeCheck()) {
                custom.boolValue = mode == 1;
                
                if (custom.boolValue && widths.arraySize == 0) {
                    ResetSectorWidths(widths, count);
                }
            }

            if (!custom.boolValue) {
                EditorGUILayout.LabelField(new GUIContent("每格角度"), new GUIContent($"{360f / count:G}°"));
                return;
            }

            if (widths.arraySize != count) {
                EditorGUILayout.HelpBox("奖格数量已变化，请重建角度列表后重新填写。", MessageType.Warning);
                
                if (GUILayout.Button($"按当前 {count} 格重建角度列表")) {
                    ResetSectorWidths(widths, count);
                }

                return;
            }

            double total = 0;
            var valid = true;

            for (var index = 0; index < count; index++) {
                var width = widths.GetArrayElementAtIndex(index);
                var name = rewards.arraySize > 0
                    ? rewards.GetArrayElementAtIndex(index).FindPropertyRelative("m_Name").stringValue : null;
                EditorGUILayout.PropertyField(width,
                    new GUIContent(string.IsNullOrEmpty(name) ? $"第 {index} 格（度）" : $"{index} · {name}（度）",
                        "按顺时针奖格顺序填写角度宽度。每格必须大于 0，总和须为 360°；不是中奖概率。"));
                total += width.floatValue;
                valid &= !float.IsNaN(width.floatValue) && !float.IsInfinity(width.floatValue) && width.floatValue > 0;
            }

            EditorGUILayout.LabelField(new GUIContent("角度合计"), new GUIContent($"{total:G}° / 360°"));
            
            if (!valid || Math.Abs(total - 360) > 0.001) {
                EditorGUILayout.HelpBox("每格角度必须大于 0，总和须为 360°。填写完成前不能转动或校准。", MessageType.Error);
            }
        }

        private static void ResetSectorWidths(SerializedProperty widths, int count) {
            widths.arraySize = count;
            
            for (var index = 0; index < count; index++) {
                widths.GetArrayElementAtIndex(index).floatValue = 360f / count;
            }
        }

        private static void DrawLanding(SerializedProperty mode) {
            mode.enumValueIndex =
                EditorGUILayout.Popup("停在格子哪里", mode.enumValueIndex, new[] { "格子中心", "指定格内位置", "格内范围随机" });

            if (mode.enumValueIndex == (int)WheelLandingMode.Fixed) {
                var offset = mode.serializedObject.FindProperty("m_LandingOffset");
                EditorGUI.BeginChangeCheck();
                var percentage = EditorGUILayout.Slider(
                    new GUIContent("格内位置（%）", "沿顺时针量，50% 是中心，1% 和 99% 靠近两侧边界；按目标格实际宽度计算。"),
                    (offset.floatValue + 0.5f) * 100, 1, 99);
                
                if (EditorGUI.EndChangeCheck()) {
                    offset.floatValue = percentage / 100 - 0.5f;
                }
            } else if (mode.enumValueIndex == (int)WheelLandingMode.Random) {
                var range = mode.serializedObject.FindProperty("m_LandingRange");
                var value = (range.vector2Value + Vector2.one * 0.5f) * 100;
                EditorGUI.BeginChangeCheck();

                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.PrefixLabel(new GUIContent("格内范围（%）", "从起始边界到结束边界，50% 为中心。仅在指定目标格内随机，不随机选择奖格。"));
                    value.x = Mathf.Clamp(EditorGUILayout.FloatField(value.x), 1, 99);
                    GUILayout.Label("～", GUILayout.Width(16));
                    value.y = Mathf.Clamp(EditorGUILayout.FloatField(value.y), value.x, 99);
                }

                if (EditorGUI.EndChangeCheck()) {
                    range.vector2Value = value / 100 - Vector2.one * 0.5f;
                }
            }
        }

        private static void DrawRewards(SerializedProperty rewards) {
            if (rewards.arraySize == 0) {
                var count = rewards.serializedObject.FindProperty("m_SectorsCount").intValue;
                EditorGUILayout.HelpBox("暂未配置奖励，当前只按奖格编号转动。可一键创建，再填写每格内容。", MessageType.None);

                if (GUILayout.Button($"按当前 {count} 格创建奖励配置")) {
                    rewards.arraySize = count;

                    for (var index = 0; index < count; index++) {
                        InitializeReward(rewards, index);
                        rewards.GetArrayElementAtIndex(index).isExpanded = index == 0;
                    }
                }

                return;
            }

            for (var index = 0; index < rewards.arraySize; index++) {
                var reward = rewards.GetArrayElementAtIndex(index);
                var name = reward.FindPropertyRelative("m_Name").stringValue;
                var summary = string.IsNullOrWhiteSpace(name) ? "未命名奖励" : name;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        reward.isExpanded =
                            EditorGUILayout.Foldout(reward.isExpanded, $"第 {index} 格 · {summary}", true);

                        using (new EditorGUI.DisabledScope(index == 0)) {
                            if (GUILayout.Button("↑", GUILayout.Width(24))) {
                                rewards.MoveArrayElement(index, index - 1);
                                return;
                            }
                        }

                        using (new EditorGUI.DisabledScope(index == rewards.arraySize - 1)) {
                            if (GUILayout.Button("↓", GUILayout.Width(24))) {
                                rewards.MoveArrayElement(index, index + 1);
                                return;
                            }
                        }

                        if (GUILayout.Button("删除", GUILayout.Width(42))) {
                            rewards.DeleteArrayElementAtIndex(index);
                            return;
                        }
                    }

                    if (reward.isExpanded) {
                        EditorGUILayout.PropertyField(reward.FindPropertyRelative("m_Name"),
                            new GUIContent("备注名称（可选）", "仅用于识别配置，不限制实际表现内容。"));
                        EditorGUILayout.PropertyField(reward.FindPropertyRelative("m_View"),
                            new GUIContent("奖格物体（可选）",
                                "绑定场景中该格的根 GameObject，内部可包含图片、Spine、特效或混合内容。仅保存引用，不自动实例化、排布或播放动画。"));
                        EditorGUILayout.PropertyField(reward.FindPropertyRelative("m_Data"),
                            new GUIContent("奖励数据（可选）", "绑定自定义 ScriptableObject 等资产。奖励类型、数量等由业务自行定义，转盘不解释这些字段。"));
                    }
                }
            }

            if (GUILayout.Button("+ 添加奖格")) {
                var index = rewards.arraySize;
                rewards.arraySize++;
                InitializeReward(rewards, index);
            }

        }

        private static void InitializeReward(SerializedProperty rewards, int index) {
            var reward = rewards.GetArrayElementAtIndex(index);
            reward.FindPropertyRelative("m_Name").stringValue = $"奖励 {index + 1}";
            reward.FindPropertyRelative("m_View").objectReferenceValue = null;
            reward.FindPropertyRelative("m_Data").objectReferenceValue = null;
            reward.isExpanded = true;
        }

        private static GUIContent FieldLabel(SerializedProperty property) {
            var (label, tooltip) = property.name switch {
                "m_Reel" => ("旋转的圆盘", "绑定转动时需要旋转的圆盘物体。奖格应放在它下面，物体轴心须位于圆盘中心。"),
                "m_PointerController" => ("指针（可选）", "需要指针拨动和点击音效时绑定；不绑定也能转动。"),
                "m_SectorsCount" => ("奖格数量", "圆盘上的奖格总数，从 0 开始按顺时针编号。"),
                "m_ZeroAngle" => ("第 0 格对准指针时的旋转角度", "由“将此奖格对准指针并保存”按钮自动计算，通常无需手动填写。"),
                "m_AccelerationTime" => ("起转用时（秒）", "从静止提到正常转速需要多久。短：迅速有力；长：轻柔舒缓。0 表示立即达到正常转速。"),
                "m_InRotationAngle" => ("起转前回拉（度）", "正式转动前，先反向摆动一点。0 表示关闭。"),
                "m_InRotationTime" => ("回拉用时（秒）", "完成这次反向小摆动所用的时间，不包含正式起转用时。"),
                "m_SpinDirection" => ("转动方向", "圆盘向顺时针还是逆时针转动。"),
                "m_WaitingSpeed" => ("每秒转几圈", "正常转动时的速度。2 表示一秒转两圈；起转和停转会自动平滑衔接。"),
                "m_MainRotationTime" => ("至少保持转动（秒）", "达到正常转速后，至少保持这个时间；起转和停转另计。等待结果时已转过的时间会计入。为了停准奖格，可能稍微延长。"),
                "m_MinimumTurns" => ("至少转几圈", "整轮正式转动至少经过几圈，包含起转和停转；不包含反向回拉、回摆。"),
                "m_DecelerationTime" => ("停转用时（秒）", "从正常转速逐渐停下来需要多久。短：干脆；长：缓慢拖尾。不包含停下后的回摆。"),
                "m_OutRotationAngle" => ("停下后回摆（度）", "先略过目标，再反向回到本次停止位置。0 表示关闭。完成通知会等回摆结束后触发。"),
                "m_OutRotationTime" => ("回摆用时（秒）", "完成最后这次小幅回摆需要多久。"),
                "m_SpinSound" => ("转动音效", "转动期间循环播放；取消或停准后停止。"),
                "m_HitTargetSound" => ("停准音效", "最终停准目标奖格后播放一次。"),
                "m_StartedEvent" => ("开始转动时", "接受新一轮转动后触发。"),
                "m_ResultEvent" => ("停准后（返回编号）", "整个表现结束后触发，传入本次目标奖格的编号。"),
                "m_RewardEvent" => ("停准后（返回奖励）", "已配置奖励时触发，返回本轮锁定的映射（对象数据仍为引用）。选择一种完成通知处理业务，避免重复发奖。"),
                "m_CompleteEvent" => ("停准后", "整个表现结束后触发，不带参数。"),
                "m_CancelledEvent" => ("取消时", "活动中的转动被取消时触发，不会触发停准事件。"),
                "m_SpinStartDelay" => ("开始前等待（秒）", "发起转动后，先等待多久再开始动作。"),
                "m_UseUnscaledTime" => ("游戏暂停时仍转动", "开启后忽略 Time.timeScale，也会让指针使用相同时间设置。"),
                "m_CustomAccelerationRhythm" => ("自定义起转节奏", "默认关闭。只在同样的起转用时下还需要不同节奏时启用。"),
                "m_AccelerationRhythm" => ("起转节奏曲线", "曲线高的时段提速更明显。使用非负数，不能全为 0；端点衔接由组件处理。"),
                "m_CustomDecelerationRhythm" => ("自定义停转节奏", "默认关闭。只在同样的停转用时下还需要不同节奏时启用。"),
                "m_DecelerationRhythm" => ("停转节奏曲线", "曲线高的时段降速更明显。使用非负数，不能全为 0；端点衔接由组件处理。"),
                "m_CustomInRhythm" => ("自定义回拉节奏", "调整起转前小幅回拉在时间上的分布。"),
                "m_InRotationCurve" => ("回拉节奏曲线", "曲线高的时段回拉动作更明显。使用非负数，不能全为 0。"),
                "m_CustomOutRhythm" => ("自定义回摆节奏", "调整停下后小幅回摆在时间上的分布。"),
                "m_OutRotationCurve" => ("回摆节奏曲线", "曲线高的时段回摆动作更明显。使用非负数，不能全为 0。"),
                _ => (property.displayName, property.tooltip)
            };
            return new GUIContent(label, tooltip);
        }

        private bool IsRhythmVisible(string propertyName) {
            // 关闭小幅摆动时，隐藏它的时长及节奏设置，避免无效选项占据界面。
            if (propertyName is "m_InRotationTime" or "m_CustomInRhythm" or "m_InRotationCurve") {
                if (serializedObject.FindProperty("m_InRotationAngle").floatValue <= 0) {
                    return false;
                }
            }

            if (propertyName is "m_OutRotationTime" or "m_CustomOutRhythm" or "m_OutRotationCurve") {
                if (serializedObject.FindProperty("m_OutRotationAngle").floatValue <= 0) {
                    return false;
                }
            }

            var toggle = propertyName switch {
                "m_AccelerationRhythm" => "m_CustomAccelerationRhythm",
                "m_DecelerationRhythm" => "m_CustomDecelerationRhythm",
                "m_InRotationCurve" => "m_CustomInRhythm",
                "m_OutRotationCurve" => "m_CustomOutRhythm",
                _ => null
            };
            return toggle == null || serializedObject.FindProperty(toggle).boolValue;
        }

        private static bool DrawFoldout(string key, string title, bool defaultExpanded) {
            var preferenceKey = k_PreferencePrefix + key;
            var expanded = EditorPrefs.GetBool(preferenceKey, defaultExpanded);
            var next = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);

            if (next != expanded) {
                EditorPrefs.SetBool(preferenceKey, next);
            }

            return next;
        }

        // 校准只建立角度映射，不识别图片或 Spine 内容，也不依赖 PointerController。
        private void DrawCalibration(WheelController wheel) {
            if (!wheel.Reel) {
                EditorGUILayout.HelpBox("先在“基本设置”绑定“旋转的圆盘”。", MessageType.Info);
                return;
            }

            if (Application.isPlaying) {
                EditorGUILayout.HelpBox("退出运行模式后可设置奖格与指针的对应位置；测试动画请展开“试转”。", MessageType.Info);
                return;
            }

            if (!wheel.ValidateSectors(out var layoutError)) {
                EditorGUILayout.HelpBox(layoutError + "请先完成“每格大小”。", MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox("先选一格作为位置参照。组件会把它对准指针，记住整张圆盘的停止位置。若各格大小不同，先在“每格大小”中填写角度。", MessageType.Info);
            var selected = DrawSectorChoice("用于对齐的奖格", m_CalibrationIndex, wheel);

            if (selected != m_CalibrationIndex) {
                m_CalibrationIndex = selected;
                m_CalibrationCenter = wheel.GetReward(selected)?.View?.transform;
            }

            // 首次填写奖励和指针后立即提供默认引用，不要求重新选中组件。
            if (!m_CalibrationCenter) {
                m_CalibrationCenter = wheel.GetReward(selected)?.View?.transform;
            }

            if (!m_CalibrationPointer) {
                var pointer =
                    serializedObject.FindProperty("m_PointerController").objectReferenceValue as PointerController;
                
                if (pointer) {
                    m_CalibrationPointer = pointer.transform;
                }
            }

            m_CalibrationCenter = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("此奖格的中心物体", "拖入代表上方奖格位置的物体，默认使用该奖励的表现对象。物体轴心须位于奖格中心线上，必须是圆盘的子物体，且不能位于圆盘正中心。"),
                m_CalibrationCenter, typeof(Transform), true);
            m_CalibrationPointer = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("指向中奖格的指针", "拖入固定指针，组件用圆盘中心到它的连线确定奖格应停在哪里。指针不能是圆盘的子物体；若轴心位置不准确，可使用单独的标记物体。"),
                m_CalibrationPointer, typeof(Transform), true);
            var valid = TryCalculateCalibrationAngle(wheel, m_CalibrationCenter, m_CalibrationPointer, out _,
                out var error);
            EditorGUILayout.HelpBox(valid
                ? "点击下方按钮，圆盘会转到所选奖格中心对准指针的位置。奖格须按顺时针排列为 0、1、2……；调整美术位置后请重新对齐。"
                : error, MessageType.None);

            using (new EditorGUI.DisabledScope(!valid)) {
                if (GUILayout.Button(new GUIContent("将此奖格对准指针并保存", "转动圆盘，让所选奖格中心对准固定指针，并保存用于计算各格停止位置的角度基准。支持撤销。"))) {
                    AlignAndSave(wheel, m_CalibrationIndex, m_CalibrationCenter, m_CalibrationPointer);
                }
            }
        }

        // 用明确的空间参照计算，不能把“按已保存角度移动”当成校准正确的证据。
        private static bool TryCalculateCalibrationAngle(WheelController wheel, Transform center, Transform pointer,
            out float angle, out string error) {
            angle = 0;

            if (!center || !pointer) {
                error = "请填写上方两个物体。奖格物体的位置代表奖格中心方向，指针物体的位置代表中奖方向。";
                return false;
            }

            var reel = wheel.Reel;

            if (center == reel || !center.IsChildOf(reel)) {
                error = "“此奖格的中心物体”必须放在“旋转的圆盘”下面，不能直接使用圆盘本身。";
                return false;
            }

            if (pointer.IsChildOf(reel)) {
                error = "指针必须放在“旋转的圆盘”外，否则会跟着圆盘一起旋转。";
                return false;
            }

            // 在 Reel 父坐标系中去掉 X/Y 倾斜，计算绕本地 Z 轴旋转所需的绝对角度。
            // 保留缩放的影响，父级旋转、非均匀缩放及 Reel 自身缩放都不会混入角度差。
            var target = reel.parent ? reel.parent.InverseTransformPoint(pointer.position) - reel.localPosition
                : pointer.position - reel.position;
            var withoutZ = reel.localRotation
                           * Quaternion.Inverse(Quaternion.AngleAxis(reel.localEulerAngles.z, Vector3.forward));
            target = Quaternion.Inverse(withoutZ) * target;
            var source = Vector3.Scale(reel.InverseTransformPoint(center.position), reel.localScale);
            var from = new Vector2(source.x, source.y);
            var to = new Vector2(target.x, target.y);

            if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) {
                error = "奖格中心和指针不能与圆盘中心重合，请使用独立标记物体。";
                return false;
            }

            angle = Mathf.Repeat(Vector2.SignedAngle(from, to), 360);

            if (float.IsNaN(angle) || float.IsInfinity(angle)) {
                error = "无法计算位置，请检查转盘及其父物体是否存在零缩放或无效坐标。";
                return false;
            }

            error = null;
            return true;
        }

        private bool AlignAndSave(WheelController wheel, int sectorIndex, Transform center, Transform pointer) {
            if (!TryCalculateCalibrationAngle(wheel, center, pointer, out var angle, out _)) {
                return false;
            }

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Auto align wheel");
            Undo.RecordObject(wheel.Reel, "Auto align wheel");
            serializedObject.FindProperty("m_ZeroAngle").floatValue = Mathf.Repeat(
                angle - wheel.GetSectorAngle(sectorIndex) + wheel.ZeroAngle, 360);
            serializedObject.ApplyModifiedProperties();
            wheel.SnapToSector(sectorIndex);
            SaveReelChange(wheel);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        private static void SaveReelChange(WheelController wheel) {
            PrefabUtility.RecordPrefabInstancePropertyModifications(wheel.Reel);
            EditorUtility.SetDirty(wheel.Reel);
            SceneView.RepaintAll();
        }

        private static int DrawSectorChoice(string label, int index, WheelController wheel) {
            var choices = new string[wheel.SectorsCount];

            for (var sector = 0; sector < choices.Length; sector++) {
                var reward = wheel.GetReward(sector);
                choices[sector] = reward == null ? $"第 {sector} 格" : $"第 {sector} 格 · {reward.Name}";
            }

            return EditorGUILayout.Popup(label, Mathf.Clamp(index, 0, choices.Length - 1), choices);
        }

        private void DrawDebug(WheelController wheel) {
            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("先完成上方“对齐奖格与指针”，再点击 Unity 顶部的播放按钮进入运行模式。选择一个目标奖格并试转，检查指针是否停准。",
                    MessageType.Info);
                return;
            }

            if (!wheel.ValidateSectors(out var layoutError)) {
                EditorGUILayout.HelpBox(layoutError, MessageType.Error);
                return;
            }

            if (!wheel.ValidateRewards(out var rewardError)) {
                EditorGUILayout.HelpBox(rewardError, MessageType.Error);
                return;
            }

            m_TestIndex = DrawSectorChoice("本次想转到哪一格", m_TestIndex, wheel);

            m_OverrideTestOffset = EditorGUILayout.Toggle("本次覆盖格内落点", m_OverrideTestOffset);

            if (m_OverrideTestOffset) {
                m_TestOffset = EditorGUILayout.Slider(new GUIContent("本次格内位置（%）", "50% 为格中心，沿顺时针从 1% 量到 99%。仅影响这次测试。"),
                    (m_TestOffset + 0.5f) * 100, 1, 99) / 100 - 0.5f;
            }

            var state = wheel.State switch {
                WheelState.Idle => "未转动",
                WheelState.Starting => "起转前回拉",
                WheelState.Accelerating => "起转中",
                WheelState.Spinning => "转动中，等待结果",
                WheelState.Stopping => "转动中，已确定结果",
                WheelState.Decelerating => "停转中",
                WheelState.Settling => "停下后回摆",
                _ => wheel.State.ToString()
            };
            
            EditorGUILayout.LabelField("当前状态", state);
            EditorGUILayout.LabelField("当前转速（圈/秒）", $"{Mathf.Abs(wheel.CurrentAngularSpeed) / 360f:F2}");
            EditorGUILayout.LabelField("本次目标奖格",
                wheel.TargetSectorIndex < 0 ? "尚未指定" : $"第 {wheel.TargetSectorIndex} 格");
            EditorGUILayout.LabelField("已停准奖格", wheel.WinSectorIndex < 0 ? "尚无结果" : $"第 {wheel.WinSectorIndex} 格");
            
            if (wheel.WinReward != null) {
                EditorGUILayout.LabelField("停准奖励", wheel.WinReward.Name);
            }

            m_RandomTarget = EditorGUILayout.Toggle("随机选择目标（仅调试）", m_RandomTarget);

            using (new EditorGUI.DisabledScope(wheel.IsSpinning || !wheel.isActiveAndEnabled)) {
                if (GUILayout.Button("转到选中奖格")) {
                    if (m_OverrideTestOffset) {
                        wheel.TryStartSpin(TestIndex(wheel), m_TestOffset);
                    } else {
                        wheel.TryStartSpin(TestIndex(wheel));
                    }
                }

                if (GUILayout.Button("开始转动，等待结果")) {
                    wheel.BeginSpin();
                }
            }

            using (new EditorGUI.DisabledScope(!wheel.CanSubmitResult)) {
                if (GUILayout.Button("提交选中奖格并停转")) {
                    if (m_OverrideTestOffset) {
                        wheel.StopAt(TestIndex(wheel), m_TestOffset);
                    } else {
                        wheel.StopAt(TestIndex(wheel));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!wheel.IsSpinning)) {
                if (GUILayout.Button("取消转动")) {
                    wheel.CancelSpin();
                }
            }
        }

        private int TestIndex(WheelController wheel) =>
            m_RandomTarget ? UnityEngine.Random.Range(0, wheel.SectorsCount) : m_TestIndex;
    }
}