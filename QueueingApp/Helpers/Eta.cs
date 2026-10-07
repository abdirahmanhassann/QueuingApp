using System.Data;

namespace QueueingApp.Helpers
{
    /// <summary>
    /// Wait-time estimates. Mirrored in the frontend's src/lib/eta.ts (used by the demo/mock API) — keep in step.
    /// </summary>
    public static class Eta
    {
        public const string MODE_MANUAL = "Manual";
        public const string MODE_AVERAGE = "Average";

        /// <summary>Average mode needs this many recent services before it's trusted; until then the manual minutes are used.</summary>
        public const int MIN_SAMPLES = 5;

        /// <summary>Services shorter than this are treated as mis-taps (taken to the chair and marked served straight away).</summary>
        public const double MIN_SERVICE_MINUTES = 0.5;

        public sealed class ServiceAverage
        {
            public double? AverageMinutes { get; init; }
            /// <summary>Services used after removing outliers.</summary>
            public int SampleCount { get; init; }
            /// <summary>Services that were dropped as outliers or mis-taps.</summary>
            public int ExcludedCount { get; init; }
        }

        /// <summary>
        /// Mean service time with outliers removed by Tukey's fences: anything below Q1 − 1.5×IQR or above
        /// Q3 + 1.5×IQR (the same rule a box plot uses) is ignored. Quartiles use linear interpolation.
        /// </summary>
        public static ServiceAverage TrimmedAverage(IEnumerable<double> minutes)
        {
            List<double> all = minutes.Where(m => !double.IsNaN(m) && !double.IsInfinity(m)).ToList();
            List<double> values = all.Where(m => m >= MIN_SERVICE_MINUTES).OrderBy(m => m).ToList();
            if (values.Count == 0)
            {
                return new ServiceAverage { AverageMinutes = null, SampleCount = 0, ExcludedCount = all.Count };
            }

            double q1 = Quantile(values, 0.25);
            double q3 = Quantile(values, 0.75);
            double iqr = q3 - q1;
            double low = q1 - 1.5 * iqr;
            double high = q3 + 1.5 * iqr;
            List<double> kept = values.Where(v => v >= low && v <= high).ToList();

            return new ServiceAverage
            {
                AverageMinutes = kept.Count == 0 ? (double?)null : kept.Average(),
                SampleCount = kept.Count,
                ExcludedCount = all.Count - kept.Count
            };
        }

        /// <summary>Linear-interpolated quantile of an already-sorted list (same as Excel's PERCENTILE.INC).</summary>
        public static double Quantile(IReadOnlyList<double> sorted, double q)
        {
            if (sorted.Count == 1)
            {
                return sorted[0];
            }
            double pos = (sorted.Count - 1) * q;
            int lower = (int)Math.Floor(pos);
            int upper = (int)Math.Ceiling(pos);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (pos - lower);
        }

        /// <summary>Minutes per customer actually used for estimates, and where it came from.</summary>
        public static (int Minutes, string Source) EffectiveMinutes(string? mode, int manualMinutes, ServiceAverage average)
        {
            int manual = Math.Max(1, manualMinutes);
            if (string.Equals(mode, MODE_AVERAGE, StringComparison.OrdinalIgnoreCase)
                && average.AverageMinutes.HasValue && average.SampleCount >= MIN_SAMPLES)
            {
                return (Math.Max(1, (int)Math.Round(average.AverageMinutes.Value)), MODE_AVERAGE);
            }
            return (manual, MODE_MANUAL);
        }

        /// <summary>
        /// Minutes until a waiting customer at <paramref name="position"/> (1 = next) should be called:
        /// remaining break + what's left of the current service + one service per person ahead.
        /// Assumes one chair.
        /// </summary>
        public static int WaitMinutes(int position, int minutesPerCustomer, DateTime? servingStartedAtUtc,
            DateTime? breakUntilUtc, DateTime nowUtc)
        {
            double breakLeft = breakUntilUtc.HasValue && breakUntilUtc.Value > nowUtc
                ? (breakUntilUtc.Value - nowUtc).TotalMinutes
                : 0;

            double currentLeft = 0;
            if (servingStartedAtUtc.HasValue)
            {
                double elapsed = Math.Max(0, (nowUtc - servingStartedAtUtc.Value).TotalMinutes);
                // Running over the estimate: assume they're about done rather than going negative.
                currentLeft = Math.Max(1, minutesPerCustomer - elapsed);
            }

            double ahead = Math.Max(0, position - 1) * (double)minutesPerCustomer;
            return (int)Math.Ceiling(breakLeft + currentLeft + ahead);
        }

        /// <summary>Earliest ServingStartedAt among customers in the chair (the one most likely to finish next).</summary>
        public static DateTime? CurrentServiceStart(DataTable queue)
        {
            DateTime? earliest = null;
            foreach (DataRow row in queue.Rows)
            {
                if (Convert.ToInt32(row["Status"]) != 2 || row["ServingStartedAt"] == DBNull.Value)
                {
                    continue;
                }
                DateTime started = AsUtc(row["ServingStartedAt"])!.Value;
                if (earliest == null || started < earliest)
                {
                    earliest = started;
                }
            }
            return earliest;
        }

        /// <summary>Recent service times for a shop, outliers removed.</summary>
        public static ServiceAverage LoadAverage(Connectivity.QADB db, int shopId)
        {
            DataTable durations = db.QueueServiceDurations(shopId);
            List<double> minutes = new();
            foreach (DataRow row in durations.Rows)
            {
                minutes.Add(Convert.ToDouble(row["Seconds"]) / 60.0);
            }
            return TrimmedAverage(minutes);
        }

        /// <summary>Minutes left on a break that's still running, else 0.</summary>
        public static int BreakMinutesLeft(DateTime? breakUntilUtc, DateTime nowUtc)
        {
            return breakUntilUtc.HasValue && breakUntilUtc.Value > nowUtc
                ? (int)Math.Ceiling((breakUntilUtc.Value - nowUtc).TotalMinutes)
                : 0;
        }

        public static DateTime? AsUtc(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            return DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
        }
    }
}
