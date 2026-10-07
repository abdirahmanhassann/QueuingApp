using Microsoft.AspNetCore.Mvc;
using QueueingApp.Connectivity;
using QueueingApp.Helpers;
using System.Reflection;

namespace QueueingApp.Controllers
{
    /// <summary>
    /// Stripe webhook: POST /Billing/webhook. Stripe calls this (not the browser), so there's no staff token —
    /// every request is checked against the Stripe-Signature header instead.
    /// Subscribe it to: checkout.session.completed, customer.subscription.created,
    /// customer.subscription.updated, customer.subscription.deleted.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class BillingController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly QADB _db;

        public BillingController(IConfiguration config)
        {
            _config = config;
            _db = new(config.GetSection("ConnectionStrings:QADB").Get<string>()!, false);
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            string json;
            using (StreamReader reader = new(HttpContext.Request.Body))
            {
                json = await reader.ReadToEndAsync();
            }

            Stripe.Event stripeEvent;
            try
            {
                stripeEvent = StripeBilling.ParseWebhook(_config, json, Request.Headers["Stripe-Signature"]);
            }
            catch (Stripe.StripeException)
            {
                // Not signed by Stripe (or the wrong webhook secret).
                return BadRequest();
            }

            try
            {
                int? shopId = null;
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        if (stripeEvent.Data.Object is Stripe.Checkout.Session session
                            && session.Mode == "subscription" && !string.IsNullOrWhiteSpace(session.SubscriptionId))
                        {
                            int? hint = int.TryParse(session.ClientReferenceId, out int fromRef) ? fromRef : null;
                            shopId = StripeBilling.ApplySubscription(_config, _db,
                                StripeBilling.GetSubscription(_config, session.SubscriptionId), hint);
                        }
                        break;

                    case "customer.subscription.created":
                    case "customer.subscription.updated":
                    case "customer.subscription.deleted":
                        if (stripeEvent.Data.Object is Stripe.Subscription eventSub)
                        {
                            // Re-read the subscription so an out-of-order event can't roll the shop back.
                            Stripe.Subscription current = StripeBilling.GetSubscription(_config, eventSub.Id);
                            shopId = StripeBilling.ApplySubscription(_config, _db, current, null);
                        }
                        break;
                }

                _db.BillingEventsTryRecord(stripeEvent.Id, stripeEvent.Type, shopId);
                return Ok();
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    $"Stripe event {stripeEvent.Id} ({stripeEvent.Type}): {ex.Message} {ex.StackTrace}", 0);
                // 500 makes Stripe retry later.
                return StatusCode(500);
            }
        }
    }
}
