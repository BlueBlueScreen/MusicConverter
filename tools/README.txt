把 qmdec.exe 和 ffmpeg.exe 放在此目录，应用会自动识别。

也可以在应用的“工具设置”中手动选择这两个程序，或把它们加入系统 PATH。

qmdec:  https://github.com/Sophomoresty/qmdec
安装:   pip install git+https://github.com/Sophomoresty/qmdec.git
FFmpeg: https://github.com/BtbN/FFmpeg-Builds/releases
请选择 ffmpeg-master-latest-win64-gpl.zip（静态版）。
不要只复制 gpl-shared 包里的 ffmpeg.exe；它还依赖同包中的多个 DLL。
