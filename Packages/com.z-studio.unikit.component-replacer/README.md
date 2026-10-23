# Component Replacer

Unity 6 编辑器组件批量替换工具：把 A 换成 B，迁移兼容字段并修复引用。支持直接处理 Project Prefab，无需逐个打开场景或进入 Prefab Mode。

## 快速开始

1. 打开 **Tools → UniKit → Component Replacer**，选择原组件 A 和目标组件 B。类型列表支持搜索，也可拖入脚本、Inspector 组件或 GameObject。
2. 选择范围，点击 **扫描并预览**。
3. 检查资产路径和对象路径，取消不需要的条目，再点击 **替换**。包含资产时按钮会明确显示 **替换并保存 N 个 Prefab**。
4. 场景修改自行保存，可用 **Ctrl / Cmd + Z** 撤销/重做；Prefab 资产直接保存，并生成磁盘备份，可点击 **还原上次资产替换**。

Inspector 组件右键 **Replace Component…** 会自动填入 A，将该对象加入指定范围。

## 批量范围

| 范围 | 使用方法 |
| --- | --- |
| 当前选择（默认） | 在 Hierarchy 或 Project 多选对象、Prefab、文件夹后扫描；可混合选择 |
| 指定对象、Prefab 或文件夹 | 把多个对象/Prefab/文件夹拖入列表；支持“添加当前选择”、逐项移除和清空 |
| 已打开场景 / 当前 Prefab Mode | 扫描所有已打开场景；处于 Prefab Mode 时只处理当前 Prefab |
| Project 全部 Prefab | 扫描 Assets 下所有 Prefab，再在预览中勾选需要的条目 |

文件夹递归包含子目录中的 `.prefab`。对象包含子对象和非激活对象；重叠目录、父子选择和重复选择会去重。A 精确匹配，不包含其派生类。选择 Prefab 资产中的子对象会处理其所属 Prefab，执行前可在预览中精确取消条目。

只修改勾选的 A；路径搜索只过滤显示，隐藏的勾选项仍会执行。范围内指向这些组件的兼容引用会同步修复，因此“只持有引用、不含 A”的 Prefab 也可能保存，预览会显示同步引用修复的资产数量。

不自动打开未加载的 `.unity` 场景文件，不修改模型导入生成的 Prefab 或只读包资源；指定列表支持可写的嵌入包和本地包中的 Prefab。场景资产文件、脚本等不支持的输入会在扫描结果中说明。

## 迁移、引用与继承

开启“迁移兼容字段”（默认）时，按**同名、同类型**迁移 Unity 序列化字段，包括隐藏/继承字段、数组、List、嵌套数据、对象引用和兼容的 enabled 状态。B 不匹配的字段保留默认值，不猜测字段重命名或转换不同类型。

- `[SerializeReference]`、fixed buffer 及包含它们的顶层字段不迁移。非序列化字段、属性和运行时状态不迁移。
- UnityEvent 数据可以迁移，但事件方法名、签名和动画绑定不会自动转换，替换后应检查。
- 关闭字段迁移后，B 保留默认字段，其他组件的兼容引用仍会更新。
- 字段声明为 `Component`、`MonoBehaviour` 或 B 的兼容基类时可承接 B；仍声明为 A 且 B 不继承 A 时会报告对象与字段，阻止不兼容的引用迁移。
- 直接资产批处理检查扫描范围内的 Prefab，并修复已打开编辑上下文中相关的组件引用。范围外的 Prefab、未打开场景、ScriptableObject、静态状态不在扫描范围内。工具创建新组件，fileID / 实例 ID 会改变。
- 源 Prefab、Variant 和嵌套使用者同时入选时，按依赖顺序处理，并使用扫描快照恢复选中条目的字段覆盖和内部引用，避免重复添加 B。
- 单独选择 Variant 或嵌套使用者，只在该资产中产生组件替换覆盖，不修改未选中的源 Prefab。源 Prefab 的修改仍会按 Unity 规则传播到所有使用者，包括未勾选的继承条目。
- 混选源 Prefab 和场景实例时，保留所选实例的字段覆盖；场景实例单独处理则保留为场景覆盖。

## 保存、取消与恢复

Prefab 逐文件隔离加载、迁移和保存，一个文件只加载/保存一次。执行前检查扫描快照、只读状态和版本控制签出状态；扫描后资产或依赖有变化时要求重扫。已打开的相关 Prefab Mode 会阻止直接覆盖资产，请退出后批量处理，或使用“已打开场景 / 当前 Prefab Mode”范围。

- 场景组件合并为一次 Unity Undo，失败时回滚本次场景修改，不自动保存。
- 磁盘资产不依赖 Unity Undo。资产修改前备份到 `Library/UniKit/ComponentReplacer/`，最后一个成功批次可通过窗口按钮还原，包括引用同步修改的文件。
- 还原前校验全部文件。若替换后又修改了资产，拒绝覆盖后续修改，备份仍可从上述目录取回。
- 批次支持进度和取消。取消保留已经完成的资产；单个资产迁移失败不保存其部分结果，其他文件的问题会汇总显示并写入 Console。不承诺跨磁盘文件的整批事务；需要撤回时使用备份还原。
- 已有 B、Transform / RectTransform、不可编辑对象及仍被其他组件 `RequireComponent` 依赖的 A 会被排除。B 的依赖若重新引入 A，会报告失败。
- `Reset`、`OnValidate`、`ExecuteAlways` 等回调仍由 Unity 调用，回调对磁盘和静态状态的副作用不属于组件 Undo 的恢复范围。

仅在非 Play Mode、编译完成后使用。备份在 Library 下；清理 Library 会清理备份。

## 安装和代码

通过 Package Manager 的 **Add package from git URL**：

```text
https://github.com/z-studio/UniKit.git?path=Packages/com.z-studio.unikit.component-replacer
```

或 **Add package from disk** 选择本包 `package.json`。最低 Unity 6000.0，无第三方依赖。

| 文件 | 职责 |
| --- | --- |
| `ComponentReplacerWindow` / `ComponentTypePicker` | 范围输入、类型选择、预览勾选、反馈 |
| `ComponentReplacementPlan` | 展开选择与文件夹，建立稳定的资产定位与扫描快照 |
| `ComponentReplacementBatch` | 按依赖保存 Prefab、批量执行场景修改和汇总结果 |
| `ComponentPrefabSnapshot` | 保留继承字段覆盖和扫描时的引用身份 |
| `ComponentAssetBackup` | 磁盘备份与带冲突检查的还原 |
| `ComponentReplacement` / `ComponentData` | 单上下文组件替换、兼容字段恢复及引用检查 |

关键流程附中文注释。回归在独立的 Unity Edit Mode 测试工程中执行，不向当前项目添加测试组件。

用途参考 [SmartSwap – Component Replacer Tool](https://assetstore.unity.com/packages/tools/utilities/smartswap-component-replacer-tool-340554)，使用 Unity 原生编辑器 API 独立实现。
