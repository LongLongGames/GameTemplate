# 服务端配置目录

## ExcelConfigCompiler 产物（与客户端同表）

由客户端 **Tools → Excel Config Compiler** 导出后拷贝到本目录（或 CI 同步）。

模板不内置具体玩法表。各游戏按需放入例如：

| 文件 | 说明 |
|------|------|
| `Level.bytes` | 关卡表（示例） |
| `Item.bytes` | 道具表（可选） |
| `GameRules.bytes` | 服务端规则表（可选） |

字段顺序必须与客户端生成的对应 `*.cs` 一致。

二进制格式：Magic `EXCF` + version + rows（与 [ExcelConfigCompiler](https://github.com/LongLongGames/ExcelConfigCompiler) 一致）。

运行时：`src/Game.Shared/ExcelConfigCompiler.Runtime/` + `GameConfigStore`。

## Docker

- 镜像内：`/app/config`（User Dockerfile 在构建时 `COPY config/`）
- Compose 可挂载：`./config:/app/config:ro` 便于本地热替换
- 环境变量：`Config__Root`（默认 `/app/config`）
