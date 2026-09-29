# UniKit Shell

在 Unity Editor 中执行命令、收集 stdout/stderr、发送标准输入，支持 `await`、同步等待和协程。要求 Unity 6000.0 或更新版本，支持 Windows、macOS 和 Linux Editor。

## 安装

本仓库已将包放在 `Packages/com.z-studio.unikit.shell`。其他项目可以在 Package Manager 中选择 **Add package from disk**，选中本目录的 `package.json`。包名为 `com.z-studio.unikit.shell`，命名空间为 `ZStudio.UniKit.Editor`。

## 执行和等待

所有入口、等待方法和事件订阅应在 Editor 主线程调用；事件也在主线程分发。输出读取在后台进行。日志按行分批处理，每次更新最多处理 128 个事件，并使用约 4 ms 的协作式时间预算；待处理输出队列最多容纳 1024 个事件，满时后台读取暂停，避免生产速度超过消费速度时无限积压。正常完成前会交付全部输出，不会丢弃；成功结果的 `Output`/`Error` 仍完整保存在内存中，内存用量随输出总量增长。单个用户回调无法被中断，因此回调本身应保持简短。

```csharp
using System;
using UnityEngine;
using ZStudio.UniKit.Editor;

async void RunGit() {
    try {
        // 可执行文件与参数分开传递，包含空格的参数不需要手动添加引号。
        var result = await Shell.RunCommandLine("git", "status", "--short");
        Debug.Log(result.Output);
    } catch (OperationCanceledException) {
        Debug.Log("已取消");
    } catch (Exception error) {
        Debug.LogException(error);
    }
}
```

`RunCommand` 执行脚本：Windows 使用 `cmd.exe`，macOS/Linux 使用 `/bin/bash`。脚本语法由对应 shell 决定，临时脚本会在完成、失败或取消后删除。无需 shell 展开、管道或多行脚本时，优先使用 `RunCommandLine`。

```csharp
var result = Shell.RunCommand("echo hello").Wait();
Debug.Log(result.ExitCode);
```

`Wait()` 会阻塞调用线程，同时处理日志和完成事件；Editor 界面可能无法交互，通常应使用 `await`。不要在 `onLog` 回调中调用 `Wait()`，此时会明确抛出异常，避免日志背压和嵌套等待造成死锁；请使用异步流程。

```csharp
System.Collections.IEnumerator RunAsCoroutine() {
    var request = Shell.RunCommand("echo hello");
    yield return request.ToCoroutine();
    var result = request.GetResult();
    Debug.Log(result.Output);
}
```

默认情况下，非零退出码会在 `await`、`Wait()`、协程完成或 `GetResult()` 时抛出异常，且请求的 `IsCompleted` 已经为 `true`。若要自行处理退出码：

```csharp
var settings = ShellSettings.Default;
settings.ThrowOnNonZeroExitCode = false;
settings.Quiet = true;
var result = await Shell.RunCommandLine("git", settings, "status");

if (result.ExitCode != 0) {
    Debug.Log(result.Error);
}
```

`Quiet` 关闭库的自动日志，不影响输出收集、回调和异常传播。stderr 经常用于进度或诊断，因此以 Warning 显示；退出失败再报告 Error。`Output` 和 `Error` 分别保留两个流的原始文本、换行和 ANSI 指令，不会混入输入回显或补充换行。两个流之间不保证全局输出顺序。

## 编码和颜色

stdout/stderr 默认使用 UTF-8，stdin 默认使用不带 BOM 的 UTF-8。显式指定带 BOM 的输入编码时，仅在第一次实际写入时发送一次 BOM，空输入结束不会发送 BOM。多字节字符跨读取块时会保留解码状态。旧工具可以明确指定编码：

```csharp
using System.Text;

var settings = ShellSettings.Default;
settings.OutputEncoding = Encoding.GetEncoding("gbk");
settings.InputEncoding = Encoding.GetEncoding("gbk");
var result = await Shell.RunCommandLine("legacy-tool", settings);
```

Windows 下的 `RunCommand` 会按输出编码设置脚本编码及控制台代码页；外部工具仍可能使用自己的编码。

Unity 日志支持基本色、亮色、256 色、RGB 前景色和重置指令，并保持跨行颜色状态。粗体、背景色等 SGR 样式被忽略。每条日志生成闭合的 `<color>` 标签。

- `ConsoleUtils.ConvertToUnityColor(text)`：独立文本转换。
- `ConsoleUtils.ConvertToNoColor(text)`：移除 SGR 样式指令，不是通用终端控制字符清理器。
- `ConsoleUtils.ScanColorLog(text, visitor)`：按顺序访问基本前景色及重置指令；自定义 visitor 自行管理标签。
- `ConsoleUtils.IsValidUTF8(bytes, index, count)`：严格校验完整字节范围，允许 NUL；不用于猜测编码。

## 环境变量和工具检测

```csharp
var settings = ShellSettings.Default;
settings.WorkDirectory = ".";
settings.Environment = new System.Collections.Generic.Dictionary<string, string> {
    ["MY_OPTION"] = "value",
    ["PATH"] = "/opt/my-tools/bin" // Windows 示例：@"C:\Tools\bin"
};
var result = await Shell.RunCommandLine("my-tool", settings, "argument with spaces");
```

`Shell.DefaultEnvironment` 为全局默认环境，请在主线程配置。请求环境覆盖同名默认值；PATH 按“请求 PATH → 默认 PATH → 系统 PATH”追加。多个目录必须使用当前系统的分隔符：Windows 为 `;`，macOS/Linux 为 `:`。

`Shell.ExistsCommand("git")` 搜索默认环境合并后的 PATH，Windows 同时检查 PATHEXT，也支持直接文件路径。macOS/Linux 会跳过没有执行权限的候选文件；文件格式或解释器是否有效仍以实际启动结果为准。该方法不解析 shell 内建命令、别名或请求级环境。

运行命令时，相对可执行文件路径及 PATH 中的相对目录均以 `WorkDirectory` 为基准；未指定工作目录时使用 Unity 当前目录。

## 进度、输入和取消

```csharp
var settings = ShellSettings.Default;
settings.WithProgress = true;
var request = Shell.RunCommand("echo hello", settings);
request.onLog += (type, line) => { /* 按行处理，包含最后一个未换行片段 */ };
request.OnComplete += code => { /* 此时 IsCompleted == true */ };
var result = await request;
```

后台任务窗口会显示最近输出（未换行长文本只预览最后 1024 个字符，完整结果不受影响）；暂停按钮打开独立的非模态输入窗口。回车或小键盘 Enter 提交，Esc 关闭；中文输入法确认候选时不会提交，提交失败会在窗口中显示错误。也可通过 `request.Input("answer")` 将一行加入输入队列，调用立即返回。输入在后台按提交顺序写入并刷新，不会占用主线程等待管道。需要确认写入成功时，使用 `await request.InputAsync("answer")`：任务会报告写入失败或取消。`Input()` 的异步写入错误会记入日志（`Quiet` 时不打印）。未换行提示会显示在后台进度中，完整日志行通过 `onLog` 通知。

输入结束后，调用 `await request.CompleteInputAsync()`：它会等待已经提交的写入完成，再关闭 stdin 发送 EOF。`cat`、`sort` 等等待输入结束的命令需要此步骤；不需要输入时也可直接调用。重复调用返回同一个任务，首次调用后禁止再提交新输入。不要在主线程同步阻塞等待输入任务，应使用 `await`。

```csharp
var request = Shell.RunCommandLine("sort");
await request.InputAsync("banana");
await request.InputAsync("apple");
await request.CompleteInputAsync();
var result = await request;
```

`TryCancel()` 请求终止当前进程，成功后等待方收到 `OperationCanceledException`。取消也适用于子进程已经退出、但完成事件仍等待主线程处理的阶段。取消通知优先于积压日志分发，尚未分发的日志及未完成的行会被丢弃；正常完成仍保证所有日志回调先于完成回调。请求已完成时取消返回 `false`。工作线程负责清理，取消不会提前释放正在读取的进程。`ProgressCancelable` 控制 UI 取消入口，`AutoClearProgress` 控制模态进度条自动关闭。

取消仅保证针对启动的进程发出终止请求，不保证终止其独立派生的子进程；需要整棵进程树管理的工具应自行提供关闭协议。脚本重载和 Editor 退出时也会请求取消活动进程，并最多等待 2 秒供工作线程清理管道和临时脚本。静默命令也会持续轮询模态进度条的取消按钮。

本包基于 [labbbirder/Unity-Shell](https://github.com/labbbirder/Unity-Shell) 演进，保留原 MIT 许可证。
