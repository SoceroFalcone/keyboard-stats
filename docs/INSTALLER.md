# Windows 安装包

`installer/KeyboardStats.iss` 使用 Inno Setup 7 编译，当前软件版本取自桌面项目文件。应用标识固定为 `{6FC3BA63-C891-4EB5-89EF-3CE67955CDD7}`，后续版本应沿用，便于覆盖安装和管理卸载。

## 构建

```powershell
./scripts/build-installer.ps1

# 编译器位于其他目录时
./scripts/build-installer.ps1 -CompilerPath 'C:\Tools\Inno Setup 7\ISCC.exe'
```

默认编译器路径为 `artifacts/installer-tools/InnoSetup7/ISCC.exe`，本次使用 Inno Setup 7.1.1。工具来自 [官方发布页](https://jrsoftware.org/inno-setup/downloads)，下载文件已验证 Authenticode 签名及 Pyrsys B.V. 发布者，使用其便携模式解包到工作目录。

构建会重新编译不含调试符号的 self-contained win-x64 单文件应用，使用独立临时目录生成分发清单，然后编译安装包。执行程序对外命名为「键盘统计器.exe」，程序集名和数据目录仍沿用 KeyTrail，以兼容现有数据。

发布目录 `release/<版本>/` 包含：

- `键盘统计器-<版本>-Windows-x64-安装包.exe`
- `键盘统计器-<版本>-Windows-x64-分享包.zip`
- `使用说明.txt`
- `SHA256.txt`

ZIP 仅包含安装包、说明和 SHA-256 校验文件，适合转发。构建记录在 `artifacts/distribution/build.json`，实际载荷清单与哈希在 `payload-manifest.json`。

## 安装行为

- 安装目标为当前用户，默认 `%LOCALAPPDATA%\Programs\KeyboardStats`。
- 支持 Windows 10 build 19041 及之后版本的 x64 系统；不声明对 ARM64、x86 或旧版 Windows 的支持。
- 默认提供桌面、开始菜单快捷方式。开机启动仍由软件偏好设置管理，安装器不会主动启用。
- 完成页可打开软件；静默安装不会自动启动统计。
- 安装文件使用明确清单：程序、说明及所用 .NET / Windows Desktop 运行包的许可文件。没有项目代码、测试数据、历史记录、设置或备份。
- 更新、卸载时检测当前安装路径对应的进程。若仍在运行，会提示先从托盘退出，避免强行中止统计。无法确认运行状态时也会暂停操作。
- 卸载保留 `%LOCALAPPDATA%\KeyTrail`，并仅清理指向本次安装位置的开机启动项。
- 目前未配置代码签名。构建脚本和交付说明会如实标明。

## 验证

```powershell
./scripts/test-installer.ps1
```

测试使用项目内带中文和空格的独立目录、独立演示数据，不替换当前使用的程序，不创建或覆盖现有快捷方式。为实际验证 Windows 安装登记，需要能够写当前用户的应用登记信息。若检测到同一 AppId 的已有正式安装，则中止隔离测试。

检查包括首次安装、安装文件完整性、安装后启动、单实例、正常退出、运行中的安装与卸载拦截、退出后的覆盖安装和卸载、历史数据保留、现有进程 / 快捷方式 / 自动启动设置保留，以及 ZIP 解压后的安装包哈希。

结果在 `artifacts/distribution/installer-test.json`。这是当前 Windows 电脑上的完整安装流程验证，没有声称在其他 Windows 版本的虚拟机中测试。
