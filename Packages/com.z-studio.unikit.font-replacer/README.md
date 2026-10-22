# Font Replacer

Unity 编辑器字体替换工具，优先支持 TextMeshPro。入口：**Tools → UniKit → Font Replacer**。

要求 Unity 6000.0+、UGUI 2.0（包含 TMP），不依赖 UniKit 的其他包，不包含运行时代码。

## 快速使用

1. 选择 **TextMeshPro** 或 **Unity Text / TextMesh**。
2. 默认使用 **只替换源字体 → 目标字体**：分别拖入源字体和目标字体，再选择处理范围。只替换引用源字体的组件，其他字体不变。
3. 点击 **扫描预览**，检查对象路径、组件类型、原字体和资产路径。
4. 按需搜索、勾选或排除结果，然后点击窗口底部的 **替换所选**。
5. 检查结果；已打开场景需要手动保存。

需要统一所有字体或修复空引用时，可切换“替换方式”，此时隐藏不适用的源字体字段。扫描不会修改字体。更改参数或 Hierarchy 选择后需要重新扫描；预览不会在脚本重载后保留。

TMP 与旧版字体页签分别记住字体、材质及范围配置，切换页签不会再清空已填写内容。

## 第一次使用时如何选择

- **只换某一部分 UI**：在 Hierarchy 选中它的父物体，使用“Hierarchy 选中的物体及子物体”。
- **换某个场景或预制体**：从 Project 拖入对应资源；可以一次添加多个场景、预制体或文件夹。
- **换整个项目**：点击“整个 Assets”；该按钮会替换当前输入列表。
- **TMP 字体选什么**：选择 `TMP Font Asset`（通常是 `.asset`），不要直接拖入 `.ttf/.otf` 文件。

窗口按“1. 选择字体 → 2. 选择处理范围 → 3. 检查替换清单”展示。扫描只生成清单，点击“替换所选”才执行修改。行标题可直接点击勾选，筛选后可用“只选当前筛选”收窄实际替换对象。

## 范围

| 范围 | 处理内容 |
| --- | --- |
| Hierarchy 选中的物体及子物体（`SelectedHierarchyObjectsAndChildren`） | 从 Hierarchy 选择场景物体，递归处理全部子物体，重叠选择自动去重 |
| Project 资源（文件夹含子文件夹）（`ProjectAssetsIncludingSubfolders`） | 添加场景或预制体时扫描该资源；添加文件夹时递归扫描其中及所有子文件夹内勾选类型的资源，不需要区分场景是否打开 |

范围只表达“处理哪些对象”。全项目不再是独立模式：点击 **整个 Assets**，等价于把 `Assets` 作为指定文件夹。该按钮会用 Assets 根目录替换当前输入列表，之后仍可用资产类型开关和排除列表缩小范围。

场景是否已打开由内部自动识别，以保护内存中尚未保存的编辑及 Undo；使用者只需选择需要处理的场景。尚未保存为资产的新场景可通过 Hierarchy 物体范围处理，或先保存再加入场景列表。

始终处理范围内的未激活物体和被禁用的文本组件；替换字体不会改变它们的激活或启用状态。
指定文件夹和资产自动去重。资产类型开关也适用于显式添加的场景和 Prefab，关闭类型后跳过相应资产。Packages、模型内嵌 Prefab 和 Prefab Mode 不在可修改范围内；请先保存并退出 Prefab Mode。

“所有字体”跳过已经使用目标字体的组件；“指定字体”精确匹配资产引用；“未指定字体 / 丢失引用”只匹配扫描时为空的字体引用。
TMP 可能在加载或校验时自动补回默认字体，这类对象会按补回后的实际字体匹配。

### 排除文件夹或资产

在“Project 资源（文件夹含子文件夹）”范围下，**扫描资源** 列表指定扫描起点；低频使用的 **排除资源** 入口放在“扫描选项”右侧，默认收起，展开后可指定跳过项。两组均可逐项填入、多选拖入或加入 Project 选中项。排除入口始终显示已配置数量，收起不影响已有规则生效。排除文件夹会跳过其中及所有子文件夹内的场景和 Prefab；路径按目录边界匹配，排除 `Assets/Demo` 不影响 `Assets/DemoExtra`。

排除针对被扫描的磁盘资产文件，不改变 Prefab 继承规则：其他已选源 Prefab 的变化仍可能传播到被排除的实例；选中父 Prefab 时也可能在其内部的嵌套实例上产生 override。需要严格限定局部修改时，应选择具体场景物体并检查预览。

## 支持的组件

- `TMP_Text`：包括 `TextMeshProUGUI`、3D `TextMeshPro` 及其派生类。不要求安装 RTL 插件；继承 TMP_Text 的 RTL 组件按同一路径处理。
- `TMP_InputField`：自身的全局字体引用单独列为一行；label 和 placeholder 按普通 TMP_Text 各自列出，遵守逐行勾选。
- `UnityEngine.UI.Text`、`TextMesh`。3D TextMesh 会同步 Renderer 的字体材质。

组件上的文字、字号、颜色、对齐、换行和布局参数保持原值；换字体仍可能因字形度量不同改变布局，需要检查实际画面。
不扫描脚本中的任意字体字段、ScriptableObject、UI Toolkit、TMP Settings、fallback 字体列表、富文本 `<font>` 标签或运行时替换逻辑。

## TMP 材质

换字体时同时换成目标字体的默认材质，避免旧材质仍引用旧图集。需要描边、阴影等样式时，先为目标字体制作 Material Preset，启用“指定目标材质”后再指定。工具会检查它是否使用目标字体图集；更换目标字体后自动恢复默认材质，避免旧 Preset 留在新字体上。

目标字体和材质必须是已保存的资产；临时 ScriptableObject 或材质实例不能作为替换目标，避免保存后丢失引用。

不直接保留旧字体材质，不自动复制样式或生成材质文件；已有独立样式需要明确选择适合目标字体的 Preset。已经使用目标字体的组件不会仅因目标材质不同而进入替换列表。

## 保存、继承与中断

- **已打开场景**：从内存扫描，保留未保存的编辑；字体和材质变更合并到一个 Undo 分组，不自动保存场景。
- **Prefab / 未打开场景**：修改并保存磁盘资产，不支持通过场景 Undo 回退，请使用版本控制。未打开场景扫描时使用隔离预览场景；应用时暂时 Additive 打开，保存后恢复原来的加载状态和活动场景。
- **Untitled 场景**：Unity 不允许在存在 Untitled 场景时 Additive 打开其他场景。工具会跳过受影响的未打开场景并报告原因；保存 Untitled 场景后重新扫描即可。
- **Prefab 继承**：先处理依赖源，再处理嵌套 Prefab、Variant 和场景，已继承新字体的项自动跳过，减少重复 override。只选择实例或父 Prefab 时，替换会生成必要的 override，不会向源 Prefab 应用修改。
- **勾选边界**：勾选决定直接修改哪些组件；修改源 Prefab 仍会按 Unity 规则影响全部未覆盖该属性的实例，包括未勾选或范围外的实例。若只想改变局部，请使用场景物体或指定父 Prefab 范围。
- **预览过期**：应用前检查资产文件及依赖哈希；场景对象检查原字体、材质和所属场景。发生变化、被删除、已关闭或无法写入的对象会跳过。修改后请重新扫描。
- **取消 / 错误**：扫描取消不产生替换；替换取消在资产边界或场景组件之间生效，已完成的修改保留。失败资产不会阻止其他资产，结果中列出问题。整次操作不承诺跨文件事务回滚。

失败资产不会保留尚未完整扫描的部分预览。应用前的文件/依赖检查也支持取消；进度按当前阶段连续推进。存活场景的单个组件处理失败时继续处理其余组件，并提示可能存在部分修改、可通过 Undo 恢复。

结果分别统计“直接修改”“已继承 / 已更新”和“其他跳过”。已继承目标字体且材质一致属于正常更新；过期、被删除或无法写入的项提供具体路径与原因，窗口显示前几条摘要，完整问题写入 Console。

预览可按 **原字体资产** 筛选，显示各字体的组件数量；同名字体通过资产路径区分。每行显示“原字体 → 目标字体”，长路径和完整信息可查看 tooltip。

原字体筛选与文字搜索共同控制显示。“只选当前筛选”会勾选所有匹配页中的组件，并取消筛选外的勾选；“取消匹配项”只取消匹配组件，“全部取消”清空整个计划。替换始终处理**全部已勾选项**，筛选外还有勾选时会明确显示数量。

结果按每页 50 项分页，可翻页或输入页码（Enter / 失焦后跳转）查看全部结果。“定位”只有点击时才加载磁盘资产，避免翻页时为整页 Prefab 额外加载内容。参数和结果分别滚动，扫描/替换按钮固定在底部；执行前显示已选项的场景内/磁盘分类，完成后按实际变更分别提示 Undo、手动保存或已保存。使用说明从底部左侧展开；空范围、未选择物体、播放/编译状态等校验原因显示在操作按钮的悬浮提示中。

## 代码结构与 Editor API

建议按 `FontReplacementOptions` → 窗口的 `DrawFontOptions / DrawScopeOptions` → `FontReplacementScanner.Scan` → `FontReplacementApplier.Apply` → `FontReplacementComponent.Replace` 阅读。参数类负责规则，窗口负责交互，扫描只生成快照，应用负责顺序/Undo/保存，组件适配只修改一个组件。

| 文件 | 职责 |
| --- | --- |
| `FontReplacementOptions` | 范围、字体类型、筛选条件和参数校验 |
| `FontReplacementScanner` | 发现资产、只读遍历、构建预览快照和定位信息 |
| `FontReplacementComponent` | 集中适配组件的字体、材质读写 |
| `FontReplacementApplier` | 过期检查、依赖排序、Undo、资产保存与结果统计 |
| `FontReplacerWindow` | 参数输入、预览勾选、搜索和进度展示 |

Editor 脚本可引用 `ZStudio.UniKit.FontReplacer.Editor`：

```csharp
using ZStudio.UniKit.Editor;

var options = new FontReplacementOptions {
    scope = FontReplacementScope.ProjectAssetsIncludingSubfolders,
    assets = { selectedSceneAsset }, // Project 中的 SceneAsset，也可添加 Prefab 或文件夹。
    kind = FontReplacementKind.TextMeshPro,
    filter = FontReplacementFilter.ReplaceSourceFontOnly,
    sourceFont = oldTmpFont,
    targetFont = newTmpFont
};
var preview = FontReplacementScanner.Scan(options);
// 可检查 preview.entries，设置 entry.selected = false 以排除。
var result = FontReplacementApplier.Apply(preview);
```

在编辑器主线程调用。扫描和应用均接受可选的 `Action<string, float>` 进度回调；抛出 `OperationCanceledException` 即可取消。

`result.Skipped` 包含 `result.AlreadyUpdated`；后者是已达到目标状态、无需重复写入的条目。结果统计以组件为单位，`Changed = SceneChanges + AssetChanges`。


枚举命名按实际操作表达：`TextMeshPro` / `UnityTextAndTextMesh` 对应两类文本组件；`ReplaceAllFonts` / `ReplaceSourceFontOnly` / `ReplaceUnassignedOrMissingFonts` 对应三种替换方式。每项均提供中文 Inspector 名称及必要注释。
