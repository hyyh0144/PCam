# 代码与资源来源

本文件区分直接改编的代码、通过API调用的依赖和外部构建工具。改编文件保留原版权，库依赖不标成项目自有实现。

## 微软VirtualCamera示例

上游项目：[microsoft/Windows-Camera](https://github.com/microsoft/Windows-Camera)。原始目录：`Samples/VirtualCamera/VirtualCameraMediaSource/`。许可：[Microsoft-LICENSE.txt](virtual-camera/Microsoft-LICENSE.txt)，MIT。

核对日期为2026-10-16，对照提交为`c1bd9e099f15c2ecf735216c7dd065cdb02a803f`。原始导入记录没有保存提交号；以下链接固定到核对版本，不能视为原始导入提交的证明。“一致”指补来源注释前、忽略换行差异后的内容比较。

| 本项目文件 | 上游文件 | 使用方式与改动 |
| --- | --- | --- |
| [dllmain.cpp](virtual-camera/dllmain.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/dllmain.cpp) | 与对照版本一致，仅补充来源注释。 |
| [pch.h](virtual-camera/pch.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/pch.h) | 移除物理相机包装分支的头文件，加入PCam共享帧结构和所需标准头文件。 |
| [SimpleFrameGenerator.cpp](virtual-camera/SimpleFrameGenerator.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleFrameGenerator.cpp) | 将示例测试图改为读取PCam共享帧，加入边界、时效和双缓冲检查；保留示例的RGB/YUV转换函数。 |
| [SimpleFrameGenerator.h](virtual-camera/SimpleFrameGenerator.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleFrameGenerator.h) | 增加帧路径、映射句柄及缓存帧成员。 |
| [SimpleMediaSource.cpp](virtual-camera/SimpleMediaSource.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleMediaSource.cpp) | 传递共享帧路径，调整分配器处理和关闭检查。 |
| [SimpleMediaSource.h](virtual-camera/SimpleMediaSource.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleMediaSource.h) | 增加帧路径成员和访问方法。 |
| [SimpleMediaStream.cpp](virtual-camera/SimpleMediaStream.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleMediaStream.cpp) | 改为1280×720，接入帧路径、独立MF样本缓冲及时间戳。 |
| [SimpleMediaStream.h](virtual-camera/SimpleMediaStream.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleMediaStream.h) | 增加帧路径和样本时间成员。 |
| [VirtualCameraMediaSource.def](virtual-camera/VirtualCameraMediaSource.def) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/VirtualCameraMediaSource.def) | 保留COM导出，增加PCam发布、写帧、停止和注册接口。 |
| [VirtualCameraMediaSource.h](virtual-camera/VirtualCameraMediaSource.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/VirtualCameraMediaSource.h) | 替换媒体源CLSID。 |
| [VirtualCameraMediaSourceActivate.cpp](virtual-camera/VirtualCameraMediaSourceActivate.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/VirtualCameraMediaSourceActivate.cpp) | 仅保留合成媒体源，移除物理相机包装分支并加入激活锁。 |
| [VirtualCameraMediaSourceActivate.h](virtual-camera/VirtualCameraMediaSourceActivate.h) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/VirtualCameraMediaSourceActivate.h) | 使用PCam CLSID与激活锁，移除不用的包装媒体源成员。 |
| [winrtCommon.cpp](virtual-camera/winrtCommon.cpp) | [原文件](https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/winrtCommon.cpp) | 与对照版本一致，仅补充来源注释。 |

`SimpleFrameGenerator.cpp`中的`RGB24ToYUY2`、`RGB24ToY`、`RGB32ToNV12`和`RGB32ToNV12Frame`属于上述示例的像素转换实现；PCam增加了输入长度、尺寸和步幅检查。`pch.h`内的`wilEx::make_unique_cotaskmem_array`及`winrtCommon.cpp`中的WinRT辅助声明同样来自示例，不是另行实现的第三方工具库。

这些文件头均标注来源链接、许可位置与改动摘要。未引入示例的VirtualCameraManager界面、物理相机包装实现或示例测试项目。

## PCam集成代码

下列目录为PCam功能实现，现有来源记录未列出其他外部源码导入。

| 路径 | 内容与边界 |
| --- | --- |
| `windows/` | 接收协议、WPF界面、图层合成、录制控制和恢复；QRCoder、NAudio等为依赖调用 |
| `android/app/src/main/java/cn/phonecam/` | 相机与服务控制、连接协议、音频发送、NV21旋转及变焦/对焦；扫码调用ZXing |
| `recorder/` | 调用Windows Media Foundation的H.264/AAC录制与验证；没有自带编解码器源码 |
| `virtual-camera/PcamBridge.cpp` | PCam发布端、共享帧写入、注册与会话管理 |
| `virtual-camera/PcamShared.h` | PCam共享帧结构及常量 |
| `virtual-camera/CameraProbe.cpp` | 枚举和读取系统虚拟摄像头的验证程序 |
| `tests/` | 项目测试与测试替身；`tests/android-stubs`仅供桌面测试，不是Android平台代码 |

调用Windows、Android或.NET API不等于复制这些平台的实现源码；具体依赖及版本见[THIRD-PARTY.md](THIRD-PARTY.md)。

## 随源码提供的JAR

- `android/app/libs/core-3.5.3.jar`：[ZXing core 3.5.3官方Maven目录](https://repo.maven.apache.org/maven2/com/google/zxing/core/3.5.3/)，Apache-2.0。SHA256为`8d8064c1636fdaef7189dd9055c7d59950a8940a12f2293956446ec3c109fd82`。本地文件摘要如上；远程核验因连接超时未完成。
- `android/gradle/wrapper/gradle-wrapper.jar`：[Gradle v8.9.0](https://github.com/gradle/gradle/blob/v8.9.0/gradle/wrapper/gradle-wrapper.jar)，Apache-2.0。SHA256为`498495120a03b9a6ab5d155f5de3c8f0d986a449153702fb80fc80e134484f17`，与该上游文件一致。

## 图像资源

图标原稿由OpenAI imagegen生成，各平台启动器和窗口图标由原稿缩放、裁切得到；具体文件见[assets/DESIGN.md](assets/DESIGN.md)。`windows/Assets/Black.jpg`是项目内使用的纯黑占位图。

`assets/screenshots/`中的实拍和截图由项目作者提供。两张JPG已移除EXIF并压缩，保持原有尺寸和画面内容；两张PNG使用原文件，均未做遮挡。

## 许可证边界

PCam自有代码采用[MIT许可证](LICENSE)。第三方代码和依赖保留各自版权声明及许可证，本项目的MIT许可不替代这些组件的原有许可。

