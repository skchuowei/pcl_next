# pcl-next（NeoPCL）

> PCL2 的第三方修改版（fork）。在 [Meloong 的 PCL2](https://github.com/Meloong-Git/PCL) 源码基础上进行持续性改写与跨平台化。

[![Stars](https://img.shields.io/github/stars/skchuowei/pcl_next?style=flat&label=Stars&labelColor=444444&color=eac54f)](https://github.com/skchuowei/pcl_next)
[![Issues](https://img.shields.io/github/issues/skchuowei/pcl_next?style=flat&label=Issues&labelColor=444444&color=1F883D)](https://github.com/skchuowei/pcl_next/issues)

pcl-next 在 PCL2 的基础上，把内核（下载 / 版本解析 / 启动）从 VB.NET 迁移到 C#，运行时升级到 **.NET 10**，并扩展出 **Windows / Linux / Android** 三条平台线（`net10.0-windows` / `net10.0-linux` / `net10.0-android`）。三条平台线各自独立演进，代码互不合并。

## 分支与平台

分支命名统一为 `<代号>-<平台>`：代号固定为 `dev` / `slush` / `alpha` / `beta` / `rc` 五个，分支名里不带版本号；版本号只出现在 tag 和发布说明里。

| 分支 | 平台 | 定位 |
|---|---|---|
| `dev-Windows` | Windows | **主线 / 功能开发线**。新功能与重构都落在这里（当前版本名 `2.100.0.7`、`VersionCode = 5007`）；可以本地编译、验证、归档，但按规则**不推发布 tag、不创建 GitHub Release**。 |
| `slush-Windows` → `alpha-Windows` → `beta-Windows` → `rc-Windows` | Windows | 稳定化到发布候选的逐级推进线，对外制品只从这几条分支发布。最新公测版为 `v2.100.0.7-beta.2-Windows`。 |
| `dev-linux` → `rc-linux` | linux | Linux 平台线（独立工作区），推送须过 `kernel-guard.yml` 跨平台门禁。 |
| `feat/*` / `fix/*` | — | 短期分支：从对应的 `<代号>-<平台>` 拉出，修完合回原分支，不跨平台、不跨代号。 |
| `main` | — | 默认分支，指向最近一次稳定基线。 |

Android 线是独立的本地仓库（`net10.0-android`），目前只在本机演进、未推送到本仓库。

## 许可证

本仓库派生自 PCL2，遵循上游《PCL 分发有限许可》（详见根目录 [`LICENCE`](./LICENCE)）。请在遵守该许可的前提下合理使用；如有疑问请先查阅许可文本。

## 相关

- 上游 PCL2：<https://github.com/Meloong-Git/PCL>
- 发布说明与更新日志：[`ReleaseNotes/`](./ReleaseNotes)
- 开发规则与分支 / 发布规范：[`AGENTS.md`](./AGENTS.md)、[`分支与发布规范.md`](./分支与发布规范.md)
