# 前端镜像:node 里构建 SPA,Caddy 托管产物 + 反代 /api 到后端。
# 构建上下文是**仓库根**(见 docker-compose.yml),这样 .dockerignore 只需维护一份。
# web/ 是 npm workspace(packages/admin 内核包 + template 消费方应用壳),npm ci 需要三份 package.json
# 都在场才能解出 workspace 依赖树。
FROM node:22-alpine AS build
WORKDIR /src

# 先只拷依赖清单:package.json 没变时这层走缓存,不必每次重装依赖
COPY web/package.json web/package-lock.json ./
COPY web/packages/admin/package.json ./packages/admin/
COPY web/template/package.json ./template/
RUN npm ci

COPY web/ ./
# 先构建内核包再构建 template(根 package.json 的 build 脚本已按此顺序编排);
# 内含 vue-tsc --noEmit —— 类型错误在这里就该炸,而不是等到运行时
RUN npm run build

# 用 Caddy 托管:真上域名时把 Caddyfile 的 `:8080` 换成域名,即自动申请/续期 Let's Encrypt 证书
# (见 web/Caddyfile 头注释)。要用 nginx:把下面两行换成 nginx:alpine + web/nginx.conf。
FROM caddy:2-alpine AS runtime
COPY web/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/template/dist /srv

# 非 root 运行。两件事:
#   1) 监听 8080(Caddyfile 与 compose 的端口映射与此一致)。caddy 镜像给二进制加了
#      cap_net_bind_service,所以非 root 其实也绑得上 80 —— 但那依赖文件能力还在:k8s 里
#      `capabilities.drop: ALL` 或 `allowPrivilegeEscalation: false` 一开,它就没了,容器起不来。
#      端口挪到 1024 以上就跟能力无关了,对外仍是 80/443,由端口映射或上游网关转换。
#   2) USER 写数字 UID:k8s 的 runAsNonRoot 只认数字,给用户名它判不出是不是 root。
# 不 chown:镜像把 /data/caddy 与 /config/caddy 建成 1777,就是为了任意 UID 都写得进状态。
# 将来上域名要留证书,请挂**具名卷** —— 具名卷首次挂载继承镜像目录的属主与权限,bind mount 会用宿主的盖掉。
USER 10001:10001
EXPOSE 8080
