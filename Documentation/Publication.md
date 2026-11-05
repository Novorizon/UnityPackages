# 发布统一仓库

本地仓库名为 UnityPackages，目标远端为 `Novorizon/UnityPackages`，准备的首次版本标签为 `v0.1.0-preview.2`。包内容与验证已准备；本轮 GitHub 连接不提供创建仓库接口，CLI 对 GitHub API 返回 Forbidden，尚未创建远端。

有建仓权限的账号创建空仓库后，可以将本地仓库推送过去：

```sh
git remote add origin https://github.com/Novorizon/UnityPackages.git
git push -u origin main
git push origin v0.1.0-preview.2
```

请勿在已经包含其他内容的远端上强制推送。来源记录和旧仓库均保留；这里是经整理的源码导入，不重写旧仓库历史。

`workspace.json` 的 repository/release 是安装目录与版本来源。更换仓库名称或首次版本标签时，先修改该配置，再执行 `tools/workspace.py sync` 与 `verify`。推送前核对标签和安装目录一致；已公开的标签保持不变，后续更新使用新标签。

每个包可以分别从 UPM Git 子目录安装；完整集合继续由聚合安装器处理，因为 Unity 不会把 package.json 内的 Git 传递依赖当作有效版本依赖。将来统一发布 scoped registry 时，可以进一步改为注册表版本依赖。
