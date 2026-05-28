using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace GrpcHttp3Demo.Controllers
{
    public static class HttpApiModule
    {
        public static IServiceCollection AddHttpApiModule(this IServiceCollection services)
        {
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                var bearerScheme = new OpenApiSecurityScheme
                {
                    Description = "输入 Bearer token，例如：Bearer {token}",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                };

                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "GrpcHttp3Demo HTTP API",
                    Version = "v1",
                    Description = "用于调试和联调当前后端 HTTP API 的 Swagger UI。"
                });

                options.AddSecurityDefinition("Bearer", bearerScheme);
            });
            return services;
        }

        public static WebApplication UseHttpApiModule(this WebApplication app)
        {
            var docsEnabled = app.Configuration.GetValue("ApiDocs:Enabled", true);
            if (!docsEnabled)
            {
                return app;
            }

            var swaggerRoutePrefix = app.Configuration["ApiDocs:SwaggerRoutePrefix"] ?? "swagger";
            var scalarRoutePrefix = app.Configuration["ApiDocs:ScalarRoutePrefix"] ?? "scalar";
            var documentTitle = app.Configuration["ApiDocs:DocumentTitle"] ?? "GrpcHttp3Demo API";
            var openApiDocumentPath = $"/{swaggerRoutePrefix}/v1/swagger.json";

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = swaggerRoutePrefix;
                options.DocumentTitle = documentTitle;
                options.SwaggerEndpoint(openApiDocumentPath, documentTitle);
                options.DisplayRequestDuration();
            });

            app.MapScalarApiReference($"/{scalarRoutePrefix}", options =>
            {
                options
                    .WithTitle(documentTitle)
                    .WithTheme(ScalarTheme.BluePlanet)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .WithOpenApiRoutePattern(openApiDocumentPath);
            });

            return app;
        }

        public static IEndpointRouteBuilder MapHttpApiModule(this IEndpointRouteBuilder app)
        {
            app.MapControllers();
            return app;
        }
    }
}

