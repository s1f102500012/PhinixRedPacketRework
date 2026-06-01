# Phinix Rework Red Packet

作者：Natsuki

这是面向 Phinix-Rework command pipeline 的独立红包子模组。

## 产物

- `Assemblies/12-Natsuki.PhinixRedPacket.Client.dll`：RimWorld 客户端模组 DLL。
- `ServerExtensions/12-Natsuki.PhinixRedPacket.Server.dll`：Phinix-Rework 服务端扩展 DLL。

## 部署

1. 在 RimWorld 启用 `Phinix Rework Red Packet`，并确保它排在 `hunyuan2333.phinixrework` 之后。
2. 将 `ServerExtensions/12-Natsuki.PhinixRedPacket.Server.dll` 放入 Phinix-Rework 服务端的 `UserExtensions/` 目录。
3. 启动服务端和客户端，确认 Phinix 日志里出现 `natsuki.redpacket` capability。

如果客户端没有自动发现该子模组，也可以按 Phinix-Rework 文档把客户端 DLL 复制到 Phinix-Rework 客户端包的 `Common/Extensions/` 目录。

## 编译

```bash
./build.sh
```

`Source/Stubs/` 只用于本地编译引用，不会被部署到 `Assemblies/` 或 `ServerExtensions/`。
