# UI Marquee

基于 UGUI 的通用跑马灯（Marquee / Ticker）组件，**核心零第三方依赖**。单条内容支持**文本 / 图片 / Spine 的任意组合**（按片段水平排列），提供**逐条轮播**与**无缝连续滚动**两种模式，可配置滚动方向、停留时长、滚动速度、循环/单次播放、缓动曲线，并暴露开始/完成/点击等事件回调。内部使用 Unity 协程驱动；无缝模式采用**环形复用 + 对象池**，常驻对象数 ≈ 铺满视口所需，与条目总数无关。Spine 支持以**可选扩展程序集**提供，不引入时核心库仍保持零依赖。

## 依赖

| 依赖 | 版本 | 必需 |
| --- | --- | --- |
| `com.unity.ugui` | 2.0.0 | 是 |
| `com.esotericsoftware.spine.spine-unity` | 4.x | 否（仅使用 `SpineSegment` 时） |

> 核心库仅依赖 UGUI（含 TextMeshPro），无需 UniTask / PrimeTween 等第三方库，`git url` 安装即可用。Spine 为**可选扩展**，不使用 `SpineSegment` 时无需安装，详见「Spine 扩展」。

## 安装

- 将本包置于工程 `Packages/` 目录下，或
- 通过 Package Manager 以本地路径 / Git URL 引入，或
- 将包中的 Runtime 与 Editor 目录一并复制到工程 Assets 下，并保留程序集定义。

## 快速开始

> **只想先看效果？** 无需任何搭建——直接导入下方「[示例](#示例)」中的 `Demo`，打开 `UIMarqueeDemo.unity` 运行即可（完全自包含，UI 与数据由代码生成）。想在自己场景里快速起步，也可给任意对象挂上示例里的 `UIMarqueeDemo` 组件，用其右键菜单 **Create Demo Marquees In Scene** 一键生成可编辑的跑马灯结构。

**从零手动搭建**（4 步）：

1. 在 Canvas 下创建一个节点作为 **viewport**，建议挂载 `RectMask2D` 或 `Mask` 以裁剪溢出内容。
2. 在 viewport 下放置 **contentTemplate** 节点，其下需至少包含一个 `Image` 或一个 `TextMeshProUGUI`。它**仅作样式模板**（字体、颜色、Image 属性等）使用，运行时会按片段克隆复用，自身不直接显示。
3. 将 `UIMarquee` 组件挂到任意节点（默认会把自身 `RectTransform` 当作 viewport），并指定 `viewport` 与 `contentTemplate`。
4. 在 Inspector 中填充 `items`，或运行时通过 API 设置。每条 item 的内容由 `segments` 列表组成——单个片段即「纯文本 / 纯图片」，多个片段即**文本+图片+Spine 的任意混排**（见下文）。

## 滚动模式

| 模式 | 说明 |
| --- | --- |
| `Sequential`（逐条轮播） | 按列表顺序逐条展示。内容未超过视口且 `centerWhenFit` 开启时居中停留 `displayDurationWhenFit` 秒；超过视口时贴边停留 `displayDurationBeforeScroll` 秒后匀速滚出，再播放下一条。受 `playMode`（Loop/Once）与每条目的 `cycles` 控制。 |
| `Continuous`（无缝连续滚动） | 条目首尾相接匀速流动形成无缝跑马灯。忽略 `playMode`、`cycles`（除 `0` 表示禁用该条）、停留时长，仅使用 `scrollSpeed`、`spacing`、`direction`。采用**环形复用**：只创建“铺满视口 + 1 个待命”的单元，滚出流出边的单元绕回入场端并换下一条数据，**常驻对象数 ≈ 铺满视口所需，与条目总数无关**（20 条也可能只需几个）。单元经对象池复用，`Refresh()` / 重新 `Play()` 复用现有对象。 |

## 方向

`direction` 支持 `Left` / `Right` / `Up` / `Down`，对两种模式均生效。运行时修改方向/间距会在下一帧自动更新；`scrollSpeed` 对 Continuous 模式每帧读取，无需 `Refresh`。

## 缓动（Sequential 专用）

`ease` 控制逐条滚动的速度曲线，提供主流缓动全集（命名/公式遵循 [easings.net](https://easings.net)）：

- `Linear`
- `Sine` / `Quad` / `Cubic` / `Quart` / `Quint` / `Expo` / `Circ` 各 `In` / `Out` / `InOut`
- `Back` / `Elastic` / `Bounce` 各 `In` / `Out` / `InOut`（Back、Elastic 可超出 `[0,1]`，Bounce 在范围内回弹；位移使用 `LerpUnclamped`）
- `Custom`：使用 `customCurve`（`AnimationCurve`），横轴 `0→1` 为进度、纵轴 `0→1` 为位移比例，可自定义任意曲线（同样支持超出 `[0,1]` 的过冲）

> **仅 Sequential 模式生效**。Continuous（无缝连续滚动）为保证接缝处无跳变，始终保持匀速，忽略 `ease`。

## 条目配置（`MarqueeItemData`）

| 字段 | 说明 |
| --- | --- |
| `id` | 业务标识（可选），用于点击/事件回调中识别条目（如公告 ID、跳转链接）。 |
| `segments` | **内容片段列表**：文本 / 图片 / Spine 等任意组合，按顺序水平排列、整体居中、各段垂直居中（见「内容片段与混排」）。单段即「纯文本 / 纯图片」。 |
| `cycles` | 出现次数：`-1` 一直重复（默认），`0` 永不出现/禁用，`>0` 限定次数。运行时只消耗内部副本，**不会修改你传入的对象**。 |

> 内容只有 `segments` 一套模型，没有额外的 type/text/sprite 字段。最常用的单段场景可用便捷工厂：`MarqueeItemData.Text("文字", id, cycles)` / `MarqueeItemData.Image(sprite, id, cycles)`。

## 内容片段与混排（`segments`）

一条 item 由若干 **片段（`MarqueeSegment`）** 按顺序**水平排列**组成，相邻片段间距由 `segmentSpacing` 控制，整体水平居中、每段垂直居中。单个片段就是「纯文本 / 纯图片」，多个片段即混排。内置两种片段，Spine 片段由可选扩展提供：

| 片段类型 | 程序集 | 字段 |
| --- | --- | --- |
| `MarqueeTextSegment` | 核心 | `text` |
| `MarqueeImageSegment` | 核心 | `sprite`、`size`（为 0 时用 sprite 原始尺寸） |
| `SpineSegment` | `ZStudio.UniKit.UI.Marquee.Spine`（可选） | `skeletonDataAsset`、`skinName`、`animationName`、`loop`、`timeScale`、`scale`、`size` |

```csharp
using UnityEngine;
using ZStudio.UniKit.UI;

// 单段（便捷工厂）
marquee.AddItem(MarqueeItemData.Text("欢迎来到游戏！", "welcome"));
marquee.AddItem(MarqueeItemData.Image(iconSprite, "icon"));

// 混排：文本 + 图片 + Spine + 文本
var item = new MarqueeItemData(
    new MarqueeTextSegment("恭喜 "),
    new MarqueeImageSegment(avatarSprite) { Size = new Vector2(48, 48) },
    new SpineSegment {
        skeletonDataAsset = crownAsset,
        animationName = "idle",
        loop = true,
        size = new Vector2(64, 64),
    },
    new MarqueeTextSegment(" 荣获冠军！")
) { ID = "honor_1" };
marquee.AddItem(item);
```

- **Inspector 配置**：组件自带 `MarqueeItemData` 自定义抽屉——展开 `Items` 里的某条，在 **Segments** 列表点 `+` 会弹出类型下拉（`文本 / 图片 / Spine …`，Spine 仅在引入扩展后出现），选中即添加；列表项**可拖拽排序**，每条标题会显示内容摘要。单段即纯文本/纯图片，多段即混排。
- **对象池**：片段视图按渲染器 `Key` 分类入池复用，混排同样享受环形复用 / 对象池，不会因混排而额外创建常驻对象。
- **点击**：点击粒度为**整条 item**（回调返回该 item 与其 index），不区分点中的是哪个片段。`index` 在 **Sequential 与 Continuous 两种模式下均为该条目在 `items`（或 `SetItems` 传入列表）中的原始下标**，即使 Continuous 模式下跳过了 `cycles==0` / 零尺寸条目，回调下标仍对应原始列表，可直接用于反查。
- **扩展自定义片段**：实现 `IMarqueeSegmentRenderer` 并在启动时调用 `MarqueeSegmentRendererRegistry.Register(...)`（建议放在 `[RuntimeInitializeOnLoadMethod]` 中），即可接入任意自定义内容类型。

## Spine 扩展（可选）

Spine 支持位于独立程序集 `ZStudio.UniKit.UI.Marquee.Spine`（`Runtime/Spine`），**核心库不依赖 Spine**：

- **启用条件**：程序集通过 `defineConstraints: ["UI_MARQUEE_SPINE"]` 守卫，仅在该宏存在时参与编译。
  - 通过 **UPM 包**（`com.esotericsoftware.spine.spine-unity`）安装 Spine 时，`versionDefines` 会**自动定义** `UI_MARQUEE_SPINE`，开箱即用。
  - 若 Spine 是以 **`.unitypackage` 导入到 `Assets`**（非 UPM）安装的，请在 *Project Settings → Player → Scripting Define Symbols* 中**手动添加** `UI_MARQUEE_SPINE`。
- **未安装 Spine 的工程**：宏不存在 → 扩展程序集整体不参与编译，**不会产生任何编译错误**，核心库照常零依赖运行。
- **自动注册**：`SpineSegmentRenderer` 通过 `[RuntimeInitializeOnLoadMethod]` 在游戏启动时自动注册到 `MarqueeSegmentRendererRegistry`，业务侧无需手动接入；漏装扩展却使用了 `SpineSegment` 时，运行时会打印一次找不到渲染器的警告。

## 事件

```csharp
marquee.OnItemStart    += (item, index) => { /* 某条开始展示（Sequential） */ };
marquee.OnItemComplete += (item, index) => { /* 某条展示/滚动完成（Sequential） */ };
marquee.OnLoopComplete += () => { /* 完成一轮（Sequential + Loop） */ };
marquee.OnAllComplete  += () => { /* 全部播放结束（Once 或无可播放条目） */ };
marquee.OnItemClicked  += (item, index) => { /* 条目被点击；index 为原始 items 下标（两种模式一致） */ };
```

> 点击事件需要内容上的 `Graphic`（Image/Text）开启 `raycastTarget`，且场景中的 Canvas 含 `GraphicRaycaster`、场景含 `EventSystem`。若订阅了 `OnItemClicked` 却缺少 `GraphicRaycaster`，运行时会打印一次诊断警告。

## 运行时 API

```csharp
public void Play(int startIndex = 0);          // 开始播放（每次重置 cycles 预算）
public int  Stop();                            // 停止并返回当前索引
public void Pause();                           // 暂停（停留与滚动都会冻结）
public void Unpause();                         // 取消暂停
public void Refresh();                         // 请求重新布局，保留播放次数与一次性等待
public void SetItems(List<MarqueeItemData> items, bool startPlay = true);
public void AddItem(MarqueeItemData item);     // 追加（不打断当前播放）
public void AddItems(List<MarqueeItemData> items);
public void PlayOnce(string text, Action onComplete = null);          // 一次性播放单条文字
public void PlayOnce(MarqueeItemData item, Action onComplete = null); // 一次性播放单条内容

// async 版本（Unity 6 Awaitable，可直接 await；被打断/取消时抛 OperationCanceledException）
public Awaitable PlayOnceAsync(string text, CancellationToken ct = default);                // await 至单条文字播完
public Awaitable PlayOnceAsync(MarqueeItemData item, CancellationToken ct = default);       // await 至单条播完
public Awaitable PlaySequenceOnceAsync(int startIndex = 0, CancellationToken ct = default); // 以 Once 语义 await 至整个序列播完

public bool IsPlaying { get; }
public bool IsPaused  { get; }
public int  CurrentIndex { get; }
```

### 示例

```csharp
using ZStudio.UniKit.UI;

var items = new List<MarqueeItemData> {
    MarqueeItemData.Text("欢迎来到游戏！", "welcome"),
    MarqueeItemData.Text("限时活动进行中", "event_001", cycles: 3),
};

marquee.OnItemClicked += (item, _) => Debug.Log($"点击了公告：{item.ID}");
marquee.SetItems(items); // 默认立即开始播放
```

### 异步编排（Awaitable）

对**一次性 / 有明确终点**的播放，可用 `Awaitable` 版本把「等待完成」写成线性代码，替代事件订阅，串行编排更直观（Unity 6+）：

```csharp
using System.Threading;
using ZStudio.UniKit.UI;

// 依次播完 A、B，再执行后续逻辑——无需监听 onComplete 回调
async Awaitable ShowIntroAsync(CancellationToken ct) {
    try {
        await marquee.PlayOnceAsync(MarqueeItemData.Text("第一条公告"), ct);
        await marquee.PlayOnceAsync(MarqueeItemData.Text("第二条公告"), ct);
        // 以 Once 语义把整列表播完（忽略 playMode=Loop）后再继续
        await marquee.PlaySequenceOnceAsync(cancellationToken: ct);
        Debug.Log("全部播放完成");
    } catch (System.OperationCanceledException) {
        // 被新的播放 / Stop() / 组件禁用或销毁 / ct 取消打断时进入这里
    }
}
```

> **说明**：`await` 仅适合「一次」；**循环、`Continuous` 无缝滚动、逐条进度通知**仍应使用事件（`OnItemStart` / `OnItemComplete` / `OnLoopComplete` / `OnItemClicked`）——它们表达的是「每一次」。两套接口可共存。被 `Stop()`、新的播放、组件禁用/销毁或传入的 `CancellationToken` 取消时，await 会抛 `OperationCanceledException`，不会悬挂。

## 示例

在 **Package Manager** 中选中 **UIMarquee** → 展开 **Samples** → 点击 `Demo` 右侧的 **Import**，导入后打开场景 `UIMarqueeDemo.unity` 即可运行——**完全自包含**，UI 与数据均在运行时由代码构建，无需任何额外资源或字体。该场景是一个**综合功能演示控制台**，覆盖组件的全部核心能力：

- **两条跑马灯**：顶部一条 `Sequential`（逐条轮播）、底部一条 `Continuous`（无缝连续滚动），均含**文本 + 图片混排**条目。
- **Sequential 演示项**：缓动 `ease` 循环切换（Linear / QuadInOut / CubicOut / BackOut / ElasticOut / BounceOut）、`Loop` ⇄ `Once` 播放模式切换、`cycles` 限定次数（某条仅出现两次后被跳过）、短内容居中停留、`PlayOnce` 一次性播放（完成后自动恢复循环）。
- **Continuous 演示项**：`scrollSpeed` 滑条**即时调速**、`spacing` 滑条 + `Apply (Refresh)` 可显式请求重新布局（间距也会自动生效）、运行时 `AddItem` 追加并 `Refresh`。
- **全局控制**：四方向循环、暂停 / 恢复 / 停止 / 重播。
- **事件**：订阅 `OnItemStart` / `OnItemComplete` / `OnLoopComplete` / `OnAllComplete` / `OnItemClicked` 全事件。
- **界面**：全部用 **UGUI** 搭建（`Canvas` + `CanvasScaler` 按屏幕缩放，自适应不同分辨率）——左侧控制面板（按钮 / 滑条）提供上述全部运行时控制；事件回调输出到 **Console**。点击任意跑马灯内容可触发 `OnItemClicked`。
- **可选：编辑期预创建**：选中挂有 `UIMarqueeDemo` 的对象，在其组件右键菜单选择 **Create Demo Marquees In Scene**，即可在**非运行时**生成两条可视跑马灯（`Canvas` + `viewport` + `contentTemplate` + 预填 `items`），随后在 Inspector 中自定义配置；进入 Play 时示例会自动**复用**这两条跑马灯及其配置，而非重新构建。**Clear Demo Marquees In Scene** 可移除它们（均支持 Undo）。

## 设计说明

- **核心零依赖**：协程 + 自带插值，不依赖任何第三方库；Spine 等富内容以可选扩展程序集接入，不引入时核心库不受影响。
- **混排可扩展**：单条内容由片段列表组成，文本/图片内置、Spine 等通过 `IMarqueeSegmentRenderer` + 注册表接入，可自定义任意片段类型；混排同样走环形复用 / 对象池。
- **重入安全**：每次 `Play` 取消上一次播放（运行版本号 + `StopCoroutine`），不会出现多个循环并发；`PlayOnce` 被打断时不会回调 `onComplete`。
- **数据不被污染**：`cycles` 的剩余次数在内部副本中维护，运行时不会修改你传入的 `MarqueeItemData`。
- **暂停完整**：`Pause` 会同时冻结停留计时与滚动。
- **生命周期安全**：禁用 GameObject 时自动停止并记录状态，重新启用后恢复播放。
- **环形复用（Continuous）**：无缝模式只创建“铺满视口 + 1 个待命”的单元，滚出流出边的单元绕回入场端并换下一条数据；常驻对象数 ≈ 铺满视口所需，与条目总数无关，不会因条目多而创建成倍的隐藏对象。单元 `center` 在有限范围自循环，无浮点漂移；并经对象池复用以减少 `Refresh()` 时的 GC。
- **首帧兜底**：`viewport` 宽度尚未完成布局（为 0）时会等待若干帧，避免首条计算错误。
- **易于测试**：核心几何/索引逻辑抽离到 `MarqueeMath` 纯静态类，不依赖运行时状态，可直接编写 EditMode 单元测试验证。

## 更新与生命周期

- `Play()` 表示重新播放，会重置次数预算；`Refresh()` 只请求重新布局，不重置预算、不取消一次性等待。
- Sequential 的方向、视口尺寸、速度与间距变化会自动反映到后续帧；Continuous 的方向、间距变化及追加条目会触发自动重铺，重铺可能改变当前可见位置。
- 修改条目对象内部的片段内容后调用 `Refresh()`；直接修改 Inspector 的 `Items` 不会替换运行中的列表，请使用 `SetItems()`。
- `SetItems()` 停止旧播放并清空旧视图。空列表保持停止；非空列表由 `startPlay` 决定是否启动。
- 禁用后重新启用会从当前条重新展示，但保留剩余次数；一次性播放被取消，不自动恢复。
- 取消令牌可以从后台线程触发；停止协程与完成取消等待统一在后续主线程更新中处理。
- 渲染器 Key 必须唯一，不能使用 `builtin.` 前缀。注册表在每次进入播放模式时清空，扩展应在 `BeforeSceneLoad` 阶段重新注册。
- 组件销毁时回收自己的视图并恢复模板激活状态，不修改原始 TMP 模板的换行/溢出设置。
- 尺寸、间距与速度使用 UI 本地单位，受 Canvas 缩放影响，并非屏幕物理像素。
