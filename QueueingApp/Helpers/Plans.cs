using System.Data;

namespace QueueingApp.Helpers
{
    /// <summary>
    /// Two paid plans, Limited and Full, after a 14-day Full trial. There's no free plan: a shop with no live
    /// subscription and no trial left is NONE (expired) — customers can't join and no texts go out until it
    /// subscribes, though staff can still finish the people already in the queue.
    /// Paid limits default to the values below and can be overridden in appsettings
    /// ("Plans": { "Limited": { "MaxStaff": 3, ... } }). Mirrored in the frontend's src/lib/plans.ts for demo mode.
    /// </summary>
    public static class Plans
    {
        /// <summary>No plan in force (Shops.Plan = 'None' means nothing paid for).</summary>
        public const string NONE = "None";
        public const string LIMITED = "Limited";
        public const string FULL = "Full";
        /// <summary>The plans on offer.</summary>
        public static readonly string[] ALL = { LIMITED, FULL };

        /// <summary>Days a past_due subscription keeps its plan while Stripe retries the card.</summary>
        public const int GRACE_DAYS = 7;

        public sealed class Limits
        {
            /// <summary>null = unlimited.</summary>
            public int? MaxStaff { get; init; }
            /// <summary>null = unlimited. The shop's own QueueLimit can be lower.</summary>
            public int? MaxWaiting { get; init; }
            /// <summary>Texts per calendar month, verification codes included. null = unlimited.</summary>
            public int? SmsPerMonth { get; init; }
            public bool CanTextCustomers { get; init; }
            public bool CanReorderQueue { get; init; }
            public bool CanUseLearnedEta { get; init; }
            /// <summary>False when no plan is in force: no new customers, no texts.</summary>
            public bool Active { get; init; } = true;
        }

        private static readonly Dictionary<string, Limits> DEFAULTS = new()
        {
            [LIMITED] = new Limits { MaxStaff = 3, MaxWaiting = 30, SmsPerMonth = 500, CanTextCustomers = true, CanReorderQueue = true },
            [FULL] = new Limits { MaxStaff = null, MaxWaiting = null, SmsPerMonth = 2000, CanTextCustomers = true, CanReorderQueue = true, CanUseLearnedEta = true },
        };

        /// <summary>Expired: staff can still sign in and clear the queue, nothing else.</summary>
        private static readonly Limits NONE_LIMITS = new() { MaxStaff = 1, MaxWaiting = null, SmsPerMonth = 0, Active = false };

        public static Limits For(IConfiguration config, string plan)
        {
            string key = Normalise(plan);
            if (key == NONE)
            {
                return NONE_LIMITS;
            }
            Limits d = DEFAULTS[key];
            IConfigurationSection s = config.GetSection("Plans:" + key);
            if (!s.Exists())
            {
                return d;
            }
            return new Limits
            {
                MaxStaff = ReadLimit(s, "MaxStaff", d.MaxStaff),
                MaxWaiting = ReadLimit(s, "MaxWaiting", d.MaxWaiting),
                SmsPerMonth = ReadLimit(s, "SmsPerMonth", d.SmsPerMonth),
                CanTextCustomers = s.GetValue("CanTextCustomers", d.CanTextCustomers),
                CanReorderQueue = s.GetValue("CanReorderQueue", d.CanReorderQueue),
                CanUseLearnedEta = s.GetValue("CanUseLearnedEta", d.CanUseLearnedEta),
            };
        }

        /// <summary>Config value; a negative number means unlimited.</summary>
        private static int? ReadLimit(IConfigurationSection s, string key, int? fallback)
        {
            string? raw = s[key];
            if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, out int value))
            {
                return fallback;
            }
            return value < 0 ? null : value;
        }

        public static string Normalise(string? plan)
        {
            foreach (string p in ALL)
            {
                if (string.Equals(p, plan, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            return NONE;
        }

        public sealed class Effective
        {
            /// <summary>The plan whose limits apply right now (NONE once the trial and any subscription have ended).</summary>
            public string Plan { get; init; } = NONE;
            /// <summary>"Subscription", "Grace" (payment failing), "Trial" or "Expired".</summary>
            public string Source { get; init; } = "Expired";
            public bool IsActive => Plan != NONE;
            public DateTime? TrialEndsAt { get; init; }
        }

        /// <summary>Which plan is in force for a shop row (needs the columns from migration section 11).</summary>
        public static Effective EffectiveFor(DataRow shop, DateTime nowUtc)
        {
            string paid = Normalise(Col(shop, "Plan") as string);
            string status = (Col(shop, "SubscriptionStatus") as string ?? "").ToLowerInvariant();
            DateTime? periodEnd = Eta.AsUtc(Col(shop, "CurrentPeriodEnd"));
            DateTime? trialEnds = Eta.AsUtc(Col(shop, "TrialEndsAt"));

            if (paid != NONE && (status == "active" || status == "trialing"))
            {
                return new Effective { Plan = paid, Source = "Subscription", TrialEndsAt = trialEnds };
            }
            if (paid != NONE && status == "past_due" && periodEnd.HasValue && periodEnd.Value.AddDays(GRACE_DAYS) > nowUtc)
            {
                return new Effective { Plan = paid, Source = "Grace", TrialEndsAt = trialEnds };
            }
            if (trialEnds.HasValue && trialEnds.Value > nowUtc)
            {
                return new Effective { Plan = FULL, Source = "Trial", TrialEndsAt = trialEnds };
            }
            return new Effective { Plan = NONE, Source = "Expired", TrialEndsAt = trialEnds };
        }

        /// <summary>The waiting-customer cap that applies: the lower of the plan's and the shop's own (0 = none).</summary>
        public static int? WaitingCap(Limits limits, int shopQueueLimit)
        {
            int? shopCap = shopQueueLimit > 0 ? shopQueueLimit : null;
            if (limits.MaxWaiting == null) return shopCap;
            if (shopCap == null) return limits.MaxWaiting;
            return Math.Min(limits.MaxWaiting.Value, shopCap.Value);
        }

        /// <summary>Feature list sent to the frontend with the shop.</summary>
        public static Dictionary<string, object?> Describe(Limits limits)
        {
            return new Dictionary<string, object?>
            {
                ["MaxStaff"] = limits.MaxStaff,
                ["MaxWaiting"] = limits.MaxWaiting,
                ["SmsPerMonth"] = limits.SmsPerMonth,
                ["CanTextCustomers"] = limits.CanTextCustomers,
                ["CanReorderQueue"] = limits.CanReorderQueue,
                ["CanUseLearnedEta"] = limits.CanUseLearnedEta,
                ["Active"] = limits.Active,
            };
        }

        public const string INACTIVE_MESSAGE = "Your free trial has ended. Choose a plan on the Billing page to keep taking customers.";

        public static string UpgradeMessage(string feature, string needs = LIMITED)
        {
            return needs == FULL
                ? $"{feature} is on the Full plan. Upgrade on the Billing page."
                : $"{feature} is on the Limited and Full plans. Upgrade on the Billing page.";
        }

        private static object? Col(DataRow row, string name)
        {
            if (!row.Table.Columns.Contains(name) || row[name] == DBNull.Value)
            {
                return null;
            }
            return row[name];
        }
    }
}
