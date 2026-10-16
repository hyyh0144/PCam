# 构建

版本：Windows0.15.1、Android0.12.6。Windows程序由C#和两个原生组件组成；Android程序使用Java与ZXing。

## 工具

- .NET SDK 8、JDK 17。
- Android SDK Platform 35、Build Tools35.0.0。
- MSVC C++编译器、Windows SDK、WIL及C++/WinRT头文件。
- Android安装包签名密钥，由构建者自行保管。

现有脚本按以下相邻目录查找工具：

```text
工作目录/
  PCam/       本仓库
  tools/      .NET、JDK、Android SDK、MSVC、Windows SDK、WIL
```

具体路径由`build-env.ps1`、`recorder/build-native.ps1`和`virtual-camera/build-native.ps1`定义。脚本使用的是现有便携工具布局，首次构建需按实际安装位置调整；仓库不含完整工具链。

`NuGet.Config`使用相邻`tools/nuget-feed`和`tools/nuget-cache`。没有本地包缓存时，先配置可用的NuGet源或准备对应离线包。

## Windows

```powershell
. ./build-env.ps1
./build-windows.ps1
```

脚本先构建录像与虚拟摄像头组件，再发布自包含的Windows x64程序。输出位于`releases/`。首次运行注册虚拟摄像头组件需要管理员确认。

## Android

将签名文件保存在仓库外，设置 `PCAM_KEYSTORE` 为其路径、`PCAM_STORE_PASSWORD` 为密钥库密码；私钥密码不同时另设 `PCAM_KEY_PASSWORD`。这些环境变量只在本机构建时设置，不写入脚本或提交到仓库。然后执行：

```powershell
. ./build-env.ps1
./android/build-direct.ps1 -Sdk $env:ANDROID_HOME -Jdk $env:JAVA_HOME
```

该脚本使用Android引导API、官方编译打包工具及D8，输出`releases/PCam-0.12.6.apk`。签名工具从环境变量读取密码，命令行不携带密码值。覆盖已有安装必须使用匹配的签名，私钥不应提交到仓库。独立分支维护者需要自行创建并保管签名密钥，不能用新密钥覆盖安装官方版本。

PCam从Android0.12.6起使用RSA4096专用发布密钥，证书SHA256为：

```text
00CE154CECFA7943C25EAE35AE8819B84CEF5DC3630F2041C792F9B02A2C4916
```

证书指纹可以公开。密钥、密码及本机备份不在本仓库；后续发布须持续使用相同密钥。旧调试签名版本需要卸载后安装新签名版本；本次未建立旧签名到新签名的轮换链。

Java编译不能仅用桌面`--release 8`加普通classpath中的`android.jar`代替Android引导API，否则可能生成旧Android不存在的方法签名。构建中的DEX检查会拦截已知的`ConcurrentHashMap.keySet():KeySetView`调用。

## 测试

测试入口和所需工具见[TESTING.md](TESTING.md)。
