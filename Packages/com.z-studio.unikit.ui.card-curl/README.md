# UI Card Curl

基于提供的 CurlEffect 整理的 UGUI 咪牌包，保留原来的正反面、旋转遮罩与折线阴影。支持四角和四边拖动、回弹、自动翻牌与提示动画。本版重点修复牌自身旋转、父级旋转/非等比缩放，以及交互或动画过程中旋转导致的错位。

## 使用

Unity 6、UGUI 2.0。核心包不依赖 URP、RenderTexture 或第三方 Tween，可直接放进现有 Canvas。

1. 在 Canvas 下放入 `Res/CardCurl.prefab`，或选中 Canvas 内父节点执行 **Tools → UniKit → UI Card Curl → Create UI Card**。
2. 在组件 Inspector 指定 Front Image / Back Image，或调用 SetFrontImage / SetBackImage。预制体提供参考素材默认值。
3. 保留预制体中的 Mask → Back → Front → Shadow 层级，只绑定这四个引用。八方向的起点与命中区直接从根 RectTransform 计算，不再需要 Anchor / DragArea 物体。
4. 项目继续使用已有的 EventSystem 与输入模块。

```csharp
using ZStudio.UniKit.UI;

// CardCurl 组件引用由 Inspector 绑定。
card.SetBackImage(backSprite);
card.SetFrontImage(faceSprite);
card.onFlipComplete.AddListener(OnVisualFlipCompleted);

card.Flip(CardCurl.EDragArea.BottomRight);
card.Reset();
card.ShowDragTips(CardCurl.EDragArea.BottomLeft);
```

`SetFrontImage`、`SetBackImage` 和 `Reset` 会停止当前动画、清除指针并重新盖牌；同时换两面时可用 `SetImages(front, back)`。它们不会改变根节点尺寸和锚点，正反面统一使用根节点尺寸。`Flip` 播放指定方向的翻牌；`ShowDragTips` 使用传入方向，Tips Offset 始终是牌根节点的本地 UI 单位，不随 Canvas 模式改变。`Only Once` 默认开启，完成后需要 Reset 才能再次交互。完成事件在旧动作结束和终态写入之后派发，回调内可以立即 Reset 或换图。

拖动只有一个捕获指针。轻触松手同样释放捕获，第二根手指不会改动第一根手指的状态。回弹、提示和翻牌均插值根节点局部位置，使用 unscaledDeltaTime；修改父节点位置、旋转或缩放不会把动画终点留在旧世界位置。

交互中心区域不拦截射线；四角和四边沿用原来的 30% 命中区。根节点尺寸变化时，未提交的拖动/提示会取消，已经提交的翻牌重映射到新布局后继续完成。动画数据在 LateUpdate 统一推进，每帧只提交一次几何；静止时只检查根节点尺寸和变换是否变化。

Inspector 会显示具体缺失引用、组件或内部层级错误。编辑器预览更新由自定义 Inspector 在主线程执行，OnValidate 只处理数据校验。

## 旋转修复

原版把世界 XY 当成牌的方向，又每帧把 Back / Front 旋转设回世界零度，旋转后会出现方向判断、遮罩和图片不一致。

当前实现以 CardCurl 根节点为唯一计算坐标系：

- 指针射线与实际变换后的牌平面相交，再通过完整逆矩阵转到局部坐标。父级非等比缩放叠加 X/Y 倾斜时，平面法线由两条变换后的切线计算。
- 拖动方向、角度、距离、阈值与动画端点均在同一局部空间计算。
- 先放置 Mask，再补偿 Back，最后提交 Front 和 Shadow，避免嵌套父节点变换使图片漂移。
- 不使用世界位置零向量作为“是否初始化”的标记，也不依赖 Start 才允许业务调用 Reset / 换图。

遮罩仍采用原版二维折页效果。旋转到完全侧向、投影面积为零或父级缩放为零时不能进行有效指针投影。

## Demo 与验证

菜单 **Tools → UniKit → UI Card Curl → Open Demo**，或通过 Package Manager 导入 Demo 并打开 `Demo.unity`。沿用参考 Demo 的牌面和初始旋转，顶部补充 ±30°、归零、Reset、Hint 和八方向翻牌按钮。Demo 适配现有 Input System 或旧输入模块，核心不依赖 Input System。

包名为 `com.z-studio.unikit.ui.card-curl`；完整组件类型为 `ZStudio.UniKit.UI.CardCurl`。已导入示例位于 `Assets/Samples/UI Card Curl/Demo`。

`Tests/PlayMode` 检查八方向在多个旋转角度、父级旋转/非等比缩放下的局部几何一致性，覆盖拖动中旋转、回弹、翻牌、Reset 重入、轻触释放、第二指针、正交/透视相机和 X/Y 倾斜。外部项目通过 manifest 的 `testables` 加入包名后运行。

验证记录见 [ImplementationNotes](Docs/ImplementationNotes.md)。这次替换只针对本包；桌面 CurlEffect 参考文件保持原样。
