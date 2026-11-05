# 发布统一仓库

统一仓库为 [Novorizon/UnityPackages](https://github.com/Novorizon/UnityPackages)，当前发行标签为 `v0.1.0-preview.10`。`main` 维护最新源码，发行安装地址使用固定标签。

已有维护工作区可按以下方式提交后续发行：

```sh
git push origin main
# 修改 workspace.json 的 release 并完成 sync、verify 后创建新发行标签：
git tag <new-release>
git push origin <new-release>
```

请勿在已经包含其他内容的远端上强制推送。来源记录和旧仓库均保留；这里是经整理的源码导入，不重写旧仓库历史。

`workspace.json` 的 repository/release 是安装目录与版本来源。更换仓库名称或准备后续版本标签时，先修改该配置，再执行 `tools/workspace.py sync` 与 `verify`。推送前核对标签和安装目录一致；已公开的标签保持不变，后续更新使用新标签。

每个包可以分别从 UPM Git 子目录安装；完整集合继续由聚合安装器处理，因为 Unity 不会把 package.json 内的 Git 传递依赖当作有效版本依赖。将来统一发布 scoped registry 时，可以进一步改为注册表版本依赖。

UnityPackages 的发行只包含 UPM 包与开发工具。GameStarter 的源码、工程清单和交付在独立仓库维护，不纳入此发行。
