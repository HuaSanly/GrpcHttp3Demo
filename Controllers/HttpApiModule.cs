using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace GrpcHttp3Demo.Controllers
{
    public static class HttpApiModule
    {
        public static IServiceCollection AddHttpApiModule(this IServiceCollection services)
        {
            services.AddControllers();
            return services;
        }

        public static IEndpointRouteBuilder MapHttpApiModule(this IEndpointRouteBuilder app)
        {
            app.MapControllers();
            return app;
        }
    }
}

