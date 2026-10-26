# Lucky Wheel

Unity 6000.0+ 的等分转盘组件。支持 UI / 2D 对象绕本地 Z 轴旋转，组件负责表现，由业务方指定结果。无第三方 Tween 依赖。

## 五分钟接入

1. 创建独立的转盘根对象，添加 `WheelController`，绑定旋转对象 `Reel`。避免将控制器本身放在需要单独隐藏的装饰对象上。
2. 在 `Sector Layout` 设置扇区数和美术的编号方向，索引从 **0** 开始；编号方向与动画旋转方向分别设置。
3. 把美术的 **0 号扇区中心转到指针下方**，点击 Inspector 的 `Use Current Z As Sector 0`。再选择其他索引，点击 `Align Selected Sector` 检查编号是否吻合。
4. 根据需要绑定 `PointerController` 和音效。转盘与指针组件会自动要求 AudioSource；没有指针和音效也可以运行。
5. 调用 `StartSpin(index)`。在 `Result Event(int)` 或 C# `Completed` 事件中处理本次表现完成。真实奖品和发奖逻辑由业务层处理。

```csharp
using GF.Unity.Feature.Wheel;

// Inspector 配置完整时，不需要 Initialize。
wheel.Completed += index => ShowPrize(index);
wheel.StartSpin(2); // 停到 2 号扇区

// 需要检查是否忙碌或不可用时：
bool accepted = wheel.TryStartSpin(2);
```

不要同时使用 Result Event、Complete Event、Completed 和每次调用的 callback 重复发奖；它们是同一次完成的不同通知入口。

## 运行时配置

```csharp
// zeroAngle 表示 0 号扇区中心对准固定指针时 Reel 的本地 Z 角。
wheel.Configure(reel, 8, zeroAngle: 22.5f,
                sectorDirection: ESpinDirection.ClockWise);
wheel.ConfigureMotion(duration: 3f, minimumTurns: 3,
                      direction: ESpinDirection.Counter,
                      waitingSpeed: 720f, accelerationTime: 0.4f,
                      startDelay: 0, landingJitter: 0.2f,
                      useUnscaledTime: true);

// 反向预摆 → 主旋转（越过目标）→ 反向回摆归位。
wheel.ConfigureStages(inDuration: 0.3f, inAngle: 8,
                      outDuration: 0.5f, outAngle: 12,
                      inCurve: AnimationCurve.EaseInOut(0, 0, 1, 1),
                      outCurve: AnimationCurve.EaseInOut(0, 0, 1, 1));
```

布局和运动配置只能在空闲时修改；旋转中调用配置接口会抛出 `InvalidOperationException`。`Initialize(count)` 只更新扇区数，不重置视觉角度。

| 配置 | 含义 |
| --- | --- |
| Sectors Count | 等分扇区数量，至少 1 格 |
| Zero Angle | 0 号中心对准指针时的绝对本地 Z 角，不依赖启动时的姿态 |
| Sector Direction | 美术编号递增方向 |
| Spin Direction | 动画旋转方向 |
| In Rotation Time / Angle / Curve | 起转反向预摆的时长、角度、进度曲线；时长 0 跳过 |
| Main Rotation Time | 主旋转阶段的时长，不包含开始延迟、起转预摆、停转回摆 |
| Out Rotation Time / Angle / Curve | 主旋转越过目标后反向回摆的时长、越过角度、进度曲线；时长 0 时不越过目标，直接完成 |
| Minimum Turns | 停止阶段至少增加的完整圈数，可以为 0 |
| Waiting Speed | 等待结果时的最高速度，单位为度/秒 |
| Acceleration Time | 等待阶段从静止加速到最高速度的时长；0 表示立即达到最高速度 |
| Landing Jitter | 在目标格内随机偏移的最大范围，以一格宽度为单位，范围 0～0.49；0 为格中心 |
| Use Unscaled Time | 忽略 Time.timeScale；指针使用相同的时间设置 |

起转与停转是独立的表现阶段：起转沿主旋转的反方向预摆；主旋转沿设定方向转到目标前方的越过位置；停转再反向回摆到实际目标。曲线从 `(0,0)` 到 `(1,1)`，采样进度限制在 0～1，运行时传入的曲线会复制，避免外部修改影响当前配置。时长为 0 可单独关闭某一阶段。

主旋转使用平滑 Hermite 插值，起始速度承接等待阶段，终点速度为 0。高速等待、短主旋转时长时，组件可能增加圈数，保证主旋转自身不反转。停转回摆是显式配置的反向运动，完成回调只在回摆归位后触发。

直接指定结果的总时长为 **开始延迟 + 起转时长 + 主旋转时长 + 停转时长**。等待模式只在起转时执行一次预摆；收到结果后，主旋转和停转回摆依次完成。等待加速的 `Acceleration Time` 与反向预摆的 `In Rotation Time` 分别控制两个不同动作。

## 等待服务器结果

```csharp
if (!wheel.BeginSpin()) return;
try {
    var result = await RequestResultAsync();
    // 异步返回后仍需确认这是当前请求，并且转盘仍属于本轮。
    // 生命周期变化、取消、超时和请求编号由调用方管理。
    if (thisRequestIsCurrent && wheel && wheel.CanSubmitResult)
        wheel.StopAt(result.sectorIndex);
} catch {
    if (thisRequestIsCurrent && wheel) wheel.CancelSpin();
}
```

`BeginSpin()` 在配置的开始延迟后持续转动，直到 `StopAt` 或 `CancelSpin`。结果可以在开始延迟或起转预摆内返回；延迟和预摆会继续执行，随后进入主旋转。`StopAt` 只接受等待状态的第一次结果；等待状态也不允许用 `StartSpin` 替换结果。组件不包含网络超时和请求重试。

## 精确落点与状态

```csharp
wheel.TryStartSpin(2, offset: 0.25f); // 固定偏移，忽略 Landing Jitter
wheel.StopAt(2, offset: 0);          // 等待模式，准确停到中心
float localZ = wheel.GetSectorAngle(2);
wheel.CancelSpin();                 // 停留在当前角度，下一轮从实际角度重新计算
wheel.SnapToSector(0);              // 空闲时立即对齐，不触发完成事件
```

`offset` 以一格宽度为单位，范围 `[-0.49, 0.49]`，正方向沿美术编号方向。不要停到 ±0.5 的扇区边界。

- `State`：`Idle` / `Starting`（起转预摆，含开始延迟）/ `Spinning`（等待结果）/ `Stopping`（已锁定结果的主旋转）/ `Settling`（停转回摆）。关闭起转时，延迟包含在后续阶段内。
- `CanSubmitResult`：本轮是否仍能接受结果；起转期间尚未锁定结果时也为 true，异步接入与调试按钮应使用此属性。
- `IsSpinning`：等待或停止动画期间为 true，可用于禁用按钮。
- `TargetSectorIndex`：结果锁定后可读，尚未收到结果为 -1。
- `WinSectorIndex`：完成后可读，开始下一轮或取消当前轮后为 -1。
- `Completed(int)`：完成并校准角度、停止旋转音效后调用。回调内可启动下一轮；优先使用事件传入的索引，避免读取被下一轮重置的属性。
- `Cancelled`：只有活动轮次被取消时触发；取消不会触发完成回调。禁用/销毁组件会自动取消。

`TryStartSpin` / `BeginSpin` 忙碌、禁用或缺少 Reel 时返回 false；缺少 Reel 额外输出错误。`StopAt` 在 `CanSubmitResult` 为 false 时返回 false。非法索引、非有限数和越界配置抛参数异常。动画过程中不要从其他脚本改写 Reel 的旋转；要更换布局或运动参数，先取消。

## 调试与示例

示例运行时调试界面全部使用 UGUI：按钮、状态、结果、目标索引加减和扇区编号均为 Canvas 下的对象，可直接在 Hierarchy 中调整。包会安装 Unity UI（`com.unity.ugui`）；示例场景还使用 Input System（`com.unity.inputsystem`），请安装后将 Player Settings → Active Input Handling 设置为 Input System 或 Both。运行组件本身不依赖这些 UI/Input 包。

打开 `Examples/Scenes/Demo.unity`，进入 Play Mode。转盘上显示从 0 开始的编号，便于核对目标与实际停留扇区。顶部 UGUI 面板可通过加减按钮选择结果索引、直接旋转、模拟等待服务器、提交结果及取消。Inspector 同样提供这些操作，还支持固定落点偏移和调试随机目标；随机目标只存在于调试界面，不会自动决定业务结果。

指针的拨动角度、拨动时长、回弹时长和跟随方向独立配置。跨格检测覆盖整个动画；一帧跨多格只触发一次反馈，避免低帧率恢复时堆积音效。指针结束或取消后会归位。

范围：等分扇区、固定指针、单个转盘绕本地 Z 轴旋转。不内置奖品数据、概率、发奖、网络协议，不支持不等宽扇区或多个指针同时判奖。

## 验证

`Tests/Editor` 包含布局、双向旋转、连续目标、实际角度恢复、等待结果、偏移、延迟、取消、生命周期与回调重入测试。在 Unity Test Runner 的 EditMode 中筛选 `ZStudio.UniKit.LuckyWheel.Tests` 运行。
