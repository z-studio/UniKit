# UniKit

UniKit 是 Z Studio 持续维护的 Unity 工具与组件集合，收集可复用的运行时组件和编辑器工具，后续会不断加入新的 Kit。

每个 Kit 以独立 Unity Package Manager（UPM）包组织，可按项目需要单独安装。本仓库同时也是一个 Unity 工程，用于开发、维护和体验这些工具。

## Kit 目录

### UI 与相机

| Kit | 主要用途 | 包目录 |
| --- | --- | --- |
| [UI Banner Carousel](Packages/com.z-studio.unikit.ui.banner-carousel/README.md) | Banner 轮播，支持图片与 Prefab 混排、拖拽切换、播放模式、自定义指示器及可选 Spine 页面 | `com.z-studio.unikit.ui.banner-carousel` |
| [UI Marquee](Packages/com.z-studio.unikit.ui.marquee/README.md) | 公告与跑马灯，支持图文混排、逐条轮播、连续滚动及可选 Spine 片段 | `com.z-studio.unikit.ui.marquee` |
| [UI Safe Area](Packages/com.z-studio.unikit.ui.safearea/README.md) | UI 安全区适配，支持逐边留白、居中，以及子元素向非安全区外扩或平移 | `com.z-studio.unikit.ui.safearea` |
| [Camera Scaler](Packages/com.z-studio.unikit.camera-scaler/README.md) | 根据参考分辨率与屏幕比例适配正交相机可视范围或透视相机视野角 | `com.z-studio.unikit.camera-scaler` |
| [UI Card Curl（咪牌）](Packages/com.z-studio.unikit.ui.card-curl/README.md) | 基于 CurlEffect 的 UGUI 四角/四边咪牌，支持旋转及父级缩放，包含遮罩、阴影和翻牌动画 | `com.z-studio.unikit.ui.card-curl` |

### 编辑器工具

| Kit | 主要用途 | 包目录 |
| --- | --- | --- |
| [Sprite Font Generator（SFGen）](Packages/com.z-studio.unikit.sfgen/README.md) | 将 Sprite 纹理、散图或 SpriteAtlas 制作为 UGUI Text / TextMeshPro 彩色位图字体 | `com.z-studio.unikit.sfgen` |
| [Texture Slicer](Packages/com.z-studio.unikit.texture-slicer/README.md) | 长图、大图与图集切割，支持重叠分块、行列均分、透明裁剪及 PNG / 坐标清单导出 | `com.z-studio.unikit.texture-slicer` |
| [Component Replacer](Packages/com.z-studio.unikit.component-replacer/README.md) | 指定对象 / 多选 Prefab / 文件夹批量替换组件，支持字段迁移、引用更新、场景撤销和资产备份还原 | `com.z-studio.unikit.component-replacer` |
| [Shell](Packages/com.z-studio.unikit.shell/README.md) | 在 Unity Editor 中执行外部命令，支持输出收集、标准输入、异步等待和协程 | `com.z-studio.unikit.shell` |

各 Kit 的配置方法、API、依赖及使用限制见对应文档。新增 Kit 时会继续扩展以上索引。

## 环境要求

- 当前各 Kit 的 `package.json` 均声明最低 Unity 版本为 **6000.0（Unity 6）**。
- 本仓库工程使用 **Unity 6000.3.9f1**，具体版本以 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) 为准。
- UI 类包依赖 UGUI；SFGen 还依赖 2D Sprite。已声明的依赖由 UPM 解析，具体以各包的 `package.json` 为准。
- Banner Carousel 和 Marquee 的 Spine 扩展是可选功能，核心组件不要求安装 Spine。示例所需依赖见下文与各包文档。

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
3. 根据需要打开工具窗口、添加组件，或在 Package Manager 中选择带示例的包，导入 **Samples → Demo**。

| Kit | 体验入口 |
| --- | --- |
| UI Banner Carousel | 示例需要安装 Input System，并启用新输入系统或 Both；导入 Demo 后打开 `Scenes/MinimalDemo.unity` 体验基础图片轮播，`Scenes/Demo.unity` 展示 Spine 与混合页面，完整示例还需要 Spine Unity 4.2 |
| UI Marquee | 导入 Demo 后打开 `Scenes/Demo.unity`；示例需要安装 Input System，并启用新输入系统或 Both |
| UI Safe Area / Camera Scaler | 按对应 Kit 文档在自己的 UI 或相机对象上添加组件 |
| UI Card Curl | 直接打开包内 `Samples/Demo/Demo.unity`；示例使用 Input System 与 URP，核心组件可接入工程现有的 UGUI 输入模块 |
| Sprite Font Generator | 菜单 **Tools → UniKit → Sprite Font Generator** |
| Texture Slicer | 菜单 **Tools → UniKit → Texture Slicer** |
| Component Replacer | 菜单 **Tools → UniKit → Component Replacer**，或 Inspector 组件右键 **Replace Component…** |
| Shell | 在编辑器脚本中调用，见包文档的执行与等待示例 |

示例源文件位于各包的 `Samples~` 目录；通过 Package Manager 导入后，在项目的 `Assets/Samples` 下打开场景。

## 仓库结构

```text
UniKit/
├── Assets/                       # 工程资源与场景
├── Packages/
│   ├── com.z-studio.unikit.*/     # 独立维护的 Kit
│   ├── SpineCsharp/              # 工程内的第三方 Spine C# 包
│   ├── SpineUnity/               # 工程内的第三方 Spine Unity 包
│   └── manifest.json             # 工程级依赖
├── ProjectSettings/              # Unity 工程设置
└── README.md                     # 项目介绍与 Kit 索引
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
