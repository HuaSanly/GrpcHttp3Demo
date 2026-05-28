using Npgsql;

namespace GrpcHttp3Demo.Storage.Postgres
{
    public sealed class PostgresConnectionTester
    {
        private readonly PostgresConnectionOptions _options;

        public PostgresConnectionTester(PostgresConnectionOptions options)
        {
            _options = options;
        }

        public async Task<PostgresConnectionTestResult> TestAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                return PostgresConnectionTestResult.Disabled("Postgres is disabled in configuration.");
            }

            if (!_options.HasRequiredValues())
            {
                return PostgresConnectionTestResult.Disabled("Postgres configuration is incomplete. Check host, database, username, and password.");
            }

            try
            {
                await using var connection = new NpgsqlConnection(_options.BuildConnectionString());
                await connection.OpenAsync(cancellationToken);

                await using var command = new NpgsqlCommand(
                    "select current_database(), current_user, inet_server_addr()::text, inet_server_port(), version()",
                    connection);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return PostgresConnectionTestResult.Failed("Connected to PostgreSQL, but test query returned no rows.");
                }

                return PostgresConnectionTestResult.Succeeded(
                    database: reader.GetString(0),
                    username: reader.GetString(1),
                    serverAddress: reader.IsDBNull(2) ? _options.Host : reader.GetString(2),
                    serverPort: reader.IsDBNull(3) ? _options.Port : reader.GetInt32(3),
                    serverVersion: reader.GetString(4));
            }
            catch (Exception exception)
            {
                return PostgresConnectionTestResult.Failed(exception.Message);
            }
        }
    }

    public sealed class PostgresConnectionTestResult
    {
        public bool Success { get; private init; }
        public bool Enabled { get; private init; }
        public string Message { get; private init; } = string.Empty;
        public string? Database { get; private init; }
        public string? Username { get; private init; }
        public string? ServerAddress { get; private init; }
        public int? ServerPort { get; private init; }
        public string? ServerVersion { get; private init; }

        public static PostgresConnectionTestResult Disabled(string message)
        {
            return new PostgresConnectionTestResult
            {
                Enabled = false,
                Success = false,
                Message = message
            };
        }

        public static PostgresConnectionTestResult Failed(string message)
        {
            return new PostgresConnectionTestResult
            {
                Enabled = true,
                Success = false,
                Message = message
            };
        }

        public static PostgresConnectionTestResult Succeeded(string database, string username, string serverAddress, int serverPort, string serverVersion)
        {
            return new PostgresConnectionTestResult
            {
                Enabled = true,
                Success = true,
                Message = "Postgres connection succeeded.",
                Database = database,
                Username = username,
                ServerAddress = serverAddress,
                ServerPort = serverPort,
                ServerVersion = serverVersion
            };
        }
    }
}