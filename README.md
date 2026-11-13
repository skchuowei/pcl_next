# pcl-next（NeoPCL）

> PCL2 的第三方修改版（fork）。在 [Meloong 的 PCL2](https://github.com/Meloong-Git/PCL) 源码基础上进行持续性改写与跨平台化。

[![Stars](https://img.shields.io/github/stars/skchuowei/pcl_next?style=flat&label=Stars&labelColor=444444&color=eac54f)](https://github.com/skchuowei/pcl_next)
[![Issues](https://img.shields.io/github/issues/skchuowei/pcl_next?style=flat&label=Issues&labelColor=444444&color=1F883D)](https://github.com/skchuowei/pcl_next/issues)

pcl-next 在 PCL2 的基础上，将内核（下载 / 版本解析 / 启动）逐步 C# 化，并扩展到 **Windows / Linux / Android** 三条平台线，目标运行时为 **.NET 10**（`net10.0-windows` / `net10.0-linux` / `net10.0-android`）。

当前处于 `dev` 代号阶段，三条平台线各自独立推进、互不合并，分支命名形如 `dev.<序号>-<平台>`（例：`dev.6-Windows`）。

## 平台线

| 平台 | 分支命名 | 说明 |
|---|---|---|
| Windows | `dev.<n>-Windows` | 主线，.NET 10 + WPF |
| Linux   | `dev.<n>-Linux`   | 独立仓库与分支，推送须过 `kernel-guard.yml` 跨平台门禁 |
| Android | `dev.<n>-Android` | 独立仓库与分支，`net10.0-android` |

## 许可证

本仓库派生自 PCL2，遵循上游《PCL 分发有限许可》（详见根目录 [`LICENCE`](./LICENCE)）。请在遵守该许可的前提下合理使用；如有疑问请先查阅许可文本。

## 相关

- 上游 PCL2：<https://github.com/Meloong-Git/PCL>
- 发布说明与更新日志：[`ReleaseNotes/`](./ReleaseNotes)
- 开发规则与分支 / 发布规范：[`AGENTS.md`](./AGENTS.md)、[`分支与发布规范.md`](./分支与发布规范.md)
