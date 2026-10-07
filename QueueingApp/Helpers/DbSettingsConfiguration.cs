using System.Data;
using QueueingApp.Connectivity;

namespace QueueingApp.Helpers
{
    /// <summary>
    /// Stripe settings from the database (dbo.AppSettings + dbo.PlanPrices), exposed as normal configuration:
    ///   Stripe:SecretKey, Stripe:WebhookSecret, Stripe:Prices:{Limited|Full}:{Monthly|Yearly}
    /// Re-read every 30 seconds, so an UPDATE/INSERT in SQL takes effect without a restart or code change.
    /// Values set in the database win; anything left empty there falls back to appsettings(.Local).json.
    /// If the database can't be read, the last values read are kept.
    /// </summary>
    public sealed class DbSettingsConfigurationSource : IConfigurationSource
    {
        private readonly string _connectionString;
        public DbSettingsConfigurationSource(string connectionString) => _connectionString = connectionString;
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new DbSettingsConfigurationProvider(_connectionString);
    }

    public sealed class DbSettingsConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private static readonly TimeSpan REFRESH_EVERY = TimeSpan.FromSeconds(30);
        private readonly string _connectionString;
        private readonly Timer _timer;

        public DbSettingsConfigurationProvider(string connectionString)
        {
            _connectionString = connectionString;
            _timer = new Timer(_ => Refresh(), null, REFRESH_EVERY, REFRESH_EVERY);
        }

        public override void Load() => Read();

        private void Refresh()
        {
            if (Read())
            {
                OnReload();
            }
        }

        /// <returns>True if the values changed.</returns>
        private bool Read()
        {
            try
            {
                DataRow row = new QADB(_connectionString, false).StripeSettingsGet();
                Dictionary<string, string?> next = new(StringComparer.OrdinalIgnoreCase);
                if (row != null)
                {
                    Put(next, "Stripe:SecretKey", row, "SecretKey");
                    Put(next, "Stripe:WebhookSecret", row, "WebhookSecret");
                    Put(next, "Stripe:Prices:Limited:Monthly", row, "LimitedMonthly");
                    Put(next, "Stripe:Prices:Limited:Yearly", row, "LimitedYearly");
                    Put(next, "Stripe:Prices:Full:Monthly", row, "FullMonthly");
                    Put(next, "Stripe:Prices:Full:Yearly", row, "FullYearly");
                }
                bool changed = next.Count != Data.Count
                    || next.Any(kv => !Data.TryGetValue(kv.Key, out string? old) || old != kv.Value);
                Data = next;
                return changed;
            }
            catch
            {
                // Database not reachable yet, or MIGRATIONS.sql not run: keep the last values (or the file settings).
                return false;
            }
        }

        /// <summary>Only non-empty database values are set, so empty ones fall back to the settings files.</summary>
        private static void Put(Dictionary<string, string?> data, string key, DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return;
            string value = (Convert.ToString(row[column]) ?? "").Trim();
            if (value.Length > 0) data[key] = value;
        }

        public void Dispose() => _timer.Dispose();
    }

    public static class DbSettingsConfigurationExtensions
    {
        public static IConfigurationBuilder AddDbSettings(this IConfigurationBuilder builder, string? connectionString)
        {
            return string.IsNullOrWhiteSpace(connectionString)
                ? builder
                : builder.Add(new DbSettingsConfigurationSource(connectionString));
        }
    }
}
