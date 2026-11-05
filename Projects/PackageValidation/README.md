# PackageValidation

用 Unity 2022.3.62f1 打开本目录，清单引用根目录全部 11 个包，并依赖 Unity Test Framework 1.1.33。用于包导入、程序集检查和包内 Editor 测试；不包含游戏业务或可玩的场景。

聚合安装包会核对已经安装的版本、嵌入官方 HybridCLR 并调用封装默认配置。实际 Editor、UPM、渲染及 IL2CPP 验收尚未在当前环境执行。
