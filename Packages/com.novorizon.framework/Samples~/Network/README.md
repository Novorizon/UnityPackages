# 可选 GameServer / TCP Network Sample

从主包 Package Manager Samples 导入 TCP Network。这个目录包含服务器专用的登录、玩家 ID、进度、存档、Facebook 绑定及 Protobuf 定义，不属于 Framework Runtime。示例仅依赖主包，不需要 UI 或 YooAsset，导入不会自动连接或修改账号。

## 运行 Demo

1. 配置 Resources/NetworkSettings 的服务器地址、加密方式和通信密钥；使用配套 GameServer。
2. Tools / Novorizon / Demos / Create Network Demo Scene 创建独立演示场景。
3. Play 后输入测试账号的 Device ID 和 JWT，主动点击 Connect。
4. 点击读取或并发读取按钮查看响应；关闭 Demo 只清理它拥有的任务和订阅。

使用 GameServer 的心跳时保留默认消息号 1、间隔 15000 毫秒。服务器联调版本见仓库 [维护说明](../../../../docs/Development.md)。

## 应用入口与页面

```csharp
using Game.Framework.Network.Samples;

var game = GameServerClient.Instance;
game.Initialize();
await game.ConnectAsync(deviceId, accountJwt, ct);
var progress = await game.GetProgressAsync(ct);
```

GameServerClient 组合 Framework 的 NetworkManager，通过认证回调登录。普通页面复用 game 或 game.Network，结束时只退订并取消自己的任务。账号切换由应用入口负责；ConnectAsync(deviceId, jwt) 会先断开旧账号连接。

GameServerClient.Configure 可在首次网络访问前安装一个能刷新凭据的 provider；重连会重新取得凭据。自定义 provider 模式使用 ConnectAsync(ct)。不要把真实 JWT 保存到场景或仓库。

TcpNetworkSample 提供读进度、按版本写进度、存档、并发请求、通知和取消方法。写入方法仅在主动操作时调用。业务失败、版本冲突与 Facebook 绑定语义由服务器定义。

## 消息与生成

消息位于 Game.Framework.Network.Samples.Messages。GameServerResultCode 也是本示例的协议定义；认证拒绝码在示例中转为通用 ConnectionRejectedException，Framework 不识别这些数值。

协议源和校验记录位于本目录 Protocol~，生成 C# 位于 Messages。维护者从仓库根目录运行：

```sh
dotnet restore build/Game.Framework.Network/Game.Framework.Network.csproj --locked-mode
python3 scripts/sync-server-protocol.py --server-root ../GameServer
python3 scripts/sync-server-protocol.py --server-root ../GameServer --check
```

link.xml 随 Sample 导入，保留示例消息。自己的游戏消息和裁剪声明由项目维护，不需要修改 Framework 的 Runtime。

UPM 安装后仅所选 Sample 被导入。Assets 替代布局同样将 Samples~ 保持为忽略目录，需要示例时只复制对应子目录到活动的 Assets 目录。
