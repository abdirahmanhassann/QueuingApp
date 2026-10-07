using QueueingApp.Connectivity;

namespace QueueingApp.Helpers
{
    /// <summary>
    /// Live vs demo SMS, from dbo.AppSettings.SmsLive (see Connectivity/MIGRATION_SMS_MODE.sql).
    /// Texts only go out when SmsLive = 1 AND Twilio is configured. With SmsLive = 0 nothing is sent:
    /// notification texts are skipped and customers can't get a verification code, so they can't join.
    /// Codes are never shown on screen.
    /// </summary>
    public static class SmsSettings
    {
        private static readonly TimeSpan CACHE_FOR = TimeSpan.FromSeconds(30);
        private static readonly object _lock = new();
        private static readonly TimeSpan RETRY_AFTER_ERROR = TimeSpan.FromSeconds(5);
        private static bool _live;
        private static bool _known;
        private static DateTime _readAt = DateTime.MinValue;

        public static bool TwilioConfigured =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID"))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN"))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER"));

        /// <summary>
        /// The SmsLive bit (cached 30 s). If it can't be read (a database blip), the last value read is kept; if it has
        /// never been read, it counts as live. Demo mode puts verification codes on screen, so a database hiccup must
        /// never switch a live site into it. Demo mode only happens when the table really says SmsLive = 0.
        /// </summary>
        public static bool IsLive(QADB db)
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _readAt < CACHE_FOR)
                {
                    return _live;
                }
                try
                {
                    _live = db.AppSettingsGetSmsLive();
                    _known = true;
                    _readAt = DateTime.UtcNow;
                }
                catch
                {
                    if (!_known)
                    {
                        _live = true;
                    }
                    // try again soon rather than sitting on a stale value for the full 30 s
                    _readAt = DateTime.UtcNow - CACHE_FOR + RETRY_AFTER_ERROR;
                }
                return _live;
            }
        }

        /// <summary>True when a text should really be sent through Twilio.</summary>
        public static bool CanSend(QADB db) => TwilioConfigured && IsLive(db);
    }
}
