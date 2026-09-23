# UI Marquee

基于 UGUI 的跑马灯组件，适用于公告、消息轮播和动态信息展示。支持文本与图片混排、逐条轮播、无缝连续滚动，以及可选的 Spine 动画片段。

核心包依赖 Unity 6 和 UGUI（含 TextMeshPro），不需要 UniTask 或其他动画库。使用 Spine 或导入 Demo 时，需另外安装对应依赖。

## 安装

在 Unity 的 Package Manager 中选择 **Add package from git URL**，输入：

```text
https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.ui.marquee
```

也可以通过 **Add package from disk** 选择本目录中的 `package.json`。需要固定版本时，在 Git URL 后追加仓库中实际存在的标签或提交号。

| 依赖 | 用途 |
| --- | --- |
| Unity 6000.0 或更新版本 | 核心组件及原生 `Awaitable` |
| `com.unity.ugui` 2.0.0 | UGUI 与 TextMeshPro，由本包声明依赖 |
| Spine Unity / Spine C# | 可选，仅用于 Spine 片段；当前扩展针对 Spine 4.2 API |
| `com.unity.inputsystem` | 仅 Demo 需要，核心组件不依赖它 |

## 先体验 Demo

1. 安装 Input System，并在项目中启用新输入系统或 Both。
2. 在 Package Manager 中选中 **UI Marquee**，导入 **Samples > Demo**。
3. 打开导入目录中的 `Scenes/Demo.unity`，进入播放模式。

示例源码位于 [`Samples~/Demo`](Samples~/Demo)，用于在编辑器中体验两种滚动模式、四个方向、调速、暂停与恢复、动态追加和点击事件。界面及演示内容由 `UIMarqueeDemo` 构建。

该组件还提供两个右键菜单：

- **Create Demo Marquees In Scene**：预先生成可编辑的跑马灯节点，方便在 Inspector 中调整配置。
- **Clear Demo Marquees In Scene**：清理生成的节点。

文本显示需要有效的 TMP 字体资源。使用中文内容时，请为模板指定包含对应字符的字体。

## 在自己的场景中使用

推荐层级：

```text
Canvas
└─ MarqueeViewport       RectTransform + RectMask2D + Marquee
   └─ ContentTemplate    样式模板
      ├─ Text            TextMeshProUGUI
      └─ Image           Image
```

1. 设置可视区域大小，并添加 `RectMask2D` 或 `Mask` 裁剪溢出内容。
2. 在 `Marquee` 上指定 `Viewport` 和 `ContentTemplate`。`Viewport` 留空时使用组件自身的 `RectTransform`；模板留空时尝试使用视口的第一个子节点。
3. 根据内容保留文本或图片模板，设置字体、颜色等样式。使用哪种内置片段，就需要对应模板；Spine 由自己的渲染器创建视图。
4. 在 Inspector 的条目列表中添加内容，展开条目后通过“内容片段”的 `+` 添加片段，拖动手柄调整顺序。
5. 开启 `PlayOnStart` 自动播放，或通过代码设置数据并启动。

模板在运行时会隐藏，组件克隆并复用其片段视图；销毁组件时恢复模板原本的激活状态。模板应独立于组件所在节点，避免隐藏模板时同时禁用组件。

## 模式与配置

| 行为 | `Sequential` 逐条轮播 | `Continuous` 连续滚动 |
| --- | --- | --- |
| 展示方式 | 一次展示一条，结束后切换下一条 | 多条首尾相接、循环流动 |
| `PlayMode` | `Loop` 循环遍历，`Once` 遍历一次 | 不使用 |
| `Cycles` | 消耗每条内容的展示次数 | 只用 `0` 排除条目，其余都参与循环 |
| 停留与居中 | 支持 | 不使用 |
| 缓动 | 使用 `Ease` 或 `CustomCurve` | 始终匀速 |
| 间距 | 使用片段间距 | 使用片段间距和条目间距 |

两种模式均支持 `Left`、`Right`、`Up`、`Down`。无论整体朝哪个方向滚动，一条内容内部的片段始终从左到右排列、垂直居中。

| 字段 | 说明 |
| --- | --- |
| `ScrollSpeed` | 滚动速度；逐条模式选择非线性缓动时，以此计算滚动时长 |
| `SegmentSpacing` | 同一条内容内部的片段间距 |
| `Spacing` | 连续模式的条目间距 |
| `CenterWhenFit` | 逐条模式下，内容能放入视口时居中展示 |
| `DisplayDurationWhenFit` | 居中展示的停留时长 |
| `DisplayDurationBeforeScroll` | 需要滚动时，开始移动前的停留时长 |
| `EdgeMargin` | 逐条滚动起点的边缘留白 |
| `Ease` | 逐条滚动的缓动类型；`Custom` 使用 `CustomCurve` |
| `CustomCurve` | 横轴为 0～1 的进度，纵轴为位移比例，可实现过冲 |
| `PlayOnStart` | 在 `Start` 时自动播放已有条目 |
| `IgnoreTimeScale` | 使用非缩放时间；Spine 及实现时间控制接口的扩展也会跟随 |

关闭 `CenterWhenFit` 后，短内容也会滚动。尺寸、间距和速度使用 UI 本地单位，受 Canvas 缩放影响；停留时长以秒计。

## 条目与混排

`MarqueeItemData` 表示一条内容：

| 字段 | 说明 |
| --- | --- |
| `ID` | 可选的业务标识，用于事件中识别条目 |
| `Segments` | 按显示顺序排列的片段列表 |
| `Cycles` | `-1` 不限次数，`0` 跳过，正数限制展示次数 |

`Cycles` 的剩余次数由组件单独维护，不会写回条目。`PlayMode.Once` 只遍历一次，不会为了用完正数次数再次循环；`Play()` 会重新初始化次数。

内置 `MarqueeTextSegment` 使用 `Text` 字段；`MarqueeImageSegment` 使用 `Sprite` 和 `Size`。图片尺寸逐轴补齐：例如 `Size = (100, 0)` 保留指定宽度，高度取 Sprite 的原始高度。

以下脚本可挂在已配置好模板的跑马灯节点上：

```csharp
using System.Collections.Generic;
using UnityEngine;
using ZStudio.UniKit.UI;

[RequireComponent(typeof(Marquee))]
public class NoticeExample : MonoBehaviour {
    [SerializeField] private Sprite m_Icon;

    private void Start() {
        var marquee = GetComponent<Marquee>();
        marquee.SetItems(new List<MarqueeItemData> {
            MarqueeItemData.Text("欢迎来到游戏！", "welcome"),
            new MarqueeItemData(
                new MarqueeImageSegment(m_Icon) { Size = new Vector2(32f, 32f) },
                new MarqueeTextSegment("限时活动进行中")
            ) { ID = "event", Cycles = 3 }
        });
    }
}
```

纯图片可用 `MarqueeItemData.Image(sprite, id, cycles)` 创建。`SetItems` 默认立即开始播放；只准备数据时传入 `startPlay: false`，稍后调用 `Play()`。

## 播放控制

| API | 行为 |
| --- | --- |
| `Play(startIndex = 0)` | 重新开始并重置次数；起始索引仅对逐条模式生效 |
| `Stop()` | 停止调度，保留当前画面并返回当前索引 |
| `Pause()` / `Unpause()` | 暂停、恢复停留计时与滚动，片段动画继续播放 |
| `SetItems(newItems, startPlay = true)` | 停止旧播放、清空旧视图并替换列表；空列表保持停止 |
| `AddItem(item)` / `AddItems(items)` | 追加内容，不取消当前播放；连续模式会重新布局 |
| `Refresh()` | 请求重建布局，不重置剩余次数，也不取消一次性等待 |
| `PlayOnce(text 或 item, onComplete)` | 打断当前播放，展示单条内容；正常结束时调用回调 |
| `PlayOnceAsync(text 或 item, cancellationToken)` | 播放单条内容并等待结束 |
| `PlaySequenceOnceAsync(startIndex = 0, cancellationToken = default)` | 在逐条模式下遍历列表一次，忽略 `PlayMode.Loop` |

`IsPlaying`、`IsPaused` 可用于查询状态。`CurrentIndex` 用于读取逐条序列的当前索引，不用于查询连续模式或 `PlayOnce` 的当前内容。

单条 `PlayOnce` 不消耗条目的 `Cycles`，也不会自动恢复此前的序列。需要恢复时，在完成回调中主动调用 `Play()`。新播放请求会打断旧播放，被打断的 `PlayOnce` 不调用完成回调。

### 异步等待

下面的方法接收已经配置好的组件，依次播放两条消息：

```csharp
using System;
using System.Threading;
using UnityEngine;
using ZStudio.UniKit.UI;

public static class NoticeSequence {
    public static async Awaitable ShowAsync(Marquee marquee, CancellationToken token) {
        try {
            await marquee.PlayOnceAsync("第一条公告", token);
            await marquee.PlayOnceAsync("第二条公告", token);
        } catch (OperationCanceledException) {
            // 新播放、Stop、禁用、销毁或取消令牌会打断等待。
        }
    }
}
```

`PlaySequenceOnceAsync` 仅用于 `Sequential`；在 `Continuous` 下调用会记录警告并立即返回已完成的等待，不会等待连续滚动结束。渲染器在播放绑定过程中抛出的异常会传递给所属异步等待。

### 运行时修改与生命周期

- 调整速度无需 `Refresh()`。方向、间距等配置会在播放循环中更新；连续模式重新布局时可见位置可能变化。
- 修改条目对象内部的文本、图片或片段后，调用 `Refresh()`。它会重新绑定当前内容，片段动画也可能重新开始。
- `SetItems` 复制列表容器，但不深拷贝条目。直接修改 Inspector 的 `Items` 列表结构不会替换运行列表，应使用 `SetItems`。
- 普通播放在禁用后记录恢复点，重新启用时保留剩余次数并重新开始展示；不保证从原滚动位置继续。一次性播放被取消后不自动恢复。
- `Stop` 和仅禁用 `Marquee` 组件都不会冻结仍可见的片段动画；禁用整个 GameObject 时动画随层级停止。
- 播放和配置 API 应在 Unity 主线程调用。取消令牌可以从后台线程触发，组件会在主线程更新中处理。

## 事件与点击

| 事件 | 触发时机 |
| --- | --- |
| `OnItemStart(item, index)` | 逐条序列开始展示某条内容 |
| `OnItemComplete(item, index)` | 逐条序列完成某条内容 |
| `OnLoopComplete()` | 逐条循环从列表尾部转回前部 |
| `OnAllComplete()` | 逐条序列自然结束，或连续模式没有有效内容 |
| `OnItemClicked(item, index)` | 内容中的可点击 Graphic 收到点击 |

单条 `PlayOnce` 不发送序列的开始、完成事件，应使用其完成回调或异步等待。连续模式不发送逐条进度事件。

点击粒度是整条内容，不区分片段。序列播放的 `index` 对应运行列表的原始下标，即使连续模式跳过了禁用条目也保持一致；单条 `PlayOnce` 的点击索引为 `0`。

点击需要 Canvas 上的 `GraphicRaycaster`、场景中的 `EventSystem` 及合适的输入模块，并开启片段 Graphic 的 `raycastTarget`。业务侧应在适当生命周期中取消事件订阅。

## Spine 扩展

Spine 支持位于独立程序集 `ZStudio.UniKit.UI.Marquee.Spine`。通过 UPM 安装 `com.esotericsoftware.spine.spine-unity` 后，程序集会自动启用，并在启动时注册渲染器。

通过 `.unitypackage` 安装时，需要手动添加 `UI_MARQUEE_SPINE` 编译宏，并确保工程提供 `spine-unity`、`spine-csharp` 两个程序集。业务脚本使用 `SpineSegment` 时也需引用本包的 Spine 扩展程序集。

| 字段 | 说明 |
| --- | --- |
| `SkeletonDataAsset` | 骨骼数据资产 |
| `SkinName` | 皮肤名，留空使用默认皮肤 |
| `AnimationName` | 动画名，留空保持初始姿势 |
| `PlayWhenFullyVisible` | 默认开启，布局矩形进入视口后开始动画；某轴大于视口时，该轴以重叠作为条件 |
| `Loop` / `TimeScale` | 动画循环与速度倍率 |
| `Scale` | 骨骼整体缩放，同时影响布局占位 |
| `Size` | 缩放前的布局尺寸，非正分量取初始姿势（setup pose）的包围盒尺寸 |

最终占位为 `Size × abs(Scale)`。渲染器通过独立子节点补偿骨骼原点偏移；播放中超出初始包围盒的动画不会自动触发布局变化，可通过 `Size` 预留空间。

## 自定义片段

1. 创建带 `[Serializable]` 的 `MarqueeSegment` 子类。要在 Inspector 的添加菜单中出现，还需具有公共无参构造函数。
2. 实现 `IMarqueeSegmentRenderer`，负责创建、绑定和回收视图。
3. 在 `RuntimeInitializeLoadType.BeforeSceneLoad` 阶段调用 `MarqueeSegmentRendererRegistry.Register` 注册渲染器。

所有渲染器使用同一个绑定入口：

```csharp
Vector2 Bind(RectTransform view, MarqueeSegment segment, MarqueeRenderContext context);
```

`context` 提供 `Viewport`、`IsMeasuring`、`IgnoreTimeScale`。`Bind` 必须覆盖旧内容，并返回包含缩放后的最终布局尺寸。测量和实际显示应返回相同尺寸；`IsMeasuring` 为 `true` 时不得启动动画或触发业务事件。

以下能力按需实现，无需为静态片段添加动画控制逻辑：

| 可选接口 | 用途 |
| --- | --- |
| `IMarqueeSegmentMeasurer` | 直接测量数据，省去隐藏视图绑定 |
| `IMarqueeSegmentTimeControl` | 在 `IgnoreTimeScale` 改变时同步片段的时间模式 |

未提供独立测量接口时，组件会在隐藏视图上调用 `Bind`。视图由组件管理和复用，渲染器不要自行销毁正常绑定的视图。`OnRecycle` 应清理自有动画、订阅和业务引用；`CreateView` 失败时应自行清理尚未返回的资源。

注册键 `Key` 必须稳定且唯一，不得使用保留前缀 `builtin.`。注册表在每次进入播放模式时清空，扩展需要重新注册；同一个渲染器可能服务多个 Marquee 实例，应将每个视图的状态保存在视图自身。

## 代码导览

| 文件 | 职责 |
| --- | --- |
| [`Marquee.cs`](Runtime/Marquee.cs) | 配置入口、条目选择与模式调度 |
| [`MarqueePlayback.cs`](Runtime/MarqueePlayback.cs) | 播放会话、取消、完成与异常处理 |
| [`MarqueeContentLayout.cs`](Runtime/MarqueeContentLayout.cs) | 内容测量、水平布局和视图池 |
| [`MarqueeRingLayout.cs`](Runtime/MarqueeRingLayout.cs) | 独立于视图的连续滚动进度与几何布局 |
| [`MarqueeMath.cs`](Runtime/MarqueeMath.cs) | 方向、索引和缓动计算 |
| [`SpineSegmentRenderer.cs`](Runtime/Spine/SpineSegmentRenderer.cs) | Spine 绑定、占位与播放控制 |
