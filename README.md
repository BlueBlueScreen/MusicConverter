# Music Converter

一个开源、免费的 Windows 桌面 App：一次选择或拖入多个 `.mgg` 文件，通过 `qmdec` 逐首解密为 OGG，再由 FFmpeg 批量转换为高品质 MP3。单首失败不会中断剩余任务，结束后会显示成功/失败统计。

[下载最新版本](https://github.com/BlueBlueScreen/MusicConverter/releases/latest)

## 使用发布版

- 前往 [GitHub Releases](https://github.com/BlueBlueScreen/MusicConverter/releases)，下载 `MusicConverter-Setup-2.0.0.exe`。
- 推荐使用安装版；安装器会创建卸载项，并可选择创建桌面快捷方式。
- 免安装：打开 `portable` 目录，双击 `MusicConverter.exe`。
- FFmpeg 与独立版 qmdec 已随程序提供，无需安装 Python、.NET 或手动下载转换工具。

第一次解密前，请打开 QQ 音乐并登录拥有对应音乐权益的账号，然后在 App 中点击“QQ 音乐授权”。

> 本项目不包含解密密钥。请只处理你有权访问的音乐文件，并遵守服务条款与当地法律。

## 从源码构建

要求：.NET 8 SDK、Inno Setup 6，以及已准备好的静态版 `ffmpeg.exe` 和独立版 `qmdec.exe`。

1. 根据 `tools/README.txt` 准备 `tools/ffmpeg.exe` 与 `tools/qmdec.exe`。第三方二进制不存放在 Git 仓库中。
2. 在项目根目录运行：

```powershell
.\build.ps1
```

构建结果位于 `publish\portable`，安装包位于 `publish`。

如需自行重新生成独立版 `qmdec.exe`，还需要 Python 3.10+ 与 PyInstaller，入口脚本位于 `Packaging/qmdec_entry.py`。

问题反馈：`ruitodd@163.com`

## 开源许可

本项目源码以 [MIT License](LICENSE) 开放。随程序分发的 qmdec 与 FFmpeg 仍分别遵循其自身许可证，详见 `ThirdPartyNotices.txt` 与 `licenses` 目录。

## 第三方组件

- [qmdec](https://github.com/Sophomoresty/qmdec)（MIT）
- [FFmpeg](https://ffmpeg.org/) / [BtbN Windows Builds](https://github.com/BtbN/FFmpeg-Builds)（GPL build）

详情见 `ThirdPartyNotices.txt` 与发布版 `licenses` 目录。
