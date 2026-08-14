> 组织总览与进度：[LongLongGames](https://github.com/LongLongGames) · [Platform Roadmap](https://github.com/orgs/LongLongGames/projects/1)

# GameTemplate

游戏后端模板。用「Use this template」创建新游戏仓库，不要把本仓当长期运行服务。

新游戏：独立 Repo + 独立 Image + 独立 DB + 独立发布。  
身份走 [MP](https://github.com/LongLongGames/MP) JWT，本服务只本地验签。

## 服务

| 服务 | 说明 |
|------|------|
| game-gateway | Nginx（8081） |
| game-user | 玩家资料 |
| game-leaderboard | 排行榜 |
| game-core | 玩法占位（clone 后在此写逻辑） |

Postgres `5433` · Redis `6380`（与 MP 隔离）

## 创建新游戏

1. 右上角 **Use this template** 生成仓库（或 clone 后改名）
2. 在 MP Catalog 注册 `game_id`
3. 改 `.env` 里 `GAME_ID`、`JWT_SECRET`（必须与 MP 一致）
4. 改玩法与表结构，启动验证

```bash
cp .env.example .env
docker compose up -d --build
```

- 网关：http://localhost:8081
- 健康检查：`GET /health`
- 联调：`./scripts/smoke-test.sh`

## API（模板内置）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/v1/user/profile?game_id=... | 资料（无则创建） |
| PUT | /api/v1/user/profile | 更新资料 |
| POST | /api/v1/leaderboard/score | 提交分数 |
| GET | /api/v1/leaderboard/top | 排行榜 |
| GET | /api/v1/leaderboard/me | 自己的排名 |
| GET | /api/v1/game/status | JWT 验签示例 |

Mail / BugReport 等按需以独立镜像引用，数据仍落在本游戏 DB。

## 技术

.NET 10 Native AOT · Npgsql · DbUp · JWT HS256（与 MP 同 Secret）
