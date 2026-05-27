namespace GrpcHttp3Demo.Authentication
{
    public static class AuthenticationModule
    {
        public static IServiceCollection AddAuthenticationModule(this IServiceCollection services)
        {
            services.AddSingleton<ClientAuthService>();
            return services;
        }
    }
}