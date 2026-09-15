> 组织总览与进度：[LongLongGames](https://github.com/LongLongGames) · [Platform Roadmap](https://github.com/orgs/LongLongGames/projects/1)

# GameTemplate

游戏后端模板。用「Use this template」创建新游戏仓库，不要把本仓当长期运行服务。

新游戏：独立 Repo + 独立 Image + 独立 DB + 独立发布。  
身份走 [MP](https://github.com/LongLongGames/MP) JWT，本服务只本地验签。

## 服务

| 服务 | 说明 |
|------|------|
| game-gateway | Nginx（宿主机端口见下方公式） |
| game-user | 玩家资料 |
| game-leaderboard | 排行榜 |
| game-core | 版本/资源检查 + 玩法占位（clone 后写逻辑） |
| \*-migrate | 一次性 DbUp 迁移 Job（与对应服务同一镜像，`--migrate`） |

## 宿主机端口（组织强制约定）

完整规划见 [LongLongGames profile README §5](https://github.com/LongLongGames/.github/blob/main/profile/README.md)：

| 分区 | 段 |
|------|-----|
| 平台 MP / Dashboard | 11000–11999 |
| 可复用组件 | 12000–12999 |
| **游戏实例** | **13000+**（每游戏一块 100 端口） |

### 游戏（G = 1,2,3…）

| 角色 | 公式 | 例 G=1 (match3) | 模板默认 G=0 |
|------|------|-----------------|--------------|
| Gateway | `13000 + G×100 + 80` | 13180 | 13080 |
| Postgres | `13000 + G×100 + 32` | 13132 | 13032 |
| Redis | `13000 + G×100 + 79` | 13179 | 13079 |

内部 user / core / leaderboard **不**映射宿主机端口，只经本游戏 Gateway 访问。

`.env` 可覆盖：

```bash
HOST_GATEWAY_PORT=13180
HOST_POSTGRES_PORT=13132
HOST_REDIS_PORT=13179
```

## 创建新游戏

1. 右上角 **Use this template** 生成仓库（或 clone 后改名）
2. 在 MP Catalog 注册 `game_id`
3. **分配游戏序号 G**，按公式设置 `HOST_*`（或改 compose 映射）
4. 改 `.env` 里 `JWT_SECRET`（必须与 MP 一致）
5. 改玩法与表结构，按需扩展 `config/` 与 `GameConfigStore`
6. 启动验证

```bash
cp .env.example .env
# 设置 HOST_GATEWAY_PORT / HOST_POSTGRES_PORT / HOST_REDIS_PORT
docker compose up -d --build
```

启动顺序：postgres healthy → **migrate Jobs 依次成功** → 业务服务 → gateway。

- 网关：`http://localhost:${HOST_GATEWAY_PORT:-13080}`
- 健康检查：`GET /health`
- 联调：`./scripts/smoke-test.sh`

## 关闭 / 清理

```bash
# 彻底卸载数据库，清理调试写脏的数据
docker compose down -v
```

## 数据库迁移（统一规范）

**禁止**在多副本 API 启动路径中直接跑 DbUp。统一使用「单镜像 + `--migrate`」：

| 场景 | 命令 |
|------|------|
| 只跑某服务迁移 | `docker compose run --rm game-user-migrate` |
| 本地无 Docker | `dotnet run --project src/Game.User -- --migrate` |
| 正常启动 | `docker compose up -d`（自动先 migrate） |

Program.cs 顶部识别 `--migrate` / `RUN_MIGRATION_ONLY=true`，迁移成功后直接退出，不启动 Web。  
Compose 中业务服务通过 `depends_on: condition: service_completed_successfully` 等待对应 migrate Job。

未来 K8s：migrate 服务可平替为 Job 或 InitContainer。

## 配置表约定（ExcelConfigCompiler → Server）

客户端 **Tools → Excel Config Compiler** 导出 `.bytes`，同步到本仓库 `config/`（CI 拷贝或手动）。

| 路径 | 说明 |
|------|------|
| `config/*.bytes` | ECC 产物（Magic `EXCF`）；各游戏自定义表结构 |
| `src/Game.Shared/ExcelConfigCompiler.Runtime/` | 通用读写（与客户端同格式） |
| `src/Game.Shared/Config/GameConfigStore.cs` | 启动加载骨架；clone 后按表扩展 |

Docker：镜像内 `/app/config`，compose 挂载 `./config`。环境变量：`Config__Root`（默认 `/app/config`）。

玩法表类型（Level / Item 等）**不**内置在模板中，由各游戏 ECC 生成后放入 Shared。

## 与 MP 联调

1. MP 在 http://localhost:11080 运行
2. 登录取 JWT，请求本服务时带 `Authorization: Bearer <token>`
3. 或：`./scripts/smoke-test.sh`

`JWT_SECRET` 必须与 MP 一致。

## API（模板内置）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/v1/user/profile?game_id=... | 资料（无则创建） |
| PUT | /api/v1/user/profile | 更新资料 |
| POST | /api/v1/leaderboard/score | 提交分数 |
| GET | /api/v1/leaderboard/top | 排行榜 |
| GET | /api/v1/leaderboard/me | 自己的排名 |
| GET | /api/v1/game/status | JWT 验签示例 |
| GET | /api/v1/game/version-check | 版本/资源检查（公开，无需 JWT） |

Mail / BugReport 等按需以独立镜像引用，数据仍落在本游戏 DB。

### version-check 参数

`game_id` · `channel` · `platform` · `region`（可选，默认 cn）· `client_version_code` · `resource_version`

配置表：`client_version_config`（由 `Game.Core` 迁移创建）。

## 发布

Tag `v*`（或手动 workflow_dispatch）触发构建：

- `ghcr.io/<org>/<repo>/game-user`
- `ghcr.io/<org>/<repo>/game-leaderboard`
- `ghcr.io/<org>/<repo>/game-core`

生产可用 `docker compose -f docker-compose.prod.yml up -d`（业务服务拉 GHCR 镜像，不再本地 build）。

## 技术

.NET 10 Native AOT · Npgsql · DbUp · JWT HS256（与 MP 同 Secret）

相关 ADR：

- [ADR-0001](https://github.com/LongLongGames/.github/blob/main/docs/adr/0001-dbup-migrate-job.md) DbUp 与 API 进程分离
- [ADR-0002](https://github.com/LongLongGames/.github/blob/main/docs/adr/0002-aot-json-and-jwt.md) AOT JSON / JWT
