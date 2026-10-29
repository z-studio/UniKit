# UI Card Curl

`CardCurl` 是一个用于 Unity UI 的咪牌组件。玩家可以拖动牌的四角或四边，松手后根据拖动进度翻开或回弹。组件也提供自动翻牌、操作提示，以及翻牌完成和回弹复原完成事件。

## 先运行示例

1. 使用 Unity 6，并安装 UGUI、Input System 和 URP。示例场景使用 Input System UI 输入模块和 URP 相机。
2. 在 **Project Settings → Player → Active Input Handling** 中启用 Input System；修改后按 Unity 提示重启编辑器。
3. 打开 **Window → Package Manager**，在 **In Project** 中选择 **UI Card Curl**，展开 **Samples**，点击 **Demo → Import**。
4. 在 Project 窗口打开 `Assets/Samples/UI Card Curl/1.0.0/Demo/Demo.unity`，进入 Play Mode。
5. 从牌的四角或四边向内拖动，然后松手。牌的中心区域不响应操作。

| 示例操作 | 作用 |
| --- | --- |
| Rotate -30° / Rotate +30° | 旋转牌，观察不同角度下的拖动和翻牌 |
| Angle 0° | 只恢复旋转角度 |
| Reset | 重新盖牌、角度归零，并清零示例的累计完成次数 |
| Hint | 重新盖牌，演示一次边角拖动提示 |
| 四角、四边按钮 | 重新盖牌，再从屏幕上对应的角或边自动翻开 |

Hint 和自动翻牌按钮会先重置牌，方便重复体验。它们的方向以屏幕为准：例如牌旋转 90° 后，Left edge 仍选择位于屏幕左侧的牌边。斜向放置时，边按钮选择朝向最接近的边，角按钮选择最接近的角。实际业务中是否重置，由调用方决定。

## 放入自己的场景

组件需要 **Unity 6 和 UGUI 2.0**。

1. 准备一个 Canvas，确认它带有 **Graphic Raycaster**。
2. 确认场景中有 **EventSystem**，并使用与项目输入设置对应的 UI 输入模块。
3. 将 `Res/CardCurl.prefab` 拖到 Canvas 下。也可以选中 Canvas 内的父节点，执行 **Tools → UniKit → UI Card Curl → Create UI Card**。
4. 在 CardCurl 组件上设置 **Front Image**（牌面）和 **Back Image**（牌背）。图片应导入为 **Sprite (2D and UI)**。
5. 调整牌根节点的 RectTransform 尺寸和位置，进入 Play Mode 后拖动牌的边角。

使用 Screen Space - Camera 或 World Space Canvas 时，请给 Canvas 指定用于 UI 交互的相机。

预制体内部的 `Mask → Back → Front → Shadow` 用于显示折页与阴影。接入时保留这些层级和组件引用，日常调整集中在牌根节点和 CardCurl 参数上。

## 常用参数

| Inspector 参数 | 含义 |
| --- | --- |
| Front Image / Back Image | 牌面、牌背的 Sprite |
| Flip Threshold | 松手时需要达到的翻牌进度，范围 0～1；数值越大，需要拖动越远 |
| Only Once | 开启后，每次盖牌只允许完成一次翻牌；再次操作前调用 Reset |
| Flip Time / Flip Ease | 自动翻牌各阶段的时长和缓动方式 |
| Recovery Time / Recovery Ease | 未达到翻牌进度时，回弹的时长和缓动方式 |
| Starting Drag Area Tips | 场景开始时演示提示的位置；None 表示不自动提示 |
| Tips Ratio | 提示距离占该方向跨度的比例，默认 0.15，范围 0～0.25；0 表示不播放提示 |
| Tips Time / Tips Ease | 提示每段运动的时长和缓动方式 |
| On Flip Complete | 翻牌完成时触发的事件 |
| On Recovery Complete | 未达到翻牌阈值、回弹并完全复原后触发的事件，可用于播放音效等处理 |

**希望点击就翻牌时，将 Flip Threshold 设为 0。** 点击仍需落在四角或四边，中心区域不响应。

动画使用不受 `Time.timeScale` 影响的时间；即使游戏时间暂停，提示、回弹和翻牌仍会播放。

## 用代码控制

将下面的脚本保存为 `CardCurlExample.cs`，挂到场景物体上，把牌拖到 Inspector 的 **Card** 字段。然后将三个按钮的 **On Click** 分别绑定到 `Reveal`、`ResetCard` 和 `ShowHint`。

```csharp
using UnityEngine;
using ZStudio.UniKit.UI;

public sealed class CardCurlExample : MonoBehaviour {
    [SerializeField] private CardCurl card;

    private void Awake() {
        if (!card) {
            Debug.LogError("请在 Inspector 的 Card 字段绑定 CardCurl。", this);
            enabled = false;
        }
    }

    private void OnEnable() {
        card.onFlipComplete.AddListener(OnFlipCompleted);
        card.onRecoveryComplete.AddListener(OnRecoveryCompleted);
    }

    private void OnDisable() {
        if (card) {
            card.onFlipComplete.RemoveListener(OnFlipCompleted);
            card.onRecoveryComplete.RemoveListener(OnRecoveryCompleted);
        }
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

    private void OnRecoveryCompleted() {
        Debug.Log("本次未翻开，牌已复原，可以在这里播放回弹音效。", card);
    }
}
```

这三个方法由不同按钮触发。不要在同一帧依次调用“翻牌 → 重置 → 提示”，因为重置会停止刚启动的翻牌。

`DragArea` 表示牌自身的四角或四边，随牌一起旋转。例如 `DragArea.Left` 始终指牌本地坐标中的左边。调用 `Flip`、`TryFlip`、`ShowDragTips` 和 `TryShowDragTips` 时，都使用这个方向约定。

### 处理翻牌与复原结果

在 Inspector 的 **On Flip Complete** 中绑定翻开后的处理，在 **On Recovery Complete** 中绑定未翻开、回弹复原后的处理。例如可将后者绑定到场景中 AudioSource 的播放方法。

`onRecoveryComplete` 在回弹动画结束、组件恢复空闲且画面已经复原后触发，每次完整回弹只通知一次。Flip Threshold 大于 0 时，在有效边角仅按下再松手也会走回弹流程；阈值为 0 时则按点击翻牌规则处理。

主动调用 Reset 或换图、禁用组件、尺寸变化导致操作取消，以及回弹被新的拖动、提示或翻牌打断，都不会触发复原完成事件。提示动画结束也不会触发。两个完成事件的回调内都可以立即重置、换图、发起下一次动作或销毁物体。

### 换牌与查询状态

| 成员 | 用途 |
| --- | --- |
| `SetImages(front, back)` | 同时设置牌面和牌背，并重新盖牌 |
| `SetFrontImage(sprite)` / `SetBackImage(sprite)` | 设置其中一面，并重新盖牌 |
| `Reset()` | 停止当前操作、清除翻牌次数并重新盖牌；保留牌的尺寸、位置和旋转 |
| `Flip(area)` / `ShowDragTips(area)` | 播放自动翻牌或提示，可用于不需要返回值的调用 |
| `TryFlip(area)` / `TryShowDragTips(area)` | 返回是否成功启动动作；true 表示已启动，完成通知由事件提供 |
| `IsBusy` | 是否正在拖动、回弹、提示或翻牌 |
| `IsInteractable` | 是否可以接受新的指针交互 |
| `FlipCompletedCount` | 自上次重置或换图以来，完成的翻牌次数 |

提示和回弹可以被新的拖动打断，因此 `IsBusy` 和 `IsInteractable` 可能同时为 true。已经开始的自动翻牌会拒绝新的翻牌请求。`TryShowDragTips` 在拖动中也会返回 false。

开启 Only Once 并完成翻牌、组件被禁用、方向为 None 或正在翻牌时，动作请求可能被拒绝。Tips Ratio 为 0 时也不会启动提示。

## 了解示例结构

示例源文件位于包内的 `Samples~/Demo`，通过 Package Manager 导入后在 `Assets/Samples/UI Card Curl/<包版本>/Demo` 中使用。示例脚本只在导入后参与工程编译。需要调整 UI、按钮绑定或脚本时，请修改已导入的示例；重新导入前先备份自己的修改。

- `Canvas / Demo controls`：标题、操作说明、状态文字和三行按钮。按钮的 On Click 已在 Inspector 中配置，可以直接查看绑定的方法。
- `Canvas / Card area / Rotation space / CardCurl`：牌的显示区域。布局组件负责为面板和牌分配空间，锚点控制牌的比例，并为旋转留出余量。
- `Demo / CurlDemo`：示例操作脚本。阅读顺序为事件订阅、按钮操作、完成回调、界面刷新。它不负责牌的拖动计算或动画推进。

示例标题中的 Completed 是累计演示次数，按 Reset 后清零；组件的 `FlipCompletedCount` 则会在每次重置或换图时清零。

## 常见问题

**拖动没有反应**

检查 EventSystem、UI 输入模块、Canvas 的 Graphic Raycaster，以及牌根节点 Image 的 Raycast Target。确认 CardCurl 已启用、没有其他 UI 挡在牌前面，并从四角或四边开始拖动。

**翻开一次后不能继续操作**

Only Once 已开启。调用 Reset 重新盖牌，或关闭 Only Once 允许连续翻牌。

**提示没有出现**

确认 Tips Ratio 大于 0，方向不是 None。牌正在拖动、正在翻牌或已被 Only Once 限制时，需要先结束当前操作或 Reset。

**图片变形或尺寸不合适**

调整根 RectTransform 的宽高比，使其与牌图一致。牌面和牌背使用相同的显示尺寸。示例中的牌由父级布局与锚点控制尺寸，可在场景中调整这些设置。

**Console 提示引用或层级错误**

选中牌查看 CardCurl Inspector 中的错误说明。优先使用提供的预制体，并检查 Mask、Back、Front、Shadow 的引用及层级。
