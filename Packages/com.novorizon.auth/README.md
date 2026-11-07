# Auth

身份与账号绑定独立于 UI、支付和 Firebase。核心提供 `AuthClient`、`IIdentityProvider`、`IAuthBackend`；原生 Apple / Google、可选 Facebook 获取身份凭据，由 Firebase Auth 或项目后端验签并维护游戏用户 ID。

原生适配器和安装菜单已提供，外部 SDK 按需启用。iOS Facebook 使用 Limited Login；Apple 使用随机 nonce + SHA-256。绑定保留当前 UID，不按邮箱自动合并；取消不会提前释放尚未结束的原生弹窗。

安装步骤、命名空间、构建目标符号和调用示例见 [平台服务接入](../../Documentation/PlatformServices.zh-CN.md)。只安装导出的独立包时参阅官方仓库[同版文档](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.36/Documentation/PlatformServices.zh-CN.md)。

`Native~` 中是显式安装到项目的桥接模板。GoogleSignIn 10.1.0 要求 iOS 15；Android Credential Manager 要求 API 23。账号删除、Apple 撤销、存档合并由服务端业务实现。SDK 真机闭环仍需项目配置和设备验收。
