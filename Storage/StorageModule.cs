using GrpcHttp3Demo.Storage.Memory;
using GrpcHttp3Demo.Storage.Memory.Session;
using GrpcHttp3Demo.Storage.Postgres;

namespace GrpcHttp3Demo.Storage
{
    public static class StorageModule
    {
        public static IServiceCollection AddStorageModule(this IServiceCollection services)
        {
            services.AddSingleton<SessionMemoryStore>();
            services.AddSingleton<MemoryStorageCatalog>();
            services.AddSingleton(sp => PostgresConnectionOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
            services.AddSingleton<PostgresSqlSugarFactory>();
            services.AddSingleton<PostgresDatabaseInitializer>();
            services.AddSingleton<PostgresConnectionTester>();
            services.AddHostedService<PostgresDatabaseInitializationService>();
            return services;
        }
    }
}
