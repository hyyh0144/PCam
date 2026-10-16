# 测试

先按[构建说明](BUILD-NOTES.md)准备工具，并在仓库根目录加载`build-env.ps1`。

## 手机连接与音频

```powershell
./tests/run-disconnect-test.ps1 -Jdk $env:JAVA_HOME
./tests/run-audio-lifecycle-test.ps1 -Jdk $env:JAVA_HOME
./tests/run-cross-platform.ps1 -Dotnet "$env:DOTNET_ROOT/dotnet.exe" -Jdk $env:JAVA_HOME
./tests/run-audio-integration.ps1 -Dotnet "$env:DOTNET_ROOT/dotnet.exe" -Jdk $env:JAVA_HOME
./tests/run-recovery-integration.ps1 -Dotnet "$env:DOTNET_ROOT/dotnet.exe" -Jdk $env:JAVA_HOME
```

覆盖断开、并发关闭、授权分片、跨端音画、重连及心跳。`tests/android-stubs`只为麦克风资源故障测试提供替身，不是Android实现，也不能用于判断硬件兼容性。

## Windows

使用`dotnet run --project <项目路径> -c Release`运行：

| 项目 | 内容 |
| --- | --- |
| `tests/ProtocolTests.csproj` | 帧长度、分片、超时、连接及音频会话隔离 |
| `tests/CompositionTests/CompositionTests.csproj` | 图层、旋转、像素、JPEG边界与控件事件 |
| `tests/RecordingTests/RecordingTests.csproj` | 录制生命周期、音轨、输出路径与文件冲突 |
| `tests/RecordingUi/RecordingUi.csproj` | 录制按钮、错误恢复、启动恢复和关闭保存 |
| `tests/DurableRecordingTests/DurableRecordingTests.csproj` | 真实跨一分钟录制、强制退出、断尾及恢复 |

`tests/ReviewTests/ReviewTests.csproj`还需传入测试MP4路径，覆盖恢复冲突、整合失败重试与畸形片段。`recorder/RecordingProbe.cpp`用于实际解码并检查音轨和画面。

## 手机实测

1. 空列表、无声、有声、多电脑状态下分别执行单台和全部断开，再重新扫码。
2. 拒绝麦克风权限后重试授权，检查视频继续、声音按开关变化。
3. 切镜头、旋转、缩放和点击对焦，检查不支持能力时的提示。
4. 临时断网后恢复；主动断开后确认不自动重连。
5. 回桌面、息屏、通知停止后重新打开，检查资源释放和恢复行为。
6. 在旧Android和较新系统上重复关键操作。

桌面测试和音频替身不替代手机实测。
