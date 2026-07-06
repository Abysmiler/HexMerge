# HexMerge

嵌入式芯片 **HEX 文件合并工具**。把 BOOT / APP / Pflash(DFlash 数据)等多个固件文件,按地址合并成单个 `Full.hex`,供烧录器一次性烧录。

- 解析标准 Intel HEX(I8/I16/I32)与 DAT 二进制数据文件
- 字节级冲突检测:自动识别「相同 / 冲突 / 独占」
- 按优先级自动仲裁 + 冲突段手动改选
- 输出标准 I32HEX(含扩展地址、起始地址、EOF),并**逐字节回读校验**
- 自动忽略「到 DFlash 的跨区间隙」,避免输出膨胀

## 运行环境

- Windows 10 / 11(需 .NET Framework 4.8,系统通常自带)
- 双击 `HexMerge.exe` 即可运行,绿色免安装

## 构建

Visual Studio 打开根目录的 `HexMerge.sln`,或命令行:

```
msbuild HexMerge.sln /p:Configuration=Debug
```

输出到 `Bin.Net/`(主程序 exe + 依赖 dll)。

## 用法

1. 启动 → 文件选择窗口
2. 添加文件:点「浏览…」或直接拖入,至少 2 个(第 3 个可选)
3. 点「比较」→ 比较窗口 → 菜单「合并 → 保存 Full.hex…」→ 预览确认 → 选保存路径

状态栏显示「已保存到 …(回读校验通过)」即成功。详见 `docs/HexMerge_使用说明书.md`。

## 目录结构

```
HexMerge/
├── src/HexMerge/         主程序(WPF,.NET Framework 4.8)
├── tests/HexMerge.Tests/ 单元测试(NUnit)
├── samples/              固件样本(BOOT/APP/Pflash)与截图
├── docs/                 设计文档、使用说明书
├── Bin.Net/              编译输出(.gitignore 忽略)
├── .gitignore
└── HexMerge.sln
```

## 许可

个人项目。Copyright © Akimio 2026。
