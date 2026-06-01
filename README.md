# Phinix Rework Red Packet

作者：Natsuki

这是面向 Phinix-Rework command pipeline 的红包 submod。它不修改 Phinix host，也不通过反射注入宿主私有状态；客户端和服务端都按 Phinix-Rework 的扩展发现机制加载。

## 设计边界

- 客户端入口实现 `IPhinixExtensionModule` / `IActivatablePhinixExtensionModule`。
- 客户端 UI 通过 `IMainTabProvider` 注册到 Phinix ServerTab。
- 角标通过 `IBadgeProvider` 注册。
- 出站请求通过 `IFrameworkClientCommandTransport.TryHandleOutgoingCommand()` 进入 command pipeline。
- 服务端入口实现 `IServerDefaultCommandHandler`，由服务端 extension registry 调度。
- 物品数据作为红包 command payload 的结构化字段传输，不使用当前尚未完整可用的独立 Item pipeline。

## 构建前提

先构建或取得 Phinix-Rework 的真实客户端/服务端产物。本仓库不提供 `Utils`、`UserManagement`、`ClientExtensionAbstractions` 的占位程序集，也不会把这些依赖编译进红包 DLL。

可选环境变量：

- `PHINIX_REWORK_ROOT`：Phinix-Rework 仓库或发布产物根目录。
- `PHINIX_CLIENT_COMMON`：包含 `Assemblies/03-Utils.dll`、`06-UserManagement.dll`、`07-ClientExtensionAbstractions.dll` 的客户端 `Common` 目录。
- `PHINIX_SERVER_BIN`：包含服务端 `Utils.dll`、`UserManagement.dll` 的 Phinix-Rework 服务端输出目录。
- `RIMWORLD_MANAGED`：RimWorld `Managed` 程序集目录；默认从当前 Mods 目录推导。

## 编译

```bash
PHINIX_REWORK_ROOT=/path/to/Phinix-Rework ./build.sh
```

输出：

- `Output/phinix-rework/Common/Extensions/12-Natsuki.PhinixRedPacket.Client.dll`
- `Output/phinix-rework/Common/Languages/...`
- `Output/phinix-rework/Server/UserExtensions/12-Natsuki.PhinixRedPacket.Server.dll`

## 部署

1. 将 `Output/phinix-rework/Common/` 覆盖到 Phinix-Rework 客户端包的 `Common/`，其中包含客户端 DLL 和语言文件。
2. 将服务端 DLL 放入 Phinix-Rework 服务端的 `UserExtensions/`。
3. 重启客户端和服务端，检查 Phinix 日志中是否出现 `natsuki.redpacket` capability。

不要依赖把本仓库作为单独 RimWorld mod 启用来完成客户端注入；标准路径是 Phinix-Rework 的 extension loader。
