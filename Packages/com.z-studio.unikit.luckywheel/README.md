# Lucky Wheel

Unity 6000.0+ 的转盘组件，支持等分和不等分扇区。支持 UI / 2D 对象绕本地 Z 轴旋转，组件负责表现，由业务方指定结果。无第三方 Tween 依赖。

## 第一次使用怎么调

Inspector 按“基本设置 → 起转 → 转动 → 停转”排列，默认值即可自然转动。

- **起转用时**：小一点，起转更利落；大一点，起转更轻柔。
- **每秒转几圈、至少保持转动、至少转几圈**：控制中间转动的快慢和最少持续量，起转、停转用时另计。
- **停转用时**：小一点，停得更干脆；大一点，停得更缓慢。
- **起转前回拉、停下后回摆**：可选的小幅摆动，角度设为 0 就关闭；关闭时不显示对应的额外时长。

Inspector 的速度以“圈/秒”显示，例如 2 表示每秒两圈；运行时 API 的 `waitingSpeed` 仍使用“度/秒”（720 度/秒等于每秒两圈）。

音效和事件可以稍后配置。开始前等待、起转与回拉节奏在“起转”中设置，停转与回摆节奏在“停转”中设置；“游戏暂停时仍转动”在“基本设置”中。只有需要特殊节奏时才使用自定义曲线。

## 第一次接入：先让一格停准

组件负责把你指定的奖励转到指针下。它不抽奖、不创建美术，也不发奖。

先按下面的层级摆放；圆盘的轴心放在圆心，指针的轴心放在它的摆动支点：

```text
转盘界面（挂 WheelController）
├─ 圆盘（拖到“旋转的圆盘”）
│  ├─ 第 0 格物体
│  ├─ 第 1 格物体
│  └─ ……按顺时针排列
└─ 固定指针（需要拨动表现时挂 PointerController）
```

1. **绑定圆盘**：在“基本设置 → 旋转的圆盘”中拖入圆盘物体。
2. **填写奖励**：在“奖格与奖励”创建列表，按顺时针填写第 0、1、2……格。列表索引就是奖格编号，无需填写 ID；“奖格物体”可绑定已有美术，但不会自动生成或移动它。仅按编号使用时也可以不填奖励列表。
3. **填写每格大小**：等分圆盘保持默认；不等分时，在“每格大小 → 每格单独设置”中填写实际角度，每格大于 0，合计 360°。角度不是中奖概率。
4. **对齐奖格与指针**：选择一格，绑定“此奖格的中心物体”和“指向中奖格的指针”，点击“将此奖格对准指针并保存”。圆盘会转到该格对准指针的位置，其他格按宽度推算，无需逐格校准。
5. **试转**：进入 Unity 运行模式，在“试转”选择第 0、1、2 格分别转动，检查指针与画面是否对应。退出运行模式后再调整并保存配置。

对齐时，“中心物体”的轴心须位于该扇形两条边界正中间的方向上，且不能与圆心重合。默认取奖格物体；如果图案轴心偏了，可在圆盘下新建空物体作为中心标记。指针位置同样可以用准确的标记替代。调整美术布局、指针位置或每格大小后，请重新对齐。

“旋转角度”指圆盘物体在 Transform / Rect Transform 中的 `Rotation → Z`。它为 0 时，第 0 格不一定在顶部；对齐按钮就是帮你记录美术与指针之间的角度关系。

## 指针拨动（可选）

静止图片也能充当指针。只有需要跨格摆动和点击音效时，才添加 `PointerController` 并绑定到转盘的“指针”栏。

| 设置 | 怎么理解 |
| --- | --- |
| 拨动音效 | 每次跨格播放一次，留空仍可摆动 |
| 拨动角度 | 指针相对原位偏开多少度，0 表示只播放音效 |
| 拨开用时 / 回位用时 | 分别控制让开与弹回的速度 |
| 自动选择拨动方向 | 推荐开启：向圆盘实际运动的反方向让开，也跟随回拉和回摆 |
| 固定拨动方向 | 关闭自动模式后才显示，从画面正面看指针自身的摆动方向 |

指针必须在旋转圆盘外，否则会一起转。组件记住初始化时的姿态作为原位；连续跨格会从当前姿态重新拨开，最终回到同一原位。不要让其他动画同时控制这个物体的旋转。运行模式下可在指针 Inspector 中模拟两个方向的拨动；试拨使用真实时间，游戏暂停时也能查看效果。

## 推荐的业务调用

**统一按奖格索引起转，索引从 0 开始。** Inspector 配好后不需要再调用初始化或配置接口。完成时统一监听 `Completed(int)`，通过索引读取奖励配置。下面是可以直接挂到物体上的最小示例，转到第 0 格：

```csharp
using UnityEngine;
using ZStudio.UniKit.LuckyWheel;

public class MyWheel : MonoBehaviour {
    [SerializeField] 
    private WheelController wheel;

    private void OnEnable() => wheel.Completed += OnCompleted;
    private void OnDisable() => wheel.Completed -= OnCompleted;

    // 可绑定到 UI 按钮。
    public void SpinFirstSector() {
        if (!wheel.TryStartSpin(0)) {
            Debug.Log("未开始：请检查圆盘配置、组件是否启用，以及是否正在转动。", this);
        }
    }

    private void OnCompleted(int index) {
        var reward = wheel.GetReward(index); // 未配置奖励列表时为 null。
        Debug.Log($"已停准第 {index} 格：{reward?.Name}", this);
        // 在这里显示结果；实际奖励由业务规则或服务器确定。
    }
}
```

**完成通知统一返回奖格索引。** 无论有没有配置奖励列表，转到第 2 格都使用 `TryStartSpin(2)`。 编号从 0 开始。`StartSpin` 适用于无需返回值的简单调用；`TryStartSpin` 会告诉你这次操作是否被受理。非法编号会抛出异常。

**结果稍后才返回：先 `BeginSpin()`，得到结果后再 `StopAt(index)`。** 这两次调用必须属于同一轮；失败、超时或退出界面时由业务方调用 `CancelSpin()`。结果只允许提交一次，取消不产生中奖通知。可以在“试转”用“开始转动，等待结果 → 提交选中奖格并停转”模拟这个过程。

Inspector 只保留一个“转动完成时（返回奖格索引）”，代码中对应 `Completed(int)`，无论是否配置奖励列表都触发。同一份业务处理选择 Inspector 绑定或代码订阅即可，避免重复处理；单次调用的 callback 可用于该次操作的后续逻辑。Inspector 中的“开始转动时”通知在操作受理时触发，可能还处在开始前等待阶段。

## 每格大小与格内停止位置

默认等分且停在格子中心，无需额外配置。两项按需展开：

- **每格大小 → 每格单独设置**：按奖格编号填写每格的角度宽度，必须大于 0，总和为 360°。例如四格可为 `20、70、110、160` 度。宽度只定义几何范围，不代表中奖概率；扇区数量必须与奖励列表一致。这里不会重建业务美术。
- **停转 → 停在格子哪里**：选择居中、指定格内位置、格内范围随机。Inspector 用 1%～99% 表示安全范围，50% 为中心，沿顺时针从该格的起始边界量到结束边界。宽格和窄格都按各自的实际宽度计算。

起转始终从圆盘当前角度开始，不会先吸附到格子中心。随机落点只在已指定的目标格内选择，不随机决定中奖格；开始延迟、等待结果或动画阶段切换都不会改变本轮已锁定的落点。

```csharp
// 先配置 Reel 和奖励。没有奖励映射时，宽度列表会同时确定格数。
wheel.ConfigureSectorWidths(new[] { 20f, 70f, 110f, 160f });

// 运行时 offset = 格内百分比 / 100 - 0.5；0 是中心。
wheel.ConfigureLanding(WheelLandingMode.Fixed, offset: 0.3f); // 80% 处
wheel.StartSpin(1);

// 空闲后可改为格内 20%～70% 随机：
wheel.ConfigureLanding(WheelLandingMode.Random,
    randomRange: new Vector2(-0.3f, 0.2f));

// 单次调用的显式位置覆盖默认落点设置：
wheel.TryStartSpin(1, offset: -0.2f); // 30% 处

// 等待模式对应 StopAt(index, offset)。
// 立即设置初始姿态也可不居中，不触发奖励事件：
wheel.SnapToSector(1, offset: 0.1f);
```

上述配置接口仅在空闲时调用；示例中的多种模式是不同轮次的用法。`GetSectorWidth(index)` 返回实际宽度，`GetSectorCenterOffset(index)` 返回从 0 号中心沿顺时针量到该格中心的角度，`GetSectorAngle(index, offset)` 返回准确落点的本地 Z 角。自定义宽度先完整校验再替换，允许 0.001° 的求和误差并归一化消除舍入误差，保证圆周闭合。非法静态宽度会阻止转动和校准，不会静默改为等分。

`ConfigureEqualSectors()` 恢复等分。更换自定义扇区的格数时，已有奖励须同步；可先恢复等分、配置新奖励，再配置新宽度。`ConfigureMotion` 的 `landingJitter` 参数仍可设置以中心为基准的随机范围（0 为居中）；使用独立落点配置时，在 `ConfigureMotion` 之后调用 `ConfigureLanding`。

## 奖格配置：内容与结果分开

在 Inspector 的 **奖格与奖励** 中，点击“按当前格数创建奖励配置”。每一项对应实际圆盘的一格，编号从 0 开始，配置后格数自动跟随列表。

奖格仅通过列表索引定位，不需要独立 ID。多个格子可以填写相同名称、绑定同一种奖励数据；奖励物品 ID 由业务数据自行定义。

- **备注名称（可选）**：帮助识别配置，不决定画面内容。
- **奖格物体（可选）**：绑定该格在场景中的根 GameObject。内部可以包含 Image、Spine、粒子、文字或任意混合内容，不依赖具体渲染组件。
- **奖励数据（可选）**：绑定自己的 ScriptableObject 等资产。奖励类型、数量、概率等字段由业务自行定义。

表现对象和业务数据只保存引用。组件负责转盘转动与结果映射；实例化预制体、格子排布、播放 Spine 动画及发奖由接入方处理。已有格子放在 Reel 下即可随盘旋转。调整列表顺序会改变结果映射，请同步实际美术位置。无需奖励映射时，保持列表为空，继续按索引使用。

同样的配置也可以完全由代码提供：

```csharp
// firstView / secondView 是已经创建、排布好的任意表现根对象。
// firstPrizeAsset 是自定义业务资产；serverPrize 可以是任意 C# 对象。
wheel.ConfigureRewards(new[] {
    new WheelReward("金币格", view: firstView, data: firstPrizeAsset),
    new WheelReward("特殊奖励", view: secondView, userData: serverPrize)
});

wheel.Completed += index => {
    var reward = wheel.GetReward(index);
    ShowResult(reward);
};

wheel.TryStartSpin(1);

// 等待异步结果时：
// wheel.BeginSpin();
// wheel.StopAt(1);
```

`ConfigureRewards` 只能在空闲时调用；先完整校验再替换，无效配置不会破坏原列表。它复制配置项，但 View、Data、UserData 仍是原对象引用，不深拷贝。静态字段可保存在场景/Prefab 中，UserData 仅存在于运行时。配置不会自动保存回场景。

`GetReward(index)` 读取格子配置；同一索引用于 `TryStartSpin(index)` 和 `StopAt(index)`。`TargetReward` 是本轮已锁定的映射，`WinReward` 在完成后可读；取消不会发出完成事件。完成事件传入本轮索引；需要奖励配置时使用 `GetReward(index)`，应在发起下一轮或修改配置之前读取所需数据。仅按编号使用时奖励为 null。

格数由奖励列表决定，已有奖励时 `Initialize` / `Configure` 不接受不同格数，请用 `ConfigureRewards` 整体更新。`ClearRewards()` 清除映射并保留当前格数，不销毁表现对象。完成通知统一返回索引，奖励数据通过索引读取。

## 运行时配置

```csharp
// zeroAngle 表示 0 号扇区中心对准固定指针时 Reel 的本地 Z 角。
wheel.Configure(reel, 8, zeroAngle: 22.5f);
wheel.ConfigureMotion(duration: 3f, minimumTurns: 3,
                      direction: SpinDirection.Counter,
                      waitingSpeed: 720f, accelerationTime: 0.4f,
                      startDelay: 0, landingJitter: 0.2f,
                      useUnscaledTime: true);

// 减速时间单独配置；不传曲线，使用自然节奏。
wheel.ConfigureRhythm(decelerationTime: 1.8f);

// 可选的轻微预摆和回摆；角度或时长为 0 时关闭。
wheel.ConfigureStages(inDuration: 0.2f, inAngle: 3,
                      outDuration: 0.3f, outAngle: 2);
```

布局和运动配置只能在空闲时修改；旋转中调用配置接口会抛出 `InvalidOperationException`。`Initialize(count)` 只更新扇区数，不重置视觉角度。

| 配置 | 含义 |
| --- | --- |
| Sectors Count | 扇区数量，至少 1 格；默认等分，也可逐格配置宽度 |
| Zero Angle | 0 号中心对准指针时的绝对本地 Z 角，不依赖启动时的姿态 |
| Spin Direction | 动画旋转方向 |
| In Rotation Time / Angle | 可选的反向预摆时长和角度；时长或角度为 0 时跳过，默认关闭 |
| Minimum Cruise Time | 整轮最少匀速时间，API 中对应 `ConfigureMotion(duration)`；等待结果时已经经过的匀速时间计入 |
| Out Rotation Time / Angle | 可选的回摆时长和越过角度；时长或角度为 0 时直接停在目标，默认关闭 |
| Minimum Turns | 本轮至少正向转过的完整圈数，包含加速、等待和减速，预摆和回摆不计入 |
| Cruise Speed | 稳定转速，API 中对应 `waitingSpeed`；已知结果和等待结果共用，单位为度/秒 |
| Acceleration Time | 预摆完成后，从静止平滑加速到稳定转速的时长；0 表示立即到达稳定转速，此时不保证起步平滑 |
| Deceleration Time | 从稳定转速平滑减速到静止的时长，至少 0.05 秒；之后才执行可选回摆 |
| Landing Mode | 居中、固定格内位置或范围内随机；显式 offset 覆盖本次默认配置 |
| Use Unscaled Time | 忽略 Time.timeScale；指针使用相同的时间设置 |

### 自然运动与时长规则

两种结果流程共用 **预摆（可选）→ 加速 → 匀速 → 减速 → 回摆（可选）**。默认不做预摆和回摆，加速、减速的速度单调变化；阶段交界处速度及加速度平顺衔接，换向前先自然减速到零。

时长、稳定转速、完整圈数和准确落点无法同时任意固定。组件优先保持设定的稳定转速、加速/减速时长和落点，再延长匀速段满足整轮最少圈数与最少匀速时间。因此 `duration` 是**最少匀速时间，不是整轮总时长**。满足最少条件后，等待落点对齐所增加的匀速时间不超过一圈的匀速时间。

结果在延迟、预摆或加速期间返回时只锁定目标，仍完成起步与加速；结果在稳定转动期间返回时不跳变转速，也不再加速。等待阶段已经消耗的匀速时间和圈数会计入，不会收到结果后重新等待完整的最少时长。完成事件仅在减速及可选回摆结束后触发。

### 自定义节奏

Inspector 的自定义节奏位于各自的 **起转**、**停转** 分组，用时与对应节奏放在一起。每个动作单独勾选后才显示曲线。无需选择大量 Ease，也无需手动画曲线才能得到自然运动。

- 加速曲线表示“加速作用权重”，减速曲线表示“制动作用权重”。前段权重大，速度变化发生得早；后段权重大，速度变化发生得晚。
- 预摆、回摆曲线表示各自的“速度权重”，调整摆动距离在时间上的分布。
- 曲线横轴为阶段时间 0～1，纵轴为有限的非负权重，不是角度进度，不要求端点为 0/1。默认权重为常数 1。
- 组件将权重乘两端平滑归零的包络、归一化并积分，保持阶段端点的速度与加速度约束；自定义曲线不能让加速/减速途中反转或越界。
- 权重不能全为 0 或出现负数、NaN、Infinity。运行时接口拒绝非法曲线，Inspector 恢复为自然权重。曲线每轮开始按 128 段采样，极窄于采样间隔的细节不适合作为节奏配置。
- 权重只控制阶段内节奏，不能任意覆盖运动边界。原先直接控制角度进度的曲线不再按原含义解释；自定义节奏默认关闭，已有序列化曲线不会自动启用。

```csharp
// 同样的时长下，让加速更早发力、制动更晚发生。
wheel.ConfigureRhythm(1.8f,
    accelerationRhythm: AnimationCurve.Linear(0, 4, 1, 0.2f),
    decelerationRhythm: AnimationCurve.Linear(0, 0.2f, 1, 4));
// ConfigureStages 的 inCurve / outCurve 参数同样是非负速度权重；传入即启用。
```

## 等待服务器结果

```csharp
if (!wheel.BeginSpin()) {
    return;
}

try {
    var result = await RequestResultAsync();
    // 异步返回后仍需确认这是当前请求，并且转盘仍属于本轮。
    // 生命周期变化、取消、超时和请求编号由调用方管理。
    if (thisRequestIsCurrent && wheel && wheel.CanSubmitResult) {
        wheel.StopAt(result.sectorIndex);
    }
} catch {
    if (thisRequestIsCurrent && wheel) {
        wheel.CancelSpin();
    }
}
```

`BeginSpin()` 在配置的开始延迟后持续转动，直到 `StopAt` 或 `CancelSpin`。结果可以在开始延迟、预摆或加速内返回；这些阶段会继续执行，随后按已锁定结果规划匀速和减速。`StopAt` 只接受等待状态的第一次结果；等待状态也不允许用 `StartSpin` 替换结果。组件不包含网络超时和请求重试。

## 精确落点与状态

```csharp
wheel.TryStartSpin(2, offset: 0.25f); // 固定偏移，覆盖默认落点配置
wheel.StopAt(2, offset: 0);          // 等待模式，准确停到中心
float localZ = wheel.GetSectorAngle(2);
wheel.CancelSpin();                 // 停留在当前角度，下一轮从实际角度重新计算
wheel.SnapToSector(0);              // 空闲时立即对齐，不触发完成事件
```

`offset` 以一格宽度为单位，范围 `[-0.49, 0.49]`，正方向沿顺时针。不要停到 ±0.5 的扇区边界。

- `State`：`Idle` / `Starting`（预摆）/ `Accelerating`（加速）/ `Spinning`（匀速等待结果）/ `Stopping`（结果已锁定、匀速对齐）/ `Decelerating`（减速）/ `Settling`（回摆）。开始延迟包含在第一个启用阶段内。
- `CurrentAngularSpeed`：实际本地 Z 角速度，单位为度/秒；顺时针为负，延迟、取消和完成后为 0。
- `CanSubmitResult`：本轮是否仍能接受结果；预摆和加速期间尚未锁定结果时也为 true，异步接入与调试按钮应使用此属性。
- `IsSpinning`：等待或停止动画期间为 true，可用于禁用按钮。
- `TargetSectorIndex`：结果锁定后可读，尚未收到结果为 -1。
- `WinSectorIndex`：完成后可读，开始下一轮或取消当前轮后为 -1。
- `Completed(int)`：完成并校准角度、停止旋转音效后调用。回调内可启动下一轮；优先使用事件传入的索引，避免读取被下一轮重置的属性。
- `Cancelled`：只有活动轮次被取消时触发；取消不会触发完成回调。禁用/销毁组件会自动取消。

`TryStartSpin` / `BeginSpin` 忙碌、禁用或缺少 Reel 时返回 false；缺少 Reel 额外输出错误。`StopAt` 在 `CanSubmitResult` 为 false 时返回 false。非法索引、非有限数和越界配置抛参数异常。动画过程中不要从其他脚本改写 Reel 的旋转；要更换布局或运动参数，先取消。

## 调试与示例

示例运行时调试界面全部使用 UGUI：按钮、状态、结果、目标索引加减和扇区编号均为 Canvas 下的对象，可直接在 Hierarchy 中调整。包会安装 Unity UI（`com.unity.ugui`）；示例场景还使用 Input System（`com.unity.inputsystem`），请安装后将 Player Settings → Active Input Handling 设置为 Input System 或 Both。运行组件本身不依赖这些 UI/Input 包。

打开 `Samples/Demo/Scenes/Demo.unity`，进入 Play Mode。示例静态配置了六格映射，UGUI 调试标签显示编号和备注名称，便于核对目标与实际停留扇区；这些标签仅为 Demo 表现。运行中配置不等分布局时，Demo 会用 UGUI 网格生成对应的扇区底图并按真实中心放置标签；业务中的图片、Spine 等表现仍由接入方维护。顶部 UGUI 面板可通过加减按钮选择结果索引、直接旋转、模拟等待服务器、提交结果及取消。Inspector 同样提供这些操作，还支持固定落点偏移和调试随机目标；随机目标只存在于调试界面，不会自动决定业务结果。

指针的拨动角度、拨动时长、回弹时长和跟随方向独立配置。跨格检测覆盖整个动画；一帧跨多格只触发一次反馈，避免低帧率恢复时堆积音效。指针结束或取消后会归位。

范围：等分或不等分扇区、固定指针、单个转盘绕本地 Z 轴旋转。不预设奖品业务结构，不内置概率、发奖或网络协议，不支持多个指针同时判奖。

## 调参建议

先保持自定义节奏关闭：缩短加速时间得到更利落的起转，延长加速时间得到更轻柔的起转；减速时间分别控制停转的干脆与拖尾。需要特殊的同时间节奏差异时，再调整对应权重曲线。预摆和回摆只负责轻微的机械表现，不应替代加速和减速。

## 源码阅读路线

先读调用与状态变化，再看运动算法，不需要从曲线积分开始。

| 文件 | 职责与建议阅读顺序 |
| --- | --- |
| `WheelReward.cs` | 一格奖励的名称、美术对象和自定义数据；列表索引确定位置；只描述映射，不负责发奖 |
| `PointerController.cs` | `Flick → PlayFlick → Update`：记住原位、拨开、回位；`CancelFlick` 立即复位 |
| `WheelController.cs` | 先读类注释和 `WheelState`，再读 `TryStartSpin / BeginSpin / StopAt`，最后看 `Advance` |
| `WheelControllerEditor.cs` | 配置分组、奖励列表、对齐与试转；仅编辑器使用，不参与实际转动 |
| `PointerControllerEditor.cs` | 中文指针配置、自动/固定方向切换、运行模式试拨 |
| `Demo.cs` / `DemoSectorGraphic.cs` | 示例交互与底图绘制；动态创建底图和排布文字属于 Demo，不是运行时组件的能力 |

一轮流程是：开始前等待 → 反向回拉（可选）→ 加速 → 匀速 → 减速 → 回摆（可选）→ 完成。`Stopping` 表示已经有目标、正在补足匀速路程，真正减速的阶段叫 `Decelerating`。

角度计算先把“奖格编号＋格内位置”转换成最终停止角度；`PlanMainStop` 再按转向、最少圈数和时间规划到达路线。`Advance` 按帧推进，`CompleteSpin` 先收尾状态再发通知，所以完成回调里可以安全发起下一轮。`RhythmProfile` 仅负责节奏权重及积分；默认配置无需理解或修改它。

## 停不准或不能转时怎么查

| 现象 | 优先检查 |
| --- | --- |
| 点击没有转动 | 圆盘引用、组件是否启用、是否已有一轮正在转动，以及 Console 中的配置错误 |
| 返回 false | 是否正在转动、组件是否启用、圆盘是否绑定；等待流程还要检查是否已提交过结果 |
| 第 0 格正确，其他格错误 | 奖励列表是否按顺时针对应美术，不等分角度是否与实际一致 |
| 所有格都偏同样角度 | 用正确的奖格中心标记和指针位置重新对齐 |
| 指针跟着圆盘转 | 将指针移到旋转圆盘外 |
| 指针围绕奇怪的位置摆动 | 将轴心放在摆动支点，必要时用父物体作为支点、子物体放指针图片 |
| 一次结果处理了多遍 | 是否同时通过 Inspector 和代码订阅处理了同一份业务，或又在 callback 中重复处理 |
