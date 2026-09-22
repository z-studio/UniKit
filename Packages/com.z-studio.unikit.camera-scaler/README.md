# Camera Scaler

Camera Scaler 用于让 Unity 相机像 `CanvasScaler` 一样，根据参考分辨率和实际屏幕宽高比调整可视范围。它同时支持正交相机和透视相机，可用于横竖屏适配、异形比例设备以及需要固定水平视野的游戏。

## 安装

1. 从 `Window > Package Manager` 打开 `Package Manager`
2. 选择 `+ > Add package from git URL...`
3. 输入以下命令进行安装
    * https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.camera-scaler

<p align="center">
  <img width="80%" src="https://user-images.githubusercontent.com/47441314/118421190-97842b00-b6fb-11eb-9f94-4dc94e82367a.png" alt="Package Manager">
</p>

或者，打开 `Packages/manifest.json` 文件，并将以下内容添加到 dependencies 块中：

```json
{
    "dependencies": {
        "com.z-studio.unikit.camera-scaler": "https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.camera-scaler"
    }
}
```

如果要设置目标版本，请按如下方式指定：

* https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.camera-scaler#v1.0.0

## 快速开始

1. 在带有 `Camera` 的 GameObject 上添加 `Layout/ZStudio/Camera Scaler`。
2. 将 `Reference Resolution` 设置为设计内容时使用的分辨率，例如 `1080 × 1920`。
3. 确认 `Reference Orthographic Size` / `Reference Field Of View` 等于你在参考分辨率下的设计值。添加组件时会从 Camera 自动采集；之后请改组件上的基准，而不是只改 Camera。
4. 根据游戏的画面策略选择 `Scale Mode`。
5. 直接在 Game View 查看效果，切换宽高比检查画面边界，无需进入 Play Mode。

组件启用后即自动适配，修改参数会直接反映到画面。添加组件时自动读取 Camera 的 Size/FOV 作为初始参考值，后续始终以组件上的参考值计算，不会累积缩放。禁用或移除组件时自动恢复接管前的相机参数，无需手动备份或恢复。

## 五种适配模式

| 模式 | 行为 | 常见用途 |
|---|---|---|
| `ConstantHeight` | 保持垂直可视范围 | Unity 默认相机行为、纵向视野必须固定 |
| `ConstantWidth` | 保持水平可视范围 | 竖屏游戏、横向玩法边界必须固定 |
| `MatchWidthOrHeight` | 在固定宽度和固定高度之间按权重插值 | 需要和 CanvasScaler 的 Match 策略一致 |
| `Expand` | 保证参考区域全部可见，设备多出的空间显示额外内容 | 不能裁掉玩法区域 |
| `Shrink` | 不显示参考区域之外的内容，必要时裁减参考区域 | 不允许看到关卡边界之外 |

`MatchWidthOrHeight` 的权重为 `0` 时等同 `ConstantWidth`，为 `1` 时等同 `ConstantHeight`。正交和透视模式均在投影尺度的对数空间插值。

## 运行时 API

```csharp
using UnityEngine;
using ZStudio.UniKit;

public sealed class CameraController : MonoBehaviour {
    [SerializeField] private CameraScaler m_Scaler;

    private void Start() {
        // 2 倍放大。正交相机会将 Size 缩小一半；
        // 透视相机会按投影平面尺度做等价缩放。
        m_Scaler.CameraZoom = 2f;

        // 运行时修改适配配置会立即刷新相机。
        m_Scaler.ReferenceResolution = new Vector2(1080f, 1920f);
        m_Scaler.ScaleMode = ScaleMode.Expand;
        m_Scaler.MatchWidthOrHeight = 0.5f;
        m_Scaler.ApplyTiming = ApplyTiming.OnPreCull;
    }
}
```

可用成员：

- `ReferenceResolution`：当前参考分辨率；宽高非法时会被修正为 `1`。
- `ScaleMode`：当前适配模式，类型为 `ScaleMode`。
- `MatchWidthOrHeight`：宽高匹配权重，自动限制在 `0～1`。
- `ReferenceOrthographicSize`：参考分辨率下的正交垂直半尺寸。
- `ReferenceFieldOfView`：参考分辨率下的透视垂直视野角。
- `CameraZoom`：缩放倍率，必须是大于 `0` 的有限值；非法输入会被拒绝。可在 Inspector 中序列化。
- `ApplyTiming`：将结果写入 Camera 的时机（`ApplyTiming.Update` / `LateUpdate` / `OnPreCull`）。
- `HorizontalSize`：参考分辨率下、未应用 Zoom 的正交水平半尺寸。
- `HorizontalFov`：参考分辨率下、未应用 Zoom 的水平视野角。
- `Refresh()`：修改 Camera 的宽高比或投影类型后，立即按已有参考值重新适配，无需等待下一次更新。组件禁用或对象未激活时不写入相机；调整设计视野请设置参考 Size/FOV 属性。

纯计算逻辑在 `CameraScalerMath` 中，不依赖组件生命周期，便于测试或给其他相机系统复用。

## Zoom 的正确用法

Camera Scaler 会接管以下属性：

- 正交相机：`Camera.orthographicSize`
- 透视相机：`Camera.fieldOfView`

运行时不要直接修改它们来实现缩放，应修改 `CameraZoom`。透视相机的角度不是线性尺度，因此组件会按照 `tan(FOV / 2)` 所表示的投影平面尺度计算 Zoom；这比直接执行 `FOV / Zoom` 更准确。

如果其他系统必须直接修改相机参数，应先明确新的基准值需求：

- 只想按当前宽高比重新套用已有基准：调用 `Refresh()`。
- 需要改变设计视野：设置 `ReferenceOrthographicSize` / `ReferenceFieldOfView`。

## 生命周期和动态变化

- 编辑和运行模式均在 `OnEnable` 完成首次适配，因此其他组件可在 `Start` 中读取适配后的相机。
- 屏幕宽高比、工作模式、Match 权重、参考分辨率、参考 Size/FOV、Zoom 以及正交/透视切换都会自动触发刷新。
- 在 Camera Scaler 自身初始化之前设置公开属性是安全的；配置会在启用时应用。
- Unity 对象不是线程安全的，所有 API 必须在主线程调用。
- 禁用组件期间配置修改不会写入 Camera；重新启用组件会重新应用适配。
- 输入未变化时复用计算结果，但仍会在指定时机恢复被其他系统改写的 Size/FOV。
- 同一物体不能挂多个 Camera Scaler。

## 编辑模式与自动恢复

修改 Game View 比例、组件参数或相机投影类型都会自动刷新，无需保持选中组件。编辑模式下不受 `Apply Timing` 影响。

组件内部保存接管前的 Size/FOV，与设计参考值分别管理。原始参数使用隐藏的序列化字段保存，因此保存场景或 Prefab 后重新加载，也能在禁用或移除组件时正确恢复。进入和退出 Play Mode 时自动处理恢复与重新适配。

## 参数保护

- 参考分辨率的宽、高必须大于 `0`；Inspector 和运行时 API 都会修正非法值。
- `CameraZoom` 必须大于 `0`，且不能是 `NaN` 或无穷大。
- Match 权重会限制在 `0～1`。
- 透视 FOV 的最终结果会限制在 Unity 可用的 `1°～179°`。
- 相机宽高比异常时会临时回退到参考分辨率的宽高比；极端比例会限制在安全计算范围内，避免除零、溢出和非法投影。

## 与其他相机系统配合

- Built-in、URP 和 HDRP 均使用 Unity `Camera` 的 Size/FOV，因此适配算法本身不依赖渲染管线。
- Cinemachine 或自定义相机控制器也可能在每帧写入 Size/FOV。应当只保留一个最终写入者。若这些系统在 `LateUpdate` 或渲染前才定稿，可选择 `On Pre Cull`，在 Built-in 使用 `OnPreCull`，在 URP/HDRP 使用 `RenderPipelineManager.beginCameraRendering`。本组件使用 Unity 默认执行顺序。`Late Update` 不保证晚于其他相机控制器；若需要在该阶段覆盖控制器的结果，请在项目的 Script Execution Order 中将 CameraScaler 安排在对应控制器之后执行。渲染回调中的其他写入者也需要明确先后顺序。
- 启用 Physical Camera 时，FOV 与焦距/传感器尺寸互相派生。建议关闭 Physical Camera，或确保没有其他系统同时写入这些属性。
- 多相机项目应在每个需要独立适配的 Camera 上分别添加组件并配置参考分辨率。
