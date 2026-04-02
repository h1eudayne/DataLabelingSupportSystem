using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DAL
{
    public static class DatabaseConnectionStringResolver
    {
        public static string GetRequiredConnectionString(IConfiguration configuration)
        {
            var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                return configuredConnectionString;
            }

            var mysqlHost = configuration["MYSQLHOST"];
            var mysqlPort = configuration["MYSQLPORT"];
            var mysqlUser = configuration["MYSQLUSER"];
            var mysqlPassword = configuration["MYSQLPASSWORD"];
            var mysqlDatabase = configuration["MYSQLDATABASE"];

            if (!string.IsNullOrWhiteSpace(mysqlHost) &&
                !string.IsNullOrWhiteSpace(mysqlUser) &&
                !string.IsNullOrWhiteSpace(mysqlDatabase))
            {
                return BuildConnectionString(
                    mysqlHost,
                    mysqlPort,
                    mysqlUser,
                    mysqlPassword,
                    mysqlDatabase);
            }

            var mysqlUrl = configuration["MYSQL_URL"];
            if (!string.IsNullOrWhiteSpace(mysqlUrl) &&
                Uri.TryCreate(mysqlUrl, UriKind.Absolute, out var uri))
            {
                var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
                var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
                var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

                if (!string.IsNullOrWhiteSpace(uri.Host) &&
                    !string.IsNullOrWhiteSpace(user) &&
                    !string.IsNullOrWhiteSpace(database))
                {
                    return BuildConnectionString(
                        uri.Host,
                        uri.Port > 0 ? uri.Port.ToString() : null,
                        user,
                        password,
                        database);
                }
            }

            throw new InvalidOperationException(
                "FATAL: MySQL connection string is not configured. " +
                "Set 'ConnectionStrings:DefaultConnection'/'ConnectionStrings__DefaultConnection' " +
                "or provide Railway MySQL variables 'MYSQLHOST', 'MYSQLPORT', 'MYSQLUSER', " +
                "'MYSQLPASSWORD', 'MYSQLDATABASE'.");
        }

        private static string BuildConnectionString(
            string host,
            string? portValue,
            string user,
            string? password,
            string database)
        {
            var port = uint.TryParse(portValue, out var parsedPort) ? parsedPort : 3306u;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = port,
                UserID = user,
                Password = password,
                Database = database,
                SslMode = MySqlSslMode.Required,
                AllowUserVariables = true
            };

            return builder.ConnectionString;
        }
    }
}
