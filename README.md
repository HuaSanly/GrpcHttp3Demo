# GrpcHttp3Demo

机器人到 VR 的控制面与 UDP 转发服务。

## 运行

要求：.NET 10 SDK。

在仓库根目录执行：

```bash
dotnet run
```

默认端口：

- TCP 7777：统一信令入口，承载 gRPC、HTTP Controller、Swagger UI、WebSocket
- UDP 7778：媒体与 UDP 控制面

## 接口文档界面

当前项目已接入 OpenAPI + Scalar，并保留 Swagger UI 作为兼容调试入口。

- 默认入口：`https://<host>:7777/scalar`
- 兼容入口：`https://<host>:7777/swagger`
- 根路径 `/` 会自动跳转到默认文档界面
- OpenAPI JSON：`https://<host>:7777/swagger/v1/swagger.json`

说明：

- 当前所有信令接口都统一走 7777，包括 gRPC、HTTP Controller 和 WebSocket
- Scalar 和 Swagger UI 都走 7777
- 部分接口需要先通过 `POST /api/client/auth/login` 获取 Bearer token，再在 Swagger 右上角 `Authorize` 中填入
- Scalar 作为默认文档界面，Swagger UI 保留用于兼容旧使用习惯

## 关键文档

- [Readme/PROTOCOL_GRPC_SIGNALING.md](Readme/PROTOCOL_GRPC_SIGNALING.md)
- [Readme/PROTOCOL_UDP_SYSTEM_MONITOR_0X07.md](Readme/PROTOCOL_UDP_SYSTEM_MONITOR_0X07.md)
- [Readme/PROTOCOL_UDP_LINK_MONITOR_0X08.md](Readme/PROTOCOL_UDP_LINK_MONITOR_0X08.md)
- [Readme/PROTOCOL_UDP_SIGNALING.md](Readme/PROTOCOL_UDP_SIGNALING.md)
- [Readme/WPF_GRPC_CLIENT_GUIDE.md](Readme/WPF_GRPC_CLIENT_GUIDE.md)

## PostgreSQL

当前服务器：Ubuntu 24.04，PostgreSQL 16，服务状态已确认是 `active`。

后端建议使用独立业务库和独立业务账号，不直接使用 `postgres/amgg` 作为应用运行账号。

当前已配置：

- 已创建 PostgreSQL 管理角色 `amgg`
- `amgg` 具备 PostgreSQL 超级用户权限
- 本机 shell 以 Linux 用户 `amgg` 登录后，可直接通过本地 socket 使用 peer 认证连接数据库

注意：这里配置的是 PostgreSQL 管理权限，不是 Linux 系统 `root` 权限。

### 第一次本机连接

在 Ubuntu 服务器上直接执行：

```bash
psql -U amgg -d postgres
```

如果只是确认当前登录身份：

```bash
psql -U amgg -d postgres -Atc "SELECT current_user, session_user;"
```

### 当前认证状态

当前已完成局域网直连配置：

- PostgreSQL 监听 `127.0.0.1:5432` 和 `192.168.3.55:5432`
- `ufw` 仅放行 `192.168.3.0/24` 到 `5432/tcp`
- `pg_hba.conf` 仅允许局域网中的 `amgg` 使用 `scram-sha-256` 登录

当前 `pg_hba.conf` 关键规则如下：

- `local all all peer`
- `host all amgg 192.168.3.0/24 scram-sha-256`
- `host all all 127.0.0.1/32 scram-sha-256`
- `host all all ::1/128 scram-sha-256`

这意味着：

- 在 Ubuntu 本机，以系统用户 `amgg` 登录后，可以无密码连接 PostgreSQL
- 从其它机器直接连 PostgreSQL，当前只允许 `192.168.3.0/24` 网段访问，且必须先给 `amgg` 设置数据库密码

### 后端配置

`ServerA` 环境下，后端数据库连接配置写在 [appsettings.ServerA.json](appsettings.ServerA.json)。当前已预置为：

```json
"Postgres": {
	"Enabled": true,
	"Host": "192.168.3.55",
	"Port": 5432,
	"Database": "grpc_http3_demo",
	"Username": "grpc_http3_app",
	"Password": "REPLACE_WITH_REAL_DB_PASSWORD",
	"SslMode": "Disable",
	"TimeoutSeconds": 5
}
```

说明：

- `Database` 和 `Username` 对应建议创建的业务数据库与业务账号
- `Password` 现在是占位符，替换成真实数据库密码后再启动服务
- 当前代码已经接入 PostgreSQL 连接测试，不会自动迁移或建表

### 真实连接测试

当前后端新增了一个受 admin token 保护的数据库测试接口：

- `POST /api/system/database/test`

使用流程：

1. 先调用 `POST /api/client/auth/login` 获取 admin bearer token
2. 带上 `Authorization: Bearer <token>` 调用 `POST /api/system/database/test`

返回成功时，会给出当前连接到的主机、端口、数据库、数据库用户和服务器版本。

### 如果要从 Windows 首次连接

有两种方式：

1. 先 SSH 到 Ubuntu，再在服务器本机执行 `psql -U amgg -d postgres`
2. 用 SSH 隧道把远端 `5432` 转到本机，再用数据库工具连接

SSH 隧道示例：

```bash
ssh -L 5432:127.0.0.1:5432 amgg@192.168.3.55
```

然后本机数据库工具连：

- Host: `127.0.0.1`
- Port: `5432`
- Database: `postgres`
- User: `amgg`
- Password: `amgg` 的 PostgreSQL 数据库密码

如果走 TCP 连接，还需要先为 `amgg` 设置数据库密码。设置完成后，可直接连接：

- Host: `192.168.3.55`
- Port: `5432`
- Database: `postgres`
- User: `amgg`
- Password: `amgg` 的 PostgreSQL 数据库密码

注意：

- SSH 登录 Ubuntu 用的是 Linux 账户 `amgg` 的密码或密钥
- 连接 PostgreSQL 用的是数据库角色 `amgg` 的密码
- 只有在 Ubuntu 本机以系统用户 `amgg` 通过 Unix socket 连接时，才可以依赖 `peer` 认证不输数据库密码

### 常用管理命令

查看服务状态：

```bash
systemctl status postgresql
```

切到 PostgreSQL 系统用户：

```bash
sudo -u postgres psql
```

查看角色：

```bash
sudo -u postgres psql -Atc "SELECT rolname, rolsuper FROM pg_roles ORDER BY rolname;"
```

创建业务数据库与业务用户的示例 SQL：

```sql
CREATE ROLE grpc_http3_app
WITH LOGIN PASSWORD 'REPLACE_WITH_REAL_DB_PASSWORD'
NOSUPERUSER CREATEDB NOCREATEROLE INHERIT;

CREATE DATABASE grpc_http3_demo OWNER grpc_http3_app ENCODING 'UTF8';
REVOKE ALL ON DATABASE grpc_http3_demo FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE grpc_http3_demo TO grpc_http3_app;
```

## 监控接口

- `/api/monitor/system/stats`
- `/api/monitor/sessions`
- `/api/monitor/udp/stats`
- `/api/system/config`

UDP 系统监控推送使用前缀 `0x07`，通过 `/api/client/monitor/subscriptions` 管理订阅。
UDP 链路监控推送使用前缀 `0x08`，通过 `/api/client/monitor/link-subscriptions` 管理订阅。
