using Npgsql;

namespace GrpcHttp3Demo.Storage.Postgres
{
    public sealed class PostgresConnectionOptions
    {
        public bool Enabled { get; init; }
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 5432;
        public string Database { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string SslMode { get; init; } = "Disable";
        public int TimeoutSeconds { get; init; } = 5;

        public static PostgresConnectionOptions FromConfiguration(IConfiguration configuration)
        {
            return new PostgresConnectionOptions
            {
                Enabled = configuration.GetValue<bool>("Postgres:Enabled", false),
                Host = configuration["Postgres:Host"] ?? string.Empty,
                Port = Math.Max(1, configuration.GetValue<int?>("Postgres:Port") ?? 5432),
                Database = configuration["Postgres:Database"] ?? string.Empty,
                Username = configuration["Postgres:Username"] ?? string.Empty,
                Password = configuration["Postgres:Password"] ?? string.Empty,
                SslMode = configuration["Postgres:SslMode"] ?? "Disable",
                TimeoutSeconds = Math.Max(1, configuration.GetValue<int?>("Postgres:TimeoutSeconds") ?? 5)
            };
        }

        public bool HasRequiredValues()
        {
            return Enabled
                && !string.IsNullOrWhiteSpace(Host)
                && !string.IsNullOrWhiteSpace(Database)
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password);
        }

        public string BuildConnectionString()
        {
            var sslMode = Enum.TryParse<SslMode>(SslMode, true, out var parsedSslMode)
                ? parsedSslMode
                : Npgsql.SslMode.Disable;

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password,
                Timeout = TimeoutSeconds,
                CommandTimeout = TimeoutSeconds,
                SslMode = sslMode
            };

            return builder.ConnectionString;
        }
    }
}