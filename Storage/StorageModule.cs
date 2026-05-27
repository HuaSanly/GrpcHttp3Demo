using GrpcHttp3Demo.Storage.Memory;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Storage
{
    public static class StorageModule
    {
        public static IServiceCollection AddStorageModule(this IServiceCollection services)
        {
            services.AddSingleton<SessionMemoryStore>();
            services.AddSingleton<MemoryStorageCatalog>();
            return services;
        }
    }
}
