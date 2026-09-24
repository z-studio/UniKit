# UI Banner Carousel

UGUI 轮播组件，支持 Sprite、Spine 和图片＋动画＋文字＋按钮组成的复合页面。

## 直接体验 Demo

打开 **`Samples/Demo/Scenes/Demo.unity`**，点击 Unity 的 **Play**，不需要手动添加素材或绑定引用。

场景已包含图片、原创 **Orbit Bot** Spine 4.2 骨骼动画、混合页面、导航按钮和指示器。编辑模式显示混合页预览；运行后开始轮播。

左侧可以拖拽、轻扫、翻页、暂停/恢复、重新播放、跳到末页、调整视口大小，以及禁用/恢复轮播。混合页内的 **Try this button** 按钮有独立点击反馈。下方 **Live Events** 显示选中、点击和播完事件。

右侧 **Playground** 可实时调整：

| 控件 | 可体验的内容 |
| --- | --- |
| Content | 全部混排、纯图片、Spine、混合页面、双页循环、单页、空列表 |
| Playback | Loop / PingPong / Once |
| Transition | Slide / CrossFade / Instant |
| Direction | 四个滑动方向 |
| Dwell / Transition 滑条 | 停留时长 / 切换时长 |
| Indicators | 圆点、径向进度、自定义条形 Prefab |
| Auto play / Pause on hover | 自动播放和悬停暂停 |
| Drag & flick / Show indicators | 手势与指示器开关 |
| Unscaled time / Time scale = 0 | 冻结游戏时间，比较轮播的时间模式 |
| Per-page dwell | 图片使用全局时长，Spine 5 秒，混合页 4 秒 |
| Switch indicator accent | 切换指示器强调色 |

建议试一下：**Two-page loop + Loop** 检查首尾衔接；**Once + Restart** 检查结束事件；**Time scale = 0** 后切换 Unscaled time；在混合页拖动很短的距离观察回弹。

完整 Demo 使用项目中已安装的 **Spine Unity 4.2** 和 **Input System**。核心轮播只依赖 UGUI；Spine 适配为独立可选程序集。示例的 Spine JSON、图集、材质和页面 Prefab 都已随包提供，无需 Spine Editor。场景层级和 Prefab 均可直接编辑，UI 绑定保存在场景中。

## 接入自己的页面

1. 在 Canvas 下创建有确定宽高的 RectTransform，添加 `BannerCarousel`。Canvas 需要 GraphicRaycaster，场景需要 EventSystem 和对应输入模块。
2. 在 **Pages** 中按顺序添加页面。图片填写 Sprite；复合页填写根节点为 RectTransform 的 Prefab。两者同时填写时使用 Prefab。`Duration = 0` 使用全局停留时间。
3. `Content` 留空时以组件自身为视口，默认自动裁切。不要在视口上放 LayoutGroup；导航按钮、指示器、固定装饰应放在视口外。
4. 导航按钮绑定 `Previous()` / `Next()`。指示器添加 `BannerCarouselIndicators` 并指定 Carousel。

Pages 是唯一的内容入口；指示器统一使用独立组件。轮播内部创建独立容器，不移动或删除调用方的装饰节点。页面根节点拉伸到视口，在子节点中配置具体布局。

```text
IntroPage (RectTransform)
├── Background (Image)
├── Character (SkeletonGraphic + BannerSpinePage)
├── Description (Text / TMP_Text)
└── Action (Button)
```

图片页支持 Preserve Aspect。示例 `SpinePage.prefab` 和 `MixedPage.prefab` 展示了真实的复合内容组织方式；`BarIndicator.prefab` 展示自定义指示器。

## 播放约定

- **Loop**：首尾连续循环，双页同样支持双向切换。
- **PingPong**：自动顺序为 0 → 1 → 2 → 1 → 0，不重复停留端点；手动导航在边界停止。
- **Once**：播到末页并完成该页停留后触发一次 `OnPlaybackCompleted`。
- 停留时间从过渡完成后开始计时，不包含切换时间。逐页 Duration 可覆盖默认值。
- 四个方向表示内容的移动方向；下一页始终为索引 +1。
- 拖动距离使用视口本地 UI 单位，适配 Canvas 缩放；轻扫阈值设为 0 可禁用轻扫。所有过渡模式下拖动都跟手滑动。
- 手动切换和回弹完成后重新计时；短拖动不会触发点击或选中事件。非循环边界有阻尼回弹。
- 过渡期间忽略新的动画切换请求；`GoTo(index, false)` 可立即中断并跳转。
- Pause 保留倒计时，当前过渡仍会完成。禁用时取消未完成的过渡；启用后恢复已选页并重新计时，显式 Pause 状态保留。
- 空列表保留组件，单页不自动翻页。无效空页面被过滤，API 索引对应过滤后的列表。

页面按索引按需创建和缓存，离屏时停用。循环切换不重复 Instantiate，也不为双页循环复制 Spine 实例。SetPages / Rebuild 清理旧实例；适用于有限数量的 Banner 和介绍页。

## Spine 页面

在每个 `SkeletonGraphic` 旁添加 `BannerSpinePage`：

- Animation 留空时使用 SkeletonGraphic 的 Starting Animation。
- 支持循环、动画速度、每次选中重播或继续原动画。
- 默认进入期间保持姿势，停稳后开始动画；关闭 Wait Until Selected 可在进入时播放。
- 离屏时暂停动画并停用页面，停止网格更新。
- Spine 时间模式独立配置，默认使用非缩放时间。

使用 CanvasGroup 淡入淡出时，需要匹配 Spine 的材质与网格设置。Demo 已配置 **CanvasGroup Compatible** 材质/网格、关闭 **PMA Vertex Colors**，并启用图集对应的 **Straight Alpha Input**。可直接参考示例材质和 SkeletonGraphic 设置。

适配程序集通过 `com.esotericsoftware.spine.spine-unity` 自动启用。Assets 方式导入 Spine 时，需要手动添加 `UI_BANNER_CAROUSEL_SPINE` define 并提供 `spine-unity` / `spine-csharp` 程序集。

自定义页面逻辑可继承 `BannerPageBehaviour`：

- `OnPageShown()`：变为可见，包括拖拽预览。
- `OnPageSelected()`：切换完成、正式选中；重新启用也会调用。
- `OnPageHidden()`：离开、取消预览、重建或禁用。

这些回调用于管理页内内容。更换整个轮播数据时，使用 `OnBannerChanged` 或下一帧操作，避免在生命周期回调中递归重建。

## 指示器

`BannerCarouselIndicators` 支持圆点、进度、自定义 Button Template，以及显示/单页隐藏、点击跳转、普通/选中颜色、选中缩放、大小、间距。默认自动水平排列；关闭 Automatic Layout 后可使用自定义布局。

自定义模板的 Target Graphic 应为 Image；进度样式需要非空 Sprite 并设为 Filled，底图可放在独立节点。`Style`、`Template`、`Size` 属性改变时自动重建；`SelectedColor` 实时更新。Inspector 中修改模板或布局后，运行时调用 Rebuild 应用。

指示器放在独立容器中，不能放进轮播页。`BannerCarousel.ShowIndicators` 控制整个轮播的指示器显示。

## API

```csharp
carousel.SetPages(new[] {
    new BannerPage { Sprite = cover, Duration = 2f },
    new BannerPage { Prefab = spinePage, Duration = 5f },
    new BannerPage { Prefab = mixedPage, Duration = 4f }
});
carousel.PlaybackMode = BannerPlaybackMode.PingPong;
carousel.Transition = BannerTransition.Slide;
carousel.TransitionDuration = 0.35f;
carousel.Play();       // 启动自动播放并重置倒计时
carousel.Pause();      // 暂停倒计时
carousel.Resume();     // 继续原倒计时
carousel.Next();
carousel.Previous();
carousel.GoTo(2);      // 动画跳转
carousel.GoTo(0, false); // 立即跳转
```

状态：`Count`、`CurrentIndex`（空列表为 -1）、`Progress`、`IsPlaying`、`IsPaused`、`IsTransitioning`、`CanGoNext`、`CanGoPrevious`。

运行时开关：`AutoPlay`、`PauseOnHover`、`AllowDrag`、`UseUnscaledTime`、`ShowIndicators`。时长、方向、播放与过渡模式也有对应属性。

事件：`OnBannerChanged(int)` 在初始选中和每次切换完成后触发；`OnBannerClicked(int)` 为页面点击；`OnPlaybackCompleted()` 为 Once 播完。初始事件请在 OnEnable / Awake 订阅，或订阅后读取 CurrentIndex。
