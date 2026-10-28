# UI Card Curl

基于提供的 CurlEffect 整理的 UGUI 咪牌包，保留原来的正反面、旋转遮罩与折线阴影。支持四角和四边拖动、回弹、自动翻牌与提示动画。本版重点修复牌自身旋转、父级旋转/非等比缩放，以及交互或动画过程中旋转导致的错位。

## 使用

Unity 6、UGUI 2.0。核心包不依赖 URP、RenderTexture 或第三方 Tween，可直接放进现有 Canvas。

1. 在 Canvas 下放入 `Res/CardCurl.prefab`，或选中 Canvas 内父节点执行 **Tools → UniKit → UI Card Curl → Create UI Card**。
2. 在组件 Inspector 指定 Front Image / Back Image，或调用 SetFrontImage / SetBackImage。预制体提供参考素材默认值。
3. 保留预制体中的 Mask → Back → Front → Shadow 层级，只绑定这四个引用。八方向的起点与命中区直接从根 RectTransform 计算，不再需要 Anchor / DragArea 物体。
4. 项目继续使用已有的 EventSystem 与输入模块。

下面是一个最小接入脚本。把它挂到场景物体上，在 Inspector 将牌拖到 `Card` 字段，然后将业务按钮分别绑定到 `Reveal`、`ResetCard`、`ShowHint`。这三个方法由不同操作触发，不要在同一帧依次调用。

```csharp
using UnityEngine;
using ZStudio.UniKit.UI;

public sealed class CardCurlExample : MonoBehaviour {
    [SerializeField] private CardCurl card;

    private void OnEnable() {
        card.onFlipComplete.AddListener(OnFlipCompleted);
    }

    private void OnDisable() {
        card.onFlipComplete.RemoveListener(OnFlipCompleted);
    }

    public void Reveal() {
        if (!card.TryFlip(CardCurl.DragArea.BottomRight)) {
            Debug.Log("当前无法翻牌，请等待动作完成，或先重置牌。", card);
        }
    }

    public void ResetCard() {
        card.Reset();
    }

    public void ShowHint() {
        card.ShowDragTips(CardCurl.DragArea.BottomLeft);
    }

    private void OnFlipCompleted() {
        Debug.Log("牌已翻开，可以在这里处理后续业务。", card);
    }
}
```

换图时调用 `card.SetImages(faceSprite, backSprite)`，它会同时设置正反面并重新盖牌。`Only Once` 默认开启，翻开后需调用 `ResetCard` 才能再次翻牌或提示。只有 `Flip Threshold` 设为 0 时允许点击热点翻牌，否则需要拖动；中心区域不响应。

`SetFrontImage`、`SetBackImage` 和 `Reset` 会停止当前动画、清除指针并重新盖牌；同时换两面时可用 `SetImages(front, back)`。它们不会改变根节点尺寸和锚点，正反面统一使用根节点尺寸。`Flip` 播放指定方向的翻牌；`ShowDragTips` 使用传入方向。Tips Ratio 为该方向跨度的比例：四角按对角线、四边按宽或高计算，默认 0.15，上限 0.25；设为 0 时不播放提示。`Only Once` 默认开启，完成后需要 Reset 才能再次交互。完成事件在旧动作结束和终态写入之后派发，回调内可以立即 Reset 或换图。

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

## 动作结果与交互状态

`Flip` / `ShowDragTips` 保留 void 签名，可绑定 UnityEvent。代码需要确认动作是否启动时，使用 `TryFlip(area)` / `TryShowDragTips(area)`：返回 true 表示已启动，false 表示方向无效、组件不可用或当前状态不接受该动作；提示幅度为零时也返回 false。失败不会中断已有动作。

- `IsBusy`：正在拖动、回弹、提示或翻牌。
- `IsInteractable`：当前可以接受指针交互。提示和回弹允许被新的拖动打断，所以 IsBusy 与 IsInteractable 可以同时为 true；提示方法在拖动中返回 false。
- `FlipCompletedCount`：自上次 Reset 或换图以来完成的翻牌次数。

Flip Threshold 为 0 时，允许在四角或四边热点轻触松手翻牌，中心区域仍不响应；大于 0 时要求达到对应拖动进度。Only Once、指针捕获和完成事件规则与拖动翻牌相同。

## Demo

当前开发阶段直接打开包内 `Samples/Demo/Demo.unity`，无需导入到 Assets。示例脚本会随工程编译，接下来的示例修改以该目录为准。

示例场景使用 Input System UI 模块和 URP 相机附加组件，需要项目安装 Input System 与 URP，并启用 Input System 输入后端。核心组件只依赖 UGUI；其他渲染管线或旧输入项目可将预制体放入现有 Canvas，使用项目自己的相机、EventSystem 和输入模块。

示例 UI 直接保存在场景中，停止运行时也能在 Hierarchy 查看和调整：

- `Canvas / Demo controls`：标题、操作说明、状态，以及旋转操作、四角翻牌、四边翻牌三行按钮；布局组件负责宽度适配。
- `Demo / CurlDemo`：绑定牌、控制面板、标题和状态文字。按钮的 On Click 在 Inspector 中绑定具名方法，旋转按钮使用 -30 / 30 的浮点参数。
- `Canvas / Card area / Rotation space / CardCurl`：可直接拖动四角或四边；达到阈值后松手翻开，未达到则回弹。中心区域不响应。

Canvas 上的纵向布局为说明面板和牌区分配空间；牌区内的 Aspect Ratio Fitter 保持正方形旋转空间，牌通过锚点按固定比例放入，留出任意角度旋转的余量。调整窗口尺寸时不需要额外的脚本计算。可在场景中调整说明、按钮布局与牌的锚点。

Reset 恢复牌背、0° 和示例累计完成次数；Angle 0° 只恢复角度。Hint 与自动翻牌按钮先盖牌再演示，保留角度并允许反复使用；这是示例为方便重复操作所做的处理，核心组件不会自动 Reset。完成次数是示例累计值，和组件自上次 Reset 以来的 `FlipCompletedCount` 含义不同。

`CurlDemo` 的阅读顺序是：OnEnable 订阅完成事件 → Rotate / ResetDemo / ShowHint / FlipFrom 调用牌的动作 → OnFlipComplete 接收完成通知 → RefreshLabel 更新界面。Update 只观察状态变化；实际拖动和动画由 CardCurl 处理。禁用或销毁 CurlDemo 时解除订阅并停止面板操作；UI 属于场景，不由脚本生成或销毁。

本包未附带自动化测试程序集。接入后应在目标 Canvas、输入后端和渲染管线中检查八方向拖动、旋转、回弹、提示、Reset、阈值为零的点击翻牌以及多指输入。
