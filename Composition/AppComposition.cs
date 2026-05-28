using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Communication.Grpc;
using GrpcHttp3Demo.Communication.Udp;
using GrpcHttp3Demo.Communication.WebSockets;
using GrpcHttp3Demo.Controllers;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Storage;
using GrpcHttp3Demo.Utils;

namespace GrpcHttp3Demo.Composition
{
    /// <summary>
    /// 组合根（Composition Root）
    /// 只负责：决定“模块级顺序”，以及把 Builder/App 的配置集中管理。
    /// 不做反射扫描，不让业务类承担注册职责。
    /// </summary>
    public static class AppComposition
    {
        /// <summary>
        /// Builder 阶段：注册依赖注入（模块级顺序在这里显式体现）
        /// </summary>
        public static WebApplicationBuilder ConfigureModules(this WebApplicationBuilder builder)
        {
            // 一级顺序：模块注入顺序（显式、可控）
            builder.Services
                .AddGrpcModule()          // gRPC + HttpContextAccessor
                .AddAuthenticationModule() // HTTP admin/client auth
                .AddStorageModule()       // 内存存储与后续持久化入口
                .AddSessionsModule()      // 会话域
                .AddUdpModule()           // UDP 端点绑定与映射救援
                .AddHttpApiModule();      // HTTP API Controllers

            return builder;
        }

        /// <summary>
        /// App 阶段：配置中间件与路由（模块级顺序在这里显式体现）
        /// </summary>
        public static WebApplication ConfigurePipeline(this WebApplication app)
        {
            // 一级顺序：中间件顺序（显式、可控）
            app.UseMiddlewareModule();

            // 一级顺序：路由/端点映射顺序（显式、可控）
            app.MapRoutesModule();

            return app;
        }

        /// <summary>
        /// 中间件模块：只放 app.UseXXX 这类管道配置。
        /// </summary>
        public static WebApplication UseMiddlewareModule(this WebApplication app)
        {
            app
                .UseAppConfigModule()
                .UseWebSocketsModule()
                .UseHttpApiModule();

            return app;
        }

        /// <summary>
        /// 路由模块：只放 app.MapXXX 这类端点映射。
        /// </summary>
        public static WebApplication MapRoutesModule(this WebApplication app)
        {
            var docsEnabled = app.Configuration.GetValue("ApiDocs:Enabled", true);
            var defaultUi = app.Configuration["ApiDocs:DefaultUi"] ?? "scalar";
            var docsRoutePrefix = string.Equals(defaultUi, "swagger", StringComparison.OrdinalIgnoreCase)
                ? (app.Configuration["ApiDocs:SwaggerRoutePrefix"] ?? "swagger")
                : (app.Configuration["ApiDocs:ScalarRoutePrefix"] ?? "scalar");

            app
                .MapGrpcModule()
                .MapWebSocketsModule()
                .MapHttpApiModule();

            // 其它非模块化的简单路由也可以留在这里
            app.MapGet("/", () =>
            {
                if (docsEnabled)
                {
                    return Results.Redirect($"/{docsRoutePrefix}");
                }

                return Results.Text("Communication with gRPC endpoints must be made through a gRPC client.");
            });

            return app;
        }
    }
}

