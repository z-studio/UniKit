# UI Safe Area

面向 Unity uGUI 的安全区组件包，并额外提供子元素出血（Outset/Bleed）能力。

## 功能概览

| 组件 | 作用 |
|---|---|
| `SafeArea` | 容器级：读取 `Screen.safeArea`，驱动 `RectTransform` 缩进安全区 |
| `SafeAreaOutset` | 子元素级：在容器内对个别 UI 做外扩 / 出血到 Player 窗口边 |

### SafeArea

- 参考朝向（Reference Orientation）+ 四边 Flags（Top/Right/Bottom/Left）
- 居中对齐（Center Horizontally / Vertically）
- 逐边 Padding（安全区之上再留白）
- `EffectiveSafeArea` 只读属性
- `SafeAreaChanged` 事件（C# event + Inspector UnityEvent）
- `DrivenRectTransformTracker` 接管锚点，Layout 冲突 Editor 警告

### SafeAreaOutset

- 固定 Outset（逐边像素，参考朝向）
- Bleed Edges（从父级 `EffectiveSafeArea` 出血到 Player 窗口边）
- Apply Mode：
  - **Expand**（默认）：逐边往外扩，语义与 Bleed 一致，适合 Stretch 横条、出血条、多数 Outset 场景
  - **Translate**：保持尺寸整体平移，适合固定大小的角标 / 图标（需手动切换）
- 自动保存和恢复元素原始布局，挂载后只需配置参数
- 订阅父级 `SafeAreaChanged` 自动刷新

## 安装

1. 从 `Window > Package Manager` 打开 `Package Manager`
2. 选择 `+ > Add package from git URL...`
3. 输入以下 Git URL 进行安装
    * https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.ui.safearea

<p align="center">
  <img width="80%" src="https://user-images.githubusercontent.com/47441314/118421190-97842b00-b6fb-11eb-9f94-4dc94e82367a.png" alt="Package Manager">
</p>

或者，打开 `Packages/manifest.json` 文件，并将以下内容添加到 dependencies 块中：

```json
{
    "dependencies": {
        "com.z-studio.unikit.ui.safearea": "https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.ui.safearea"
    }
}
```

如需锁定版本，可在 URL 后添加仓库中实际存在的标签或提交号。以下标签仅为格式示例，安装前需确认目标仓库和版本已发布：

* https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.ui.safearea#v1.0.0

## 推荐层级

```
Canvas
 ├─ Background            ← 可选，满屏出血背景放外面
 └─ SafeAreaRoot          ← SafeArea（Edges 按需勾选，Padding 可选）
     ├─ TopBar / HUD / Buttons
     └─ CornerBadge       ← SafeAreaOutset（Translate + Bleed Top/Right）
```

**注意：** 不要将 `SafeArea` 挂在 Root Canvas 上，应挂在 Canvas 的子节点。Editor 会提示此问题。其直接父级必须覆盖整个 Player 窗口；组件直接将屏幕归一化坐标作为父级内的锚点，不会转换任意父容器的坐标。不要嵌套多个 `SafeArea`，否则可能重复内缩。

## 快速上手

### 1. 容器适配安全区

1. 在全屏 Canvas 下创建带 RectTransform 的 UI 对象 `SafeAreaRoot`，保持旋转为零、局部缩放为一
2. Add Component → **UI (Canvas) > ZStudio > Safe Area**
3. 设置 **Reference Orientation**（通常与游戏默认竖屏一致）
4. 勾选 **Edges** 需要避让的边（新组件默认四边全部开启；已有组件保留序列化设置）
5. 可选设置 **Padding** 增加内边距
6. 将需要保证可见的 UI 放到 `SafeAreaRoot` 下

### 2. 角标出血到刘海区

1. 在 `SafeAreaRoot` 下创建角标，锚点设为 **Top Right**，先设置好未外扩时的位置和尺寸
2. Add Component → **UI (Canvas) > ZStudio > Safe Area Outset**（默认 **Expand**）
3. 设置 **Reference Orientation**（它独立于父组件，不会自动继承）；若是**固定大小**角标，将 **Apply Mode** 改为 **Translate**
4. **Bleed Edges** = Top + Right（不要选 Everything）
5. 调整 Outset 或 Bleed Edges 即可预览效果，无需额外确认或记录操作

## 处理顺序

```
SafeArea（父容器）
  Screen.safeArea → Padding → Edges → Alignment → EffectiveSafeArea

SafeAreaOutset（子元素，独立）
  元素原始布局 → Outset + Bleed（相对 EffectiveSafeArea）→ Expand / Translate
```

## 参数默认值与单位

| 组件 | 参数 | 新组件默认值 |
|---|---|---|
| SafeArea | Reference Orientation | Portrait |
| SafeArea | Edges | Top、Right、Bottom、Left 全部开启 |
| SafeArea | Alignment | 不居中（0） |
| SafeArea | Padding | 四边均为 0 |
| SafeAreaOutset | Reference Orientation | Portrait，独立设置 |
| SafeAreaOutset | Apply Mode | Expand |
| SafeAreaOutset | Outset / Bleed Edges | 四边外扩量为 0 / 不延伸（0） |

- Padding、Outset、EffectiveSafeArea 均使用 **Player 窗口像素**，不是 Canvas Scaler 的参考分辨率单位。Outset 应用到 RectTransform 时除以 Canvas 的 scaleFactor。
- 参考方向只决定边缘和对齐轴如何映射，不会修改设备方向或 Player Settings。当前 API 只接受四个明确方向；将 ReferenceOrientation 设为 AutoRotation 等值会抛出异常。
- 本文的“窗口边缘”是 `Screen.width / Screen.height` 定义的边界。Unity 的 `Screen.safeArea` 相对于 Player 窗口，不能一概视为物理设备屏幕。Android 禁用 Render outside safe area 时，Player 窗口可能已被限制在安全区内，此时组件无法让 UI 延伸到窗口之外。参见 [Unity Screen.safeArea 文档](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen-safeArea.html)。

## Padding、Edges 与 Alignment 的关系

Padding 先叠加在系统安全区上，再由 Edges 决定保留哪些边的内缩量。因此，关闭某一边也会清除该边的 Padding。

Alignment 最后执行，取同一轴两侧较大的内缩量并应用到两侧。因此，即使某一边未勾选 Edges，居中也可能再次使该边内缩。

例如，上方安全区内缩 60 像素、Top Padding 为 20 时，上方共内缩 80 像素。若只开启 Top 且未居中，下方内缩为 0；开启垂直居中后，上下方均内缩 80 像素。

当前实现不会限制过大的 Padding；当两侧内缩量之和超过可用尺寸时，可能产生反向矩形。使用时需自行保证有效区域宽高为正。

## Outset 与 Bleed 的准确含义

Bleed 根据父级 SafeArea 的 EffectiveSafeArea 到窗口各边的距离增加外扩量，其中包含父级 Padding 和 Alignment 的结果。如果未找到父级，或父级有效区域宽高不为正，则退回 Screen.safeArea。

它不测量子元素自身到窗口边缘的距离。因此，只有子元素的对应边原本与参考区域的边对齐时，才能恰好延伸到窗口边缘；基准布局已有的间距仍会保留。额外设置 Outset 还可能让元素超出窗口。

- **Expand**：左右、上下分别扩大矩形。
- **Translate**：水平位移为“右外扩量 − 左外扩量”，垂直位移为“上外扩量 − 下外扩量”，尺寸不变。因此勾选相对的两边可能相互抵消；固定角标通常只勾选对应的两条相邻边。
- 不要在 SafeAreaOutset 的祖先上放置会裁剪该元素的 Mask / RectMask2D，否则延伸部分可能不可见。

## 自动布局管理

挂载 SafeAreaOutset 后，只需配置 Reference Orientation、Apply Mode、Outset 和 Bleed Edges。组件自动保存原始布局，并始终从原始布局计算结果；重复调整参数、切换模式或启停组件不会累加外扩。

组件启用期间会驱动 RectTransform 的锚点、位置和尺寸。禁用或移除组件时恢复原始布局。需要改变元素本身的位置和尺寸时，可禁用组件后正常编辑 RectTransform，再启用即可自动采用新布局，无需调用额外 API 或点击记录按钮。

原始布局作为组件的隐藏序列化数据保留在场景、Prefab 和 Player 中。无需用户维护基准数据。

## 事件和脚本调用

- `SafeArea.EffectiveSafeArea` 是计算出的屏幕空间矩形，原点在左下角。首次成功应用前为默认矩形；驱动冲突或组件禁用时，不应将缓存值视为最新布局。
- `SafeAreaChanged`（C#）和 Inspector 中的 `On Safe Area Changed`（UnityEvent）仅在有效矩形变化时触发，参数为同一个 Rect。订阅事件不会立即回放当前值；订阅方需要时应自行读取 EffectiveSafeArea。
- 属性 setter 只修改配置，通常在下一次 Update 时应用；不要在设置 Padding 等属性后立即读取并期待已更新的 EffectiveSafeArea。
- SafeAreaOutset 自动订阅父级事件，在更换直接父级时重新订阅，并检查屏幕、自身配置和 Canvas.scaleFactor 的变化。

## 布局兼容性与当前限制

- 两个组件都会写入 RectTransform 属性。避免与同对象的 ContentSizeFitter / AspectRatioFitter，或控制该对象的父级 LayoutGroup 同时使用；必要时增加独立包装节点。
- SafeArea 驱动锚点、尺寸和位置，并将 offsetMin / offsetMax 清零。已有手动偏移不会保留。
- SafeAreaOutset 用 Canvas.scaleFactor 做像素换算，没有处理额外祖先缩放、旋转和任意相机投影。World Space Canvas、非全屏相机视口和任意嵌套容器不应视为已支持。
- SafeAreaOutset 发现 RectTransform 已由其他组件驱动时会停止写入；禁用时也不会覆盖其他驱动器的布局。仍应避免让多个组件争用同一个 RectTransform。
