using System.Collections.Concurrent;
using System.Data;
using QueueingApp.Connectivity;
using Stripe;

namespace QueueingApp.Helpers
{
    /// <summary>
    /// Everything that talks to Stripe. Config (keep the secrets in user secrets / environment, not appsettings.json):
    ///   Stripe:SecretKey                    sk_test_... / sk_live_...
    ///   Stripe:WebhookSecret                whsec_...
    ///   Stripe:Prices:Limited:Monthly       price_...   (and :Yearly, and the same for Full)
    /// </summary>
    public static class StripeBilling
    {
        public const string MONTHLY = "Monthly";
        public const string YEARLY = "Yearly";
        public static readonly string[] PAID_PLANS = { Plans.LIMITED, Plans.FULL };
        public static readonly string[] INTERVALS = { MONTHLY, YEARLY };

        public static bool IsConfigured(IConfiguration config)
        {
            return !string.IsNullOrWhiteSpace(config["Stripe:SecretKey"])
                   && PAID_PLANS.Any(p => INTERVALS.Any(i => !string.IsNullOrWhiteSpace(PriceId(config, p, i))));
        }

        public static string? PriceId(IConfiguration config, string plan, string interval)
        {
            string? id = config[$"Stripe:Prices:{plan}:{interval}"];
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        /// <summary>Which of our plans/intervals a Stripe price id is. Null if it's not one we know.</summary>
        public static (string Plan, string Interval)? PlanForPrice(IConfiguration config, string? priceId)
        {
            if (string.IsNullOrWhiteSpace(priceId)) return null;
            foreach (string plan in PAID_PLANS)
            {
                foreach (string interval in INTERVALS)
                {
                    if (string.Equals(PriceId(config, plan, interval), priceId, StringComparison.Ordinal))
                    {
                        return (plan, interval);
                    }
                }
            }
            return null;
        }

        private static StripeClient Client(IConfiguration config)
        {
            return new StripeClient(config["Stripe:SecretKey"]);
        }

        public sealed record PriceInfo(long? UnitAmount, string Currency);

        private static readonly ConcurrentDictionary<string, (PriceInfo Info, DateTime Fetched)> _priceCache = new();

        /// <summary>Amount and currency for a configured price (cached 10 minutes). Null if not configured or unreachable.</summary>
        public static PriceInfo? GetPrice(IConfiguration config, string plan, string interval)
        {
            string? id = PriceId(config, plan, interval);
            if (id == null || string.IsNullOrWhiteSpace(config["Stripe:SecretKey"])) return null;
            if (_priceCache.TryGetValue(id, out var cached) && cached.Fetched > DateTime.UtcNow.AddMinutes(-10))
            {
                return cached.Info;
            }
            try
            {
                Price price = new PriceService(Client(config)).Get(id);
                PriceInfo info = new(price.UnitAmount, (price.Currency ?? "gbp").ToUpperInvariant());
                _priceCache[id] = (info, DateTime.UtcNow);
                return info;
            }
            catch (StripeException)
            {
                return null;
            }
        }

        /// <summary>Stripe-hosted checkout page for a new subscription. Returns its URL.</summary>
        public static string CreateCheckoutUrl(IConfiguration config, QADB db, DataRow shop, string? staffEmail,
            string plan, string interval, string returnOrigin)
        {
            string priceId = PriceId(config, plan, interval)
                ?? throw new InvalidOperationException($"No Stripe price configured for {plan} {interval}.");
            StripeClient client = Client(config);
            int shopId = Convert.ToInt32(shop["Id"]);

            string? customerId = shop["StripeCustomerId"] == DBNull.Value ? null : Convert.ToString(shop["StripeCustomerId"]);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                Customer customer = new CustomerService(client).Create(new CustomerCreateOptions
                {
                    Email = staffEmail,
                    Name = Convert.ToString(shop["Name"]),
                    Metadata = new Dictionary<string, string> { ["shopId"] = shopId.ToString() },
                });
                customerId = customer.Id;
                db.ShopsSetStripeCustomer(shopId, customerId);
            }

            Stripe.Checkout.Session session = new Stripe.Checkout.SessionService(client).Create(new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                ClientReferenceId = shopId.ToString(),
                LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
                {
                    new() { Price = priceId, Quantity = 1 },
                },
                SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { ["shopId"] = shopId.ToString() },
                },
                AllowPromotionCodes = true,
                SuccessUrl = $"{returnOrigin}/app/billing?checkout=success",
                CancelUrl = $"{returnOrigin}/app/billing?checkout=cancelled",
            });
            return session.Url;
        }

        /// <summary>Stripe's customer portal (change plan, update card, cancel, invoices). Returns its URL.</summary>
        public static string CreatePortalUrl(IConfiguration config, string customerId, string returnOrigin)
        {
            Stripe.BillingPortal.Session session = new Stripe.BillingPortal.SessionService(Client(config)).Create(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = customerId,
                    ReturnUrl = $"{returnOrigin}/app/billing",
                });
            return session.Url;
        }

        /// <summary>Verifies the Stripe-Signature header. Throws StripeException if it's not from Stripe.</summary>
        public static Event ParseWebhook(IConfiguration config, string json, string? signature)
        {
            string secret = config["Stripe:WebhookSecret"]
                ?? throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");
            return EventUtility.ConstructEvent(json, signature ?? "", secret, throwOnApiVersionMismatch: false);
        }

        public static Subscription GetSubscription(IConfiguration config, string subscriptionId)
        {
            return new SubscriptionService(Client(config)).Get(subscriptionId);
        }

        /// <summary>
        /// Copies a subscription's current state onto its shop. Always pass a freshly fetched subscription so an
        /// out-of-order webhook can't roll the shop back to an older state. Returns the shop id, or null if unknown.
        /// </summary>
        public static int? ApplySubscription(IConfiguration config, QADB db, Subscription sub, int? shopIdHint)
        {
            int? shopId = shopIdHint;
            if (shopId == null && sub.Metadata != null && sub.Metadata.TryGetValue("shopId", out string? raw)
                && int.TryParse(raw, out int fromMeta))
            {
                shopId = fromMeta;
            }
            if (shopId == null && !string.IsNullOrWhiteSpace(sub.CustomerId))
            {
                DataRow byCustomer = db.ShopsGetByStripeCustomer(sub.CustomerId);
                if (byCustomer != null) shopId = Convert.ToInt32(byCustomer["Id"]);
            }
            if (shopId == null)
            {
                return null;
            }

            SubscriptionItem? item = sub.Items?.Data?.FirstOrDefault();
            (string Plan, string Interval)? mapped = PlanForPrice(config, item?.Price?.Id);
            string status = sub.Status ?? "incomplete";
            // A price we don't recognise (e.g. edited in the dashboard) can't grant a plan.
            string plan = mapped?.Plan ?? Plans.FREE;
            // Since API 2025-03-31 the billing period lives on the subscription item, not the subscription.
            DateTime? periodEnd = item == null ? null : DateTime.SpecifyKind(item.CurrentPeriodEnd, DateTimeKind.Utc);

            db.ShopsUpdateSubscription(shopId.Value, sub.CustomerId ?? "", sub.Id, plan, status, mapped?.Interval,
                periodEnd, sub.CancelAtPeriodEnd);
            return shopId;
        }
    }
}
