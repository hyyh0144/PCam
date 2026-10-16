# Windows虚拟摄像头

媒体源和COM激活部分改编自微软[Windows-Camera / VirtualCamera](https://github.com/microsoft/Windows-Camera/tree/master/Samples/VirtualCamera)示例。逐文件出处、核对版本和改动见[代码来源](../SOURCES.md)，原版权声明及[MIT许可](Microsoft-LICENSE.txt)保留。

PCam保留合成媒体源，去掉示例中物理相机包装分支。电脑合成画面通过按用户隔离的共享帧文件提供给媒体源，固定输出1280×720，提供RGB32/NV12格式和30fps媒体类型。双缓冲与序列检查避免读到写入中的帧；长时间没有新数据时输出黑帧。

`PcamBridge.cpp`、`PcamShared.h`为PCam的发布桥接和共享数据结构，`CameraProbe.cpp`为验证程序。示例中的媒体源、流、激活和转换代码与这些集成文件分别标注。

首次运行时由提升权限的辅助进程注册组件。后续创建当前用户、当前会话的虚拟摄像头，关闭程序后停止。使用Windows11的Media Foundation API，不安装内核驱动。

构建入口为`../build-windows.ps1`；仅构建组件可运行`build-native.ps1`。WIL及C++/WinRT由外部工具链提供，详见[第三方依赖](../THIRD-PARTY.md)。
