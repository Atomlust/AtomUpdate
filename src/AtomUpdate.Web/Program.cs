using AtomUpdate.Api;
using AtomUpdate.Web.Client.Services;
using AtomBlazor;
using AtomUpdate.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Api 与 Web 单进程托管（docs/02 §2 决策 4）；ContentRoot 供 JWT 密钥文件相对路径解析
builder.Services.AddAtomUpdateApi(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddAtomBlazorComponents();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// DataProtection 在本工程仅服务于 UseAntiforgery 的 SSR 表单令牌；认证为无状态 JWT
// （无 Cookie/会话态），单实例一体化部署下令牌无需跨容器重启存活。
// ① UseEphemeralDataProtectionProvider：实际加解密走纯内存密钥（临时提供程序），
//    不写任何密钥文件，容器重建后旧令牌自然失效（ antiforgery 令牌本就短效）。
// ② 移除 DataProtectionHostedService：AddDataProtection 注册的该托管服务会在启动时
//    无条件加载默认文件系统密钥环（读/写 ~/.aspnet/DataProtection-Keys），容器内刷
//    "密钥可能不持久化 + 未配加密器"两条告警——该密钥环经①后无人使用，纯属噪音。
//    实现类型在 Internal 命名空间不可直接引用，按类型名过滤；框架若改名则过滤失配、
//    退化为仅①生效（告警回来但功能不变），届时改用日志过滤兜底。
// 若将来引入需跨重启存活的 Cookie/会话态：删②、改 PersistKeysToFileSystem 挂载卷
// 并配置证书加密（ProtectKeysWithCertificate）。
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
foreach (var hosted in builder.Services.Where(d =>
    d.ServiceType == typeof(IHostedService) &&
    d.ImplementationType?.FullName?.Contains("DataProtectionHostedService", StringComparison.Ordinal) == true).ToList())
{
    builder.Services.Remove(hosted);
}

// Auto 服务端渲染阶段的 HttpClient：回环调用本进程 API
builder.Services.AddAdminClientServices(builder.Configuration["AdminApi:BaseUrl"]);

var app = builder.Build();

// .NET 11 起 UseWebAssemblyDebugging 已废弃（ASPDEPR011）：Blazor WASM 调试改由
// Visual Studio / VS Code 直接发起，宿主侧不再需要该调用，故原 Development 分支整体移除。
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    // 默认 HSTS 30 天；生产环境如需调整见 https://aka.ms/aspnetcore-hsts
    app.UseHsts();
    // 仅非开发环境强制 http→https：dev 的 ASP.NET 证书常不受信任，
    // 且会打断 Auto 模式下的同源 API 调用（本机验收走 http，端口见 launchSettings 与 AdminApi:BaseUrl）
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// 版本文件静态分发（/uploads/{appId}/{version}/{filename}）——先于鉴权，更新包公开可下载
// Storage:Root 相对路径（如 "uploads"）基于 ContentRoot 解析为绝对路径（PhysicalFileProvider 要求绝对路径）
var uploadsRootConfig = builder.Configuration.GetValue<string>("Storage:Root");
var uploadsRoot = uploadsRootConfig is null
    ? Path.Combine(builder.Environment.ContentRootPath, "uploads")
    : Path.IsPathRooted(uploadsRootConfig)
        ? uploadsRootConfig
        : Path.Combine(builder.Environment.ContentRootPath, uploadsRootConfig);
if (!Directory.Exists(uploadsRoot))
{
    Directory.CreateDirectory(uploadsRoot);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads",
    ServeUnknownFileTypes = true
});

// 鉴权中间件 + 开发迁移 + 管理员引导（须先于 UseAntiforgery）
await app.UseAtomUpdateApiAsync();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AtomUpdate.Web.Client._Imports).Assembly);

app.MapAtomUpdateApi();

app.Run();
