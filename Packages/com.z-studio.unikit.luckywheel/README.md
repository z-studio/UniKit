# Lucky Wheel

Unity 6000.0+ 的转盘表现组件，支持等分、不等分奖格和固定指针。业务指定中奖索引，组件负责旋转、停准、指针拨动和完成通知，无第三方 Tween 依赖。

最常用的操作只有：**`TryStartSpin(index)` 转到指定格，`Completed(int)` 接收完成结果。** 如果中奖结果稍后才返回，使用 `BeginSpin()` 和 `StopAt(index)`。

## 1. 首次接入

### 摆放圆盘与指针

```text
转盘界面（挂 WheelController）
├─ 圆盘（绑定到“旋转的圆盘”）
│  ├─ 第 0 格物体
│  ├─ 第 1 格物体
│  └─ ……按顺时针排列
└─ 固定指针（需要拨动效果时挂 PointerController）
```

- 圆盘轴心放在圆心。组件修改这个物体的 `Rotation → Z`，不要让其他动画同时控制它的旋转。
- 指针放在旋转圆盘外，避免跟着圆盘一起转；需要摆动时，将指针轴心放在摆动支点。
- 美术由你准备，组件不会创建、排列或播放奖格内容。奖格物体可以包含图片、文字、Spine 或特效。

### 按顺序完成配置

1. **基本设置**：绑定“旋转的圆盘”。只按索引使用时填写奖格数量；配置了奖励列表后，数量自动跟随列表。
2. **奖格与奖励**：可选。按美术的顺时针顺序填写第 0、1、2……格，绑定名称、奖格物体和奖励数据。没有独立的奖格 ID，名称可重复或留空。
3. **每格大小**：等分保持默认。不等分选择“每格单独设置”，按索引填写每格的实际角度；每格大于 0，合计 360°。角度只描述形状，不是中奖概率。
4. **对齐奖格与指针**：选择一格，绑定“此奖格的中心物体”和“指向中奖格的指针”，点击“将此奖格对准指针并保存”。圆盘会转到该格中心对准指针的位置，其他格的位置按宽度推算。
5. **试转**：进入 Unity 运行模式，在“试转”中分别选择第 0、1、2 格测试，检查返回索引与实际指向是否一致。退出运行模式后调整并保存配置。

对齐时，奖格中心物体必须是圆盘的子物体，其轴心位于该扇形两条边界正中间的方向上，并且不能与圆心重合。默认取绑定的奖格物体；图案轴心偏了，就在圆盘下新建空物体作为中心标记。指针也可以使用单独的位置标记。

这一步记录的是“圆盘转到哪里才算对准指针”。圆盘的 `Rotation Z = 0` 不代表第 0 格一定在顶部。对齐只需首次配置时做；修改奖格布局、每格角度或指针位置后再重新对齐，不需要每次转动前操作。

## 2. 常用调用

### 结果已经确定

Inspector 配好后直接指定索引，不需要额外初始化。以下脚本假定由它独占控制这个转盘，运行前将组件拖到 `wheel` 栏：

```csharp
using UnityEngine;
using ZStudio.UniKit.LuckyWheel;

public class WheelExample : MonoBehaviour {
    [SerializeField] 
    private WheelController wheel;

    private void OnEnable() => wheel.Completed += OnCompleted;

    private void OnDisable() {
        wheel.Completed -= OnCompleted;
        wheel.CancelSpin(); // 退出界面时停止本轮，不产生完成结果。
    }

    public void SpinFirstSector() {
        if (!wheel.TryStartSpin(0)) {
            Debug.Log("未开始：请检查配置、组件是否启用，以及是否正在转动。", this);
        }
    }

    private void OnCompleted(int index) {
        var reward = wheel.GetReward(index); // 没有奖励列表时返回 null。
        Debug.Log($"已停准第 {index} 格：{reward?.Name}", this);
        // 在这里显示结果；实际发奖由业务层处理。
    }
}
```

索引从 **0** 开始。`TryStartSpin(2)` 表示转到第 2 格。返回 true 表示本轮已受理，完成事件在减速和可选回摆结束后才触发。

Inspector 的“转动完成时（返回奖格索引）”与 C# 的 `Completed(int)` 表示同一时机。同一份业务处理选择一种绑定方式即可。完成事件不依赖奖励列表；取消只通知“取消时”/ `Cancelled`，不通知完成。

### 结果稍后才返回

```csharp
// 第一步：开始本轮等待，返回 true 后由业务发起请求。
bool accepted = wheel.BeginSpin();

// 第二步：本轮结果返回后，提交目标索引。
// 业务须先确认返回结果仍属于当前请求，再调用：
bool submitted = wheel.StopAt(2);

// 请求失败、超时或退出界面时：
wheel.CancelSpin();
```

以上是三个不同操作时机，不是让它们连续执行。转盘在收到结果前持续转动；结果可以在等待延迟、回拉、加速或匀速阶段提交，组件会完成起步动作后再停转。同一轮只接受第一次结果。

`CanSubmitResult` 只表示当前轮次还能提交结果，**不能识别网络响应属于哪次请求**。请求编号、过期响应、超时和重试由业务管理，避免上一轮的返回结果停掉新一轮转盘。

### 返回值和使用边界

| 操作 | 说明 |
| --- | --- |
| `TryStartSpin(index)` | 直接指定结果并开始转动；忙碌、禁用、未绑定圆盘或奖格配置无效时返回 false |
| `BeginSpin()` | 开始转动并等待结果；开始条件同上 |
| `StopAt(index)` | 向等待中的本轮提交结果；未开始或已经提交结果时返回 false |
| `CancelSpin()` | 保留圆盘当前角度，停止音效并复位指针；空闲时调用不会产生取消通知 |
| `GetReward(index)` | 读取这一格的奖励配置；没有奖励列表时返回 null |

可操作时，非法索引、非法格内位置会抛出异常；自定义布局非法时，`StopAt`、角度查询等也会拒绝操作。配置接口只允许在空闲时使用，转动中修改会抛出异常。禁用或销毁转盘组件会自动取消当前轮次。

## 3. 配置参考

### 转动表现

一轮顺序是：**开始前等待 → 回拉（可选）→ 加速 → 匀速 → 减速 → 回摆（可选）→ 完成**。

| 分组 | 常用参数 | 调整效果 |
| --- | --- | --- |
| 基本设置 | 游戏暂停时仍转动 | 开启后忽略 `Time.timeScale`，指针跟随同一时间设置 |
| 起转 | 开始前等待、起转用时 | 控制延迟和从静止提速的时间；起转用时越短越利落 |
| 起转 | 起转前回拉、回拉用时 | 正式转动前向反方向轻摆；角度为 0 时关闭 |
| 转动 | 转动方向、每秒转几圈 | 决定圆盘的运动方向与稳定转速；不改变顺时针编号约定 |
| 转动 | 至少保持转动、至少转几圈 | 分别约束最少匀速时间与整轮正向圈数；回拉和回摆不计入圈数 |
| 停转 | 停转用时 | 从稳定转速降到静止的时间，不包含回摆；越短越干脆 |
| 停转 | 停下后回摆、回摆用时 | 先略过目标再反向回到本次停止位置；角度为 0 时关闭 |
| 音效 | 转动音效、停准音效 | 前者转动期间循环，后者最终停准时播放一次，均可留空 |

时间、速度、圈数与准确落点不能全部任意固定。组件保持稳定转速、加减速时间与落点，必要时延长匀速段。因此“至少保持转动”不是整轮总时长，已等待的匀速时间和圈数会计入。

默认节奏即可自然转动。需要特殊表现时，在对应动作下启用自定义节奏：横轴为阶段时间 0～1，纵轴为非负作用权重，曲线较高处动作更明显。权重不是角度进度，不能全为 0、负数或无效数值；组件负责归一化和阶段衔接。回拉/回摆关闭时，其节奏选项也隐藏。

### 奖格大小与格内停止位置

不等分示例：四格按顺时针分别为 `20、70、110、160` 度。每格角度必须与实际美术一致，组件不会识别图片中的分界线。

“停在格子哪里”可选中心、指定格内位置或格内范围随机。界面统一用 **1%～99%**，50% 是中心，从该格的起始边界沿顺时针量到结束边界。随机只发生在已经指定的目标格内，不会随机选择中奖格。

运行时 `offset` 以目标格宽度为单位，范围 `[-0.49, 0.49]`：0 是中心，正值沿顺时针。与界面的换算是 `offset = 百分比 / 100 - 0.5`。

```csharp
// 在空闲时配置：停在目标格的 80% 位置。
wheel.ConfigureLanding(WheelLandingMode.Fixed, offset: 0.3f);

// 单次转动也可以覆盖默认配置，例如停在第 1 格的 30% 位置。
wheel.TryStartSpin(1, offset: -0.2f);
// 等待流程同样支持 StopAt(index, offset)。
```

### 奖励数据

`WheelReward` 只保存这一格的名称、场景物体和业务数据。多个格子可以使用相同名称、绑定同一种奖励；奖励物品 ID、数量和概率由业务数据定义。

| 字段 | 用途 |
| --- | --- |
| `Name` | 可选备注名称，便于检查配置，不控制美术文字 |
| `View` | 已有奖格的场景物体，仅保存引用；对齐时可作为位置参照 |
| `Data` | 可选 Unity 资产，例如自定义 `ScriptableObject` |
| `UserData` | 仅运行时使用的普通 C# 对象，不写入场景或 Prefab |

`ConfigureRewards` 复制每项配置，但其中的对象与业务数据仍为原引用。结果锁定时的配置副本保存在 `TargetReward`，完成后可读 `WinReward`；如需读取它，请在开始下一轮或修改配置之前保存所需数据。完成事件始终返回索引，当前列表配置可通过 `GetReward(index)` 获取。

### 运行时配置（按需使用）

Inspector 配置完整时可跳过本节。下列配置都在空闲时调用；每项示例按各自适用的布局使用。

```csharp
// 仅编号模式：8 个等分奖格，0 号格对准指针时圆盘的 Rotation Z 为 22.5°。
wheel.Configure(reel, sectorsCount: 8, zeroAngle: 22.5f);

// 运动参数只管运动，不会重置已经设置的格内落点。
// API 的稳定转速使用度/秒，720 度/秒 = Inspector 中的 2 圈/秒。
wheel.ConfigureMotion(duration: 3f, minimumTurns: 3,
    direction: SpinDirection.Counter, waitingSpeed: 720f,
    accelerationTime: 0.4f, startDelay: 0, useUnscaledTime: true);

// 减速、回拉与回摆的参数分别配置。
wheel.ConfigureRhythm(decelerationTime: 1.8f);
wheel.ConfigureStages(inDuration: 0.2f, inAngle: 3,
    outDuration: 0.3f, outAngle: 2);
```

| 接口 | 用途与约束 |
| --- | --- |
| `Configure(reel, sectorsCount, zeroAngle)` | 配置圆盘、格数和角度基准，不改变当前姿态 |
| `ConfigureRewards(rewards)` | 整体配置奖励列表，列表数量决定格数，不接受空列表或空条目 |
| `ClearRewards()` | 清除奖励列表并保留当前格数，不销毁奖格物体 |
| `ConfigureSectorWidths(widths)` | 配置每格角度，总和须为 360°；已有奖励列表时数量须一致 |
| `ConfigureEqualSectors()` | 恢复全部等分 |
| `ConfigureLanding(mode, offset, randomRange)` | 配置默认格内停止位置或随机范围 |
| `ConfigureRhythm(...)` / `ConfigureStages(...)` | 配置减速及可选节奏；传入合法曲线启用自定义节奏 |
| `SnapToSector(index, offset)` | 空闲时立即对齐指定格，不触发完成事件；省略 offset 时对齐中心 |
| `GetSectorAngle(index, offset)` | 查询目标格对准指针时，圆盘所需的本地 Z 角 |
| `GetSectorWidth(index)` / `GetSectorCenterOffset(index)` | 查询格宽，以及从 0 号中心沿顺时针到该格中心的角度 |

更换不等分布局的格数时，先恢复等分，再配置新格数或新奖励列表，最后设置新角度列表并重新对齐。已有奖励时 `Configure` 的格数必须匹配列表；不要通过配置接口单独改变其中一项造成数量不一致。

### 可读状态

- `IsSpinning`：本轮仍在进行中，包含等待延迟、回拉、等待结果、停转和回摆。
- `CanSubmitResult`：本轮尚未锁定目标，可以接受第一次结果。
- `TargetSectorIndex`：已锁定的目标索引，未指定时为 -1。
- `WinSectorIndex`：已完成的结果索引；开始下一轮或取消活动轮次后为 -1。
- `CurrentAngularSpeed`：实际转速，单位度/秒，顺时针为负；等待延迟、空闲时为 0。
- `State`：内部阶段；`Stopping` 表示已有目标、仍在匀速补足路程，`Decelerating` 才是真正减速。

## 4. 指针拨动（可选）

静止图片也能充当指针。需要跨格摆动或点击音效时，添加 `PointerController` 并拖到转盘的“指针”栏；组件会自动要求 `AudioSource`，音效可留空。

- **拨动角度**控制偏开幅度，0 表示只播放音效；**拨开用时 / 回位用时**分别控制两段速度。
- **自动选择拨动方向**推荐开启：向圆盘实际运动的反方向让开，包含回拉和回摆。关闭后才显示固定方向。
- 原位是初始化时记住的姿态；连续跨格从当前姿态重新拨开，结束或取消时恢复原位。一帧跨多格只反馈一次。
- 指针 Inspector 的试拨仅在运行模式可用，使用真实时间，游戏暂停时也能查看。试拨前先停下圆盘，避免重复触发。

独立使用时，`Flick()` 按固定方向和游戏时间拨动；转盘调用的 `Flick(wheelClockwise, useUnscaledTime)` 会按自动/固定模式决定指针方向。正常接入无需手动调用这两个方法。

## 5. Demo 与源码阅读

打开 `Samples/Demo/Scenes/Demo.unity`，进入运行模式。顶部面板支持选择索引、直接转动、开始等待、提交结果和取消。Inspector 的“试转”还支持单次格内位置与调试随机目标；业务结果不会自动随机。

Demo 的格数直接使用转盘配置，不另设一份。它为不等分布局生成底图并排布文字，仅用于核对索引与扇形；正式游戏的美术排布由业务维护。运行界面的英文标签使用场景自带字体，Inspector 配置为中文。

包依赖 Audio 模块和 Unity UI。运行时核心不引用 UGUI，Demo 使用 UGUI 与 Input System；运行示例前，请安装 `com.unity.inputsystem`，并将 Player Settings → Active Input Handling 设为 Input System 或 Both。

| 文件 | 阅读重点 |
| --- | --- |
| `WheelReward.cs` | 一格奖励的配置与数据边界 |
| `PointerController.cs` | 收到拨动 → 让开 → 回位 → 取消复位 |
| `WheelController.cs` | 先读类注释和状态，再读 `TryStartSpin / BeginSpin / StopAt`，最后看 `Advance` |
| `WheelControllerEditor.cs` / `PointerControllerEditor.cs` | 仅负责配置、对齐与调试，不参与游戏中的结果选择 |
| `Demo.cs` | 按钮和完成事件；界面在 `LateUpdate` 统一刷新，布局演示放在 `RefreshSectorLayout` |
| `DemoSectorGraphic.cs` | 用三角形绘制不等分扇形底图，不计算中奖结果 |

核心计算分三步：`GetSectorAngle` 把索引和格内位置换成停止角度；`PlanMainStop` 规划沿指定转向到达落点的路程；`Advance` 推进各阶段。`CompleteSpin` 先收尾状态再发完成通知，回调中可以开始下一轮。`RhythmProfile` 处理节奏权重与积分，普通接入无需修改。

## 6. 常见问题

| 现象 | 优先检查 |
| --- | --- |
| 点击没有转动 / 返回 false | 圆盘引用、组件是否启用、是否已有活动轮次，以及 Console 中的配置错误 |
| 第 0 格正确，其他格错误 | 奖格是否按顺时针对应美术，不等分角度是否与实际一致 |
| 所有格都偏同样角度 | 用准确的奖格中心标记和指针位置重新对齐 |
| 指针跟着圆盘转 | 将指针移到旋转圆盘外 |
| 指针围绕奇怪的位置摆动 | 轴心应在摆动支点；可用父物体作为支点，子物体放图片 |
| 收到结果却不能提交 | 是否未开始、已经提交过结果，或者这次响应属于已经取消的旧请求 |
| 一次结果处理了多遍 | 是否同时通过 Inspector 和代码订阅处理了同一份业务 |

适用范围是单个圆盘绕本地 Z 轴旋转、固定指针、等分或不等分奖格。抽奖概率、发奖、网络协议和多个指针判奖由业务实现。
