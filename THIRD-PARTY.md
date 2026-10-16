# 第三方依赖

本文件列出依赖库、随源码提供的二进制文件和构建组件。直接改编的源码另见[SOURCES.md](SOURCES.md)。

## 应用依赖

| 项目 | 版本 | 用途与调用位置 | 引入方式 | 许可证 |
| --- | --- | --- | --- | --- |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.6.0 | `windows/Pairing.cs`生成二维码 | NuGet，版本在`windows/PhoneCam.csproj` | [MIT](licenses/QRCoder-LICENSE.txt) |
| [ZXing core](https://github.com/zxing/zxing) | 3.5.3 | `android/app/src/main/java/cn/phonecam/QrDecoder.java`识别二维码 | `android/app/libs/core-3.5.3.jar` | [Apache-2.0](licenses/ZXing-LICENSE.txt) |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | `windows/AudioPlayback.cs`播放手机PCM | NuGet，版本在`windows/PhoneCam.csproj` | [MIT](licenses/NAudio-LICENSE.txt) |
| [.NET / WPF / ASP.NET Core](https://github.com/dotnet/runtime) | net8.0目标；当前构建运行时8.0.31 | Windows界面、网络、HTTP输出 | SDK/FrameworkReference；自包含发布带运行时 | [许可证](licenses/DotNet-LICENSE.txt)与[第三方声明](licenses/DotNet-ThirdPartyNotices.txt) |

上述库通过API调用使用，没有把其编码器、解码器或音频库源码改名放入应用目录。NuGet还会引入传递依赖，具体版本以还原后的`project.assets.json`和发布输出为准。

## 直接改编的源码

`virtual-camera/`内13个文件来自或改编自微软Windows-Camera的VirtualCameraMediaSource示例，包括媒体源、媒体流、COM激活和像素格式转换。使用[MIT许可](virtual-camera/Microsoft-LICENSE.txt)。逐文件路径、对照版本和PCam改动见[SOURCES.md](SOURCES.md)。

## 原生构建依赖

| 项目 | 当前工具版本 | 使用位置 | 许可证/声明 |
| --- | --- | --- | --- |
| [Windows Implementation Library（WIL）](https://github.com/microsoft/wil) | Microsoft.Windows.ImplementationLibrary 1.0.250325.1 | `virtual-camera/pch.h`及虚拟摄像头实现 | [MIT](licenses/WIL-LICENSE.txt) |
| [C++/WinRT](https://github.com/microsoft/cppwinrt) | SDK头文件标记2.0.230511.6 | `virtual-camera/`的WinRT/COM辅助类型 | [MIT](licenses/CppWinRT-LICENSE.txt) |
| Windows SDK / Media Foundation | 当前SDK构建环境10.0.26100 | `virtual-camera/`、`recorder/`调用系统API | 通过Windows SDK使用；不在本仓库提供SDK或系统实现源码 |

WIL和C++/WinRT的头文件不随本仓库提供，由构建工具目录引入。Media Foundation是Windows组件，不是本项目实现的编解码器。

## Android构建

- **Gradle Wrapper 8.9**：`android/gradle/wrapper/gradle-wrapper.jar`来自[Gradle v8.9.0](https://github.com/gradle/gradle/blob/v8.9.0/gradle/wrapper/gradle-wrapper.jar)，已逐字节核对；`gradlew.bat`为调用该JAR的简化启动脚本。许可见[Gradle-LICENSE.txt](licenses/Gradle-LICENSE.txt)，其中保留上游的第三方声明。
- **Android Gradle Plugin 8.7.3**：在`android/build.gradle`声明，使用Gradle时下载；直接APK构建脚本不使用它。
- **Android SDK / Build Tools 35**与**JDK 17**：作为外部工具使用，不随源码提供。`core-lambda-stubs.jar`仅用于编译，不打包进APK。

Gradle Wrapper JAR SHA256：

```text
498495120a03b9a6ab5d155f5de3c8f0d986a449153702fb80fc80e134484f17
```

## 图标

图标原稿及各平台派生文件的来源见[assets/DESIGN.md](assets/DESIGN.md)。

PCam自有代码采用[MIT许可证](LICENSE)；第三方许可按各自文件适用。


## NuGet还原清单

以下为Windows0.15.1还原时记录的14个包，未包含SDK自带的框架引用。NAudio子包来自同一个NAudio项目，Microsoft/System包属于.NET组件，许可文件见上表。

| 包 | 版本 |
| --- | --- |
| `Microsoft.NETCore.Platforms` | 3.1.0 |
| `Microsoft.Win32.Registry` | 4.7.0 |
| `Microsoft.Win32.SystemEvents` | 6.0.0 |
| `NAudio` | 2.2.1 |
| `NAudio.Asio` | 2.2.1 |
| `NAudio.Core` | 2.2.1 |
| `NAudio.Midi` | 2.2.1 |
| `NAudio.Wasapi` | 2.2.1 |
| `NAudio.WinForms` | 2.2.1 |
| `NAudio.WinMM` | 2.2.1 |
| `QRCoder` | 1.6.0 |
| `System.Drawing.Common` | 6.0.0 |
| `System.Security.AccessControl` | 4.7.0 |
| `System.Security.Principal.Windows` | 4.7.0 |
