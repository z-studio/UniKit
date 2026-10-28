# UniKit

UniKit 是 Z Studio 持续维护的 Unity 工具与组件集合，收集可复用的运行时组件和编辑器工具，后续会不断加入新的 Kit。

每个 Kit 以独立 Unity Package Manager（UPM）包组织，可按项目需要单独安装。本仓库同时也是一个 Unity 工程，用于开发、维护和体验这些工具。

## 从哪里开始

- **把组件或工具放进自己的项目**：先按下文“安装到已有项目”安装所需的包，再打开对应 Kit 的 README 完成配置。
- **先体验效果**：打开本仓库的 Unity 工程，按“打开工程与体验示例”导入并运行示例。
- **使用编辑器工具**：安装后从 **Tools → UniKit** 打开对应窗口；具体入口见下方表格。

## Kit 目录

### UI 与相机

| Kit | 主要用途 | 包目录 |
| --- | --- | --- |
| [UI Banner Carousel](Packages/com.z-studio.unikit.ui.banner-carousel/README.md) | Banner 轮播，支持图片与 Prefab 混排、拖拽切换、播放模式、自定义指示器及可选 Spine 页面 | `com.z-studio.unikit.ui.banner-carousel` |
| [UI Marquee](Packages/com.z-studio.unikit.ui.marquee/README.md) | 公告与跑马灯，支持图文混排、逐条轮播、连续滚动及可选 Spine 片段 | `com.z-studio.unikit.ui.marquee` |
| [UI Safe Area](Packages/com.z-studio.unikit.ui.safearea/README.md) | UI 安全区适配，支持逐边留白、居中，以及子元素向非安全区外扩或平移 | `com.z-studio.unikit.ui.safearea` |
| [Camera Scaler](Packages/com.z-studio.unikit.camera-scaler/README.md) | 根据参考分辨率与屏幕比例适配正交相机可视范围或透视相机视野角 | `com.z-studio.unikit.camera-scaler` |
| [UI Card Curl（咪牌）](Packages/com.z-studio.unikit.ui.card-curl/README.md) | UGUI 四角/四边拖动与自动翻牌，支持回弹、提示、旋转及缩放，可配置点击翻牌 | `com.z-studio.unikit.ui.card-curl` |

### 游戏表现

| Kit | 主要用途 | 包目录 |
| --- | --- | --- |
| [Lucky Wheel](Packages/com.z-studio.unikit.luckywheel/README.md) | 转盘表现，支持等分与不等分奖格、指定结果索引、等待结果、格内停止位置、指针拨动和音效 | `com.z-studio.unikit.luckywheel` |

### 编辑器工具

| Kit | 主要用途 | 包目录 |
| --- | --- | --- |
| [Sprite Font Generator（SFGen）](Packages/com.z-studio.unikit.sfgen/README.md) | 将 Sprite 纹理、散图或 SpriteAtlas 制作为 UGUI Text / TextMeshPro 彩色位图字体 | `com.z-studio.unikit.sfgen` |
| [Font Replacer](Packages/com.z-studio.unikit.font-replacer/README.md) | 批量替换 TMP、UGUI Text 与 TextMesh 字体，支持场景与 Prefab 扫描、预览勾选及字体材质设置 | `com.z-studio.unikit.font-replacer` |
| [Texture Slicer](Packages/com.z-studio.unikit.texture-slicer/README.md) | 长图、大图与图集切割，支持重叠分块、行列均分、透明裁剪及 PNG / 坐标清单导出 | `com.z-studio.unikit.texture-slicer` |
| [Component Replacer](Packages/com.z-studio.unikit.component-replacer/README.md) | 指定对象 / 多选 Prefab / 文件夹批量替换组件，支持字段迁移、引用更新、场景撤销和资产备份还原 | `com.z-studio.unikit.component-replacer` |
| [Shell](Packages/com.z-studio.unikit.shell/README.md) | 在 Unity Editor 中执行外部命令，支持输出收集、标准输入、异步等待和协程 | `com.z-studio.unikit.shell` |

各 Kit 的配置方法、API、依赖及使用限制见对应文档。新增 Kit 时会继续扩展以上索引。

## 环境要求

- 当前各 Kit 的 `package.json` 均声明最低 Unity 版本为 **6000.0（Unity 6）**。
- 本仓库工程使用 **Unity 6000.3.9f1**，具体版本以 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) 为准。
- UI 类包、Lucky Wheel、Font Replacer 和 SFGen 声明了 UGUI 依赖；Lucky Wheel 还声明了 Audio 模块依赖，SFGen 还依赖 2D Sprite。已声明的依赖由 UPM 解析，具体以各包的 `package.json` 为准。
- Banner Carousel 和 Marquee 的 Spine 扩展是可选功能，核心组件不要求安装 Spine。
- 包的必要依赖与示例依赖分别说明：例如 Card Curl 组件只要求 UGUI，它的示例还使用 Input System 和 URP。导入示例前请先按下表准备依赖。

## 安装到已有项目

### 通过 Git URL 安装

在 Unity 的 **Window → Package Manager** 中选择 **Add package from git URL**，输入目标包的地址。例如安装 Camera Scaler：

```text
https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.camera-scaler
```

安装其他 Kit 时，将 `?path=Packages/` 后的目录替换为上表对应的包目录。

也可以在项目已有的 `Packages/manifest.json` 的 `dependencies` 中加入条目：

```json
{
  "dependencies": {
    "com.z-studio.unikit.camera-scaler": "https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.camera-scaler"
  }
}
```

保留项目中原有的其他依赖。左侧键名必须使用目标包 `package.json` 中的 `name`。

需要固定版本时，在 Git URL 末尾追加 `#<实际存在的标签或提交号>`。各包的版本记录在自己的 `package.json` 中，安装时使用的 Git 标签或提交号则对应整个仓库的某次快照。

### 通过本地目录安装

克隆或下载本仓库后，在目标项目的 Package Manager 中选择 **Add package from disk**，选中所需 Kit 目录中的 `package.json`：

```text
UniKit/Packages/<包目录>/package.json
```

## 打开工程与体验示例

1. 克隆本仓库：

   ```bash
   git clone https://github.com/z-studio/UniKit.git
   ```

2. 在 Unity Hub 中添加仓库根目录，使用工程对应的 Unity 版本打开，等待资源导入及依赖解析完成。各 Kit 已作为内嵌包放在 `Packages` 下。
3. 需要运行示例时，打开 **Window → Package Manager**，在 **In Project** 中选择对应 Kit，展开 **Samples**，点击 **Demo → Import**。
4. 在 Project 窗口找到 `Assets/Samples/<包显示名>/<包版本>/Demo`，打开下表对应场景并进入 Play Mode。没有示例的组件和编辑器工具，按后面的入口直接使用。

### 带场景的示例

以下场景路径均相对于导入后的 `Demo` 文件夹：

| Kit（Package Manager 中的名称） | 场景路径 | 示例依赖与体验内容 |
| --- | --- | --- |
| UI Banner Carousel | `Scenes/MinimalDemo.unity`、`Scenes/Demo.unity` | 安装 Input System，启用新输入系统或 Both。MinimalDemo 演示图片轮播；完整 Demo 还需要 Spine Unity 4.2，展示 Spine 与混合页面 |
| UI Marquee | `Scenes/Demo.unity` | 安装 Input System，启用新输入系统或 Both；体验逐条轮播和连续滚动，可选 Spine 功能见包文档 |
| UI Card Curl | `Demo.unity` | 安装 Input System 和 URP，启用新输入系统或 Both；体验边角拖动、屏幕方向的自动翻牌、旋转、Reset 和 Hint |
| Lucky Wheel | `Scenes/Demo.unity` | 安装 Input System，启用新输入系统或 Both；体验指定索引转动、等待结果、提交结果、取消及不等分奖格 |

例如 UI Card Curl 1.0.0 的场景位于 `Assets/Samples/UI Card Curl/1.0.0/Demo/Demo.unity`。路径中的显示名和版本取自该包的 `package.json`，随安装的包变化。

示例源文件统一放在各包的 `Samples~/Demo`，使用时通过 Package Manager 导入到 `Assets/Samples`。`Samples~` 中的脚本不会直接参与工程编译；调整界面或脚本时修改已导入的示例，重新导入前先备份自己的修改。

### 直接使用的组件与工具

| Kit | 使用入口 |
| --- | --- |
| UI Safe Area | 在需要适配安全区的 UI 对象上添加组件，按包文档设置边缘与子元素的适配方式 |
| Camera Scaler | 在相机对象上添加组件，设置参考分辨率与适配模式 |
| Sprite Font Generator | **Tools → UniKit → Sprite Font Generator** |
| Font Replacer | **Tools → UniKit → Font Replacer**；先扫描预览，再替换勾选项 |
| Texture Slicer | **Tools → UniKit → Texture Slicer** |
| Component Replacer | **Tools → UniKit → Component Replacer**，或 Inspector 组件右键 **Replace Component…** |
| Shell | 在编辑器脚本中调用，见包文档的执行与等待示例 |

## 仓库结构

```text
UniKit/
├── Assets/                          # 仓库工程的资源，以及导入后的示例
├── Packages/
│   ├── com.z-studio.unikit.*/        # 独立维护的 Kit
│   │   ├── package.json             # 包名、版本、依赖及示例入口
│   │   ├── README.md                # 该 Kit 的配置与使用说明
│   │   ├── Runtime/                 # 运行时组件，按需提供
│   │   ├── Editor/                  # 编辑器工具，按需提供
│   │   └── Samples~/Demo/           # 示例源文件，按需提供
│   ├── SpineCsharp/                 # 工程内的第三方 Spine C# 包
│   ├── SpineUnity/                  # 工程内的第三方 Spine Unity 包
│   ├── manifest.json                # 工程级依赖
│   └── packages-lock.json           # 已解析的依赖版本
├── ProjectSettings/                 # Unity 工程设置
└── README.md                        # 项目介绍、安装步骤与 Kit 索引
```

## 持续扩展

新增 Kit 时，建议沿用现有的独立包结构，方便按需安装与维护：

- 在 `Packages/` 下创建 `com.z-studio.unikit.<kit>` 或相应分类目录，填写 `package.json` 中的包名、版本、Unity 版本要求与必要依赖。
- 按实际用途组织 `Runtime/`、`Editor/` 及对应程序集定义，编辑器专用工具可仅保留 `Editor/`。
- 在包内维护 `README.md`，说明用途、安装、快速开始、依赖与使用限制。
- 如提供示例，将资源放入 `Samples~/`，并在 `package.json` 的 `samples` 中声明。
- 提交 Unity 资源时保留对应 `.meta` 文件，维持资源引用稳定。
- 在根 README 的 Kit 目录中补充入口；详细配置与 API 留在各包文档中维护。

第三方包的使用与分发遵循其各自的许可证说明。
