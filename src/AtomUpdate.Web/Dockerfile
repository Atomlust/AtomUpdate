# AtomUpdate.Web 一体化镜像（Api + 管理后台单进程托管，docs/02 §2 决策 4）
# 构建上下文为本仓库根目录。UI 组件库走 NuGet 包 AtomBlazor（RCL，静态资源随包
# 分发、Tailwind 产物已在包内编译好），镜像内无需库源码、无需 Node 阶段：
#   docker build -f src/AtomUpdate.Web/Dockerfile -t atomupdate-web .
#   docker compose up -d --build   # compose 的 build.context 为 .
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.3 AS build
WORKDIR /src

# 先拷工程文件以利用 NuGet 还原缓存层（还原闭包：
#   Web → Web.Client / Api / Core / Abstractions / Validation；
#   UI 库经 PackageReference 从 nuget.org 还原，nuget.config 的源映射必须同拷）
COPY AtomUpdate.slnx Directory.Packages.props Directory.Build.props global.json nuget.config ./
COPY src/AtomUpdate.Web/AtomUpdate.Web.csproj src/AtomUpdate.Web/
COPY src/AtomUpdate.Web.Client/AtomUpdate.Web.Client.csproj src/AtomUpdate.Web.Client/
COPY src/AtomUpdate.Api/AtomUpdate.Api.csproj src/AtomUpdate.Api/
COPY src/AtomUpdate.Core/AtomUpdate.Core.csproj src/AtomUpdate.Core/
COPY src/AtomUpdate.Abstractions/AtomUpdate.Abstractions.csproj src/AtomUpdate.Abstractions/
COPY src/AtomUpdate.Validation/AtomUpdate.Validation.csproj src/AtomUpdate.Validation/
RUN dotnet restore src/AtomUpdate.Web/AtomUpdate.Web.csproj

COPY src/ ./src/
# Directory.Build.targets 必须入镜像：它承载剔除 AtomBlazor 与 Primitives 两包
# 同名 THIRD-PARTY-NOTICES.txt 静态资产的目标。MSBuild 按目录链自动导入该文件，
# 漏拷则镜像内目标不执行，发布清单保留重复资产路径，运行期资源集合判重抛
# InvalidOperationException、容器启动即崩（本地有该文件故开发环境不暴露）
COPY Directory.Build.targets ./
# linux-musl-x64：Alpine(musl) 底座所需原生库；UseAppHost=false 避免 NETSDK1084；同层删调试代理减体积
RUN dotnet publish src/AtomUpdate.Web/AtomUpdate.Web.csproj -c Release -r linux-musl-x64 -o /app/publish /p:UseAppHost=false \
    && rm -rf /app/publish/BlazorDebugProxy

# 运行基镜像注意：aspnet 镜像用运行时版本号（11.0.0-rc.3），SDK 镜像才用
# SDK 带号（11.0.100-rc.3）——两者不可混用（曾误写 11.0.100-rc.3-alpine3.24，
# MCR 无此标签，拉取即失败）
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.3-alpine3.24 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
RUN mkdir -p /data/uploads /app/uploads && chown $APP_UID /data /data/uploads /app/uploads
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AtomUpdate.Web.dll"]
