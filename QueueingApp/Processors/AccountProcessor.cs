using System.Data;
using System.Reflection;
using QueueingApp.Connectivity;
using QueueingApp.Helpers;
using QueueingApp.Security;
using static QueueingApp.Model.ApiResponses;
using static QueueingApp.Helpers.ApiHelper;

namespace QueueingApp.Processors
{
    public class AccountProcessor
    {
        private IConfiguration _config;
        private QADB _db;

        public AccountProcessor(IConfiguration config)
        {
            _config = config;
            _db = new(config.GetSection("ConnectionStrings:QADB").Get<string>()!, false);
        }

        public GenericResponse Login(string email, string password)
        {
            try
            {
                email = (email ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    return CreateFailureResponse("Email and password are required.");
                }

                DataRow dr = _db.GetAccountByEmail(email);
                if (dr == null)
                {
                    return CreateFailureResponse("Either the email specified is not signed up, or the password is incorrect");
                }

                Encryptor enc = new(_config);
                string salt = dr.Field<string>("Salt")!;
                string pwdEncrypted = enc.Encrypt(password, salt);
                if (pwdEncrypted != dr.Field<string>("Password"))
                {
                    return CreateFailureResponse("Either the email specified is not signed up, or the password is incorrect");
                }

                int userId = dr.Field<int>("Id");
                string token = Guid.NewGuid().ToString();
                _db.AddAccessToken(userId, token);
                _db.TouchLastLogin(userId);

                return CreateSuccessResponseWithData(token);
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>
        /// callerId = signed-in employee (0 = no token). New staff go into the caller's own shop, or into a shop the
        /// caller is AdminId of. Without a token this only works while there are no employees at all (first-run setup).
        /// </summary>
        public GenericResponse SignUp(int callerId, string email, string name, string password, int? requestedShopId,
            string? phoneNumber = null)
        {
            try
            {
                email = (email ?? "").Trim();
                name = (name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                {
                    return CreateFailureResponse("Enter a name (up to 100 characters).");
                }
                if (string.IsNullOrWhiteSpace(email) || email.Length > 255 || !email.Contains('@'))
                {
                    return CreateFailureResponse("Enter a valid email address.");
                }
                if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                {
                    return CreateFailureResponse("Password must be at least 8 characters.");
                }

                int shopId;
                if (callerId < 1)
                {
                    if (_db.EmployeesCountAll() > 0)
                    {
                        return CreateFailureResponse("Only signed-in staff can add employees.");
                    }
                    if (requestedShopId is not int bootstrapShop || bootstrapShop < 1 || _db.ShopsGetById(bootstrapShop) == null)
                    {
                        return CreateFailureResponse("Choose an existing shop for the first account.");
                    }
                    shopId = bootstrapShop;
                }
                else
                {
                    DataRow caller = _db.GetUserById(callerId);
                    int callerShop = caller == null || caller["ShopId"] == DBNull.Value ? 0 : Convert.ToInt32(caller["ShopId"]);
                    shopId = requestedShopId is int r && r > 0 ? r : callerShop;
                    if (shopId < 1)
                    {
                        return CreateFailureResponse("No shop is linked to this account.");
                    }
                    if (shopId != callerShop)
                    {
                        DataRow target = _db.ShopsGetById(shopId);
                        bool isAdmin = target != null && target["AdminId"] != DBNull.Value && Convert.ToInt32(target["AdminId"]) == callerId;
                        if (!isAdmin)
                        {
                            return CreateFailureResponse("You can only add staff to your own shop.");
                        }
                    }
                }

                DataRow targetShop = _db.ShopsGetById(shopId);
                if (targetShop != null)
                {
                    Plans.Limits limits = PlanFor(targetShop).Limits;
                    if (limits.MaxStaff.HasValue && _db.EmployeesGet(shopId).Rows.Count >= limits.MaxStaff.Value)
                    {
                        return CreateFailureResponse($"Your plan allows {limits.MaxStaff.Value} staff login{(limits.MaxStaff.Value == 1 ? "" : "s")}. Upgrade on the Billing page to add more.");
                    }
                }

                if (_db.GetAccountByEmail(email) != null)
                {
                    return CreateFailureResponse("That email is already in use.");
                }

                Encryptor enc = new(_config);
                string salt = enc.GenerateRandomSalt();
                string pwdEncrypted = enc.Encrypt(password, salt);

                _db.Signup(email, name, pwdEncrypted, salt, shopId, phoneNumber);
                return CreateSuccessResponseNoData();
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse GetMe(int accountId)
        {
            try
            {
                DataRow user = _db.GetUserById(accountId);
                DataRow shop = _db.GetShopFromAccount(accountId);
                return CreateSuccessResponseWithData(new { User = QADB.RowToObject(user), Shop = shop == null ? null : ShopWithStats(shop) });
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse GetShopFromAccount(long accountId)
        {
            try
            {
                DataRow dr = _db.GetShopFromAccount(accountId);
                if (dr == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                return CreateSuccessResponseWithData(ShopWithStats(dr));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse CreateShop(int accountId, string name, int adminId, DateTime openingTime,
            DateTime closingTime, int? queueLimit, DateTime? lunchBreak, string? qrCode,
            string? address = null, string? postcode = null, decimal? latitude = null, decimal? longitude = null,
            int? joinRadiusMetres = null)
        {
            try
            {
                if (joinRadiusMetres.HasValue && (joinRadiusMetres < MIN_JOIN_RADIUS || joinRadiusMetres > MAX_JOIN_RADIUS))
                {
                    return CreateFailureResponse($"Join radius must be between {MIN_JOIN_RADIUS} and {MAX_JOIN_RADIUS} metres.");
                }

                string? normalisedPostcode = string.IsNullOrWhiteSpace(postcode) ? null : Geocoding.NormalisePostcode(postcode);
                if (Geocoding.IsValidCoordinate(latitude, longitude))
                {
                    latitude = Math.Round(latitude!.Value, 6);
                    longitude = Math.Round(longitude!.Value, 6);
                }
                else if (normalisedPostcode != null)
                {
                    (GenericResponse? error, decimal lat, decimal lng) = LookupPostcode(normalisedPostcode);
                    if (error != null)
                    {
                        return error.Value;
                    }
                    latitude = lat;
                    longitude = lng;
                }
                else
                {
                    // No location yet: the shop exists but customers can't join until staff set it.
                    latitude = null;
                    longitude = null;
                }

                // The creator is always the shop's admin (the AdminId in the request is ignored), so they can then add
                // staff to it via /Account/signup.
                _db.CreateShop(name, accountId, openingTime, closingTime, queueLimit, lunchBreak, qrCode,
                    string.IsNullOrWhiteSpace(address) ? null : address.Trim(), normalisedPostcode,
                    latitude, longitude, joinRadiusMetres);
                return CreateSuccessResponseNoData();
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }


        private const int MIN_JOIN_RADIUS = 25;
        private const int MAX_JOIN_RADIUS = 2000;

        /// <summary>Set where the shop is. Device coordinates win; otherwise the postcode is geocoded.</summary>
        public GenericResponse UpdateShopLocation(int accountId, string? address, string? postcode,
            decimal? latitude, decimal? longitude, int? joinRadiusMetres)
        {
            try
            {
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                if (joinRadiusMetres.HasValue && (joinRadiusMetres < MIN_JOIN_RADIUS || joinRadiusMetres > MAX_JOIN_RADIUS))
                {
                    return CreateFailureResponse($"Join radius must be between {MIN_JOIN_RADIUS} and {MAX_JOIN_RADIUS} metres.");
                }

                string? normalisedPostcode = string.IsNullOrWhiteSpace(postcode) ? null : Geocoding.NormalisePostcode(postcode);
                string? cleanAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
                if (cleanAddress != null && cleanAddress.Length > 300)
                {
                    return CreateFailureResponse("Address is too long (300 characters max).");
                }

                decimal lat;
                decimal lng;
                if (latitude.HasValue || longitude.HasValue)
                {
                    if (!Geocoding.IsValidCoordinate(latitude, longitude))
                    {
                        return CreateFailureResponse("Those coordinates don't look right.");
                    }
                    lat = Math.Round(latitude!.Value, 6);
                    lng = Math.Round(longitude!.Value, 6);
                }
                else if (normalisedPostcode != null)
                {
                    (GenericResponse? error, decimal foundLat, decimal foundLng) = LookupPostcode(normalisedPostcode);
                    if (error != null)
                    {
                        return error.Value;
                    }
                    lat = foundLat;
                    lng = foundLng;
                }
                else if (shop["Latitude"] != DBNull.Value && shop["Longitude"] != DBNull.Value)
                {
                    // Only the radius or address changed.
                    lat = Convert.ToDecimal(shop["Latitude"]);
                    lng = Convert.ToDecimal(shop["Longitude"]);
                }
                else
                {
                    return CreateFailureResponse("Enter the shop's postcode, or use this device's location while you're in the shop.");
                }

                DataRow updated = _db.ShopsUpdateLocation(Convert.ToInt32(shop["Id"]), cleanAddress, normalisedPostcode,
                    lat, lng, joinRadiusMetres);
                return CreateSuccessResponseWithData(ShopWithStats(updated));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>New QR code for the shop. Printed copies of the old one stop working.</summary>
        public GenericResponse RegenerateQrCode(int accountId)
        {
            try
            {
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                DataRow updated = _db.ShopsRegenerateQrCode(Convert.ToInt32(shop["Id"]));
                return CreateSuccessResponseWithData(ShopWithStats(updated));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        private const int MAX_QUEUE_LIMIT = 500;
        private const int MIN_MINUTES_PER_CUSTOMER = 1;
        private const int MAX_MINUTES_PER_CUSTOMER = 240;
        private const int MIN_BREAK_MINUTES = 5;
        private const int MAX_BREAK_MINUTES = 240;

        /// <summary>Queue limit (0 = none) and how waits are estimated.</summary>
        public GenericResponse UpdateQueueSettings(int accountId, int? queueLimit, string? etaMode, int? minutesPerCustomer)
        {
            try
            {
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }

                int limit = queueLimit ?? Convert.ToInt32(shop["QueueLimit"]);
                if (limit < 0 || limit > MAX_QUEUE_LIMIT)
                {
                    return CreateFailureResponse($"Queue limit must be between 0 (no limit) and {MAX_QUEUE_LIMIT}.");
                }

                string mode = string.IsNullOrWhiteSpace(etaMode) ? Convert.ToString(shop["EtaMode"]) ?? Eta.MODE_MANUAL : etaMode.Trim();
                if (string.Equals(mode, Eta.MODE_AVERAGE, StringComparison.OrdinalIgnoreCase))
                {
                    mode = Eta.MODE_AVERAGE;
                    if (!PlanFor(shop).Limits.CanUseLearnedEta)
                    {
                        return CreateFailureResponse(Plans.UpgradeMessage("Learning wait times from recent services", Plans.FULL));
                    }
                }
                else if (string.Equals(mode, Eta.MODE_MANUAL, StringComparison.OrdinalIgnoreCase))
                {
                    mode = Eta.MODE_MANUAL;
                }
                else
                {
                    return CreateFailureResponse("Choose how wait times are estimated.");
                }

                int minutes = minutesPerCustomer ?? Convert.ToInt32(shop["MinutesPerCustomer"]);
                if (minutes < MIN_MINUTES_PER_CUSTOMER || minutes > MAX_MINUTES_PER_CUSTOMER)
                {
                    return CreateFailureResponse($"Minutes per customer must be between {MIN_MINUTES_PER_CUSTOMER} and {MAX_MINUTES_PER_CUSTOMER}.");
                }

                DataRow updated = _db.ShopsUpdateQueueSettings(Convert.ToInt32(shop["Id"]), limit, mode, minutes);
                return CreateSuccessResponseWithData(ShopWithStats(updated));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>Start a break for <paramref name="minutes"/> (0 ends it). Customers see it and it's added to wait estimates.</summary>
        public GenericResponse SetBreak(int accountId, int minutes, bool pauseJoins)
        {
            try
            {
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                if (minutes != 0 && (minutes < MIN_BREAK_MINUTES || minutes > MAX_BREAK_MINUTES))
                {
                    return CreateFailureResponse($"A break must be between {MIN_BREAK_MINUTES} and {MAX_BREAK_MINUTES} minutes.");
                }
                DataRow updated = _db.ShopsSetBreak(Convert.ToInt32(shop["Id"]), minutes, pauseJoins);
                return CreateSuccessResponseWithData(ShopWithStats(updated));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>Shop row plus what staff need to see about wait estimates and the current break.</summary>
        private Dictionary<string, object?> ShopWithStats(DataRow shop)
        {
            Dictionary<string, object?> dict = QADB.RowToObject(shop);
            int shopId = Convert.ToInt32(shop["Id"]);
            Eta.ServiceAverage average = Eta.LoadAverage(_db, shopId);
            (Plans.Effective plan, Plans.Limits limits) = PlanFor(shop);
            string? mode = limits.CanUseLearnedEta ? Convert.ToString(shop["EtaMode"]) : Eta.MODE_MANUAL;
            (int effective, string source) = Eta.EffectiveMinutes(mode, Convert.ToInt32(shop["MinutesPerCustomer"]), average);
            int breakLeft = Eta.BreakMinutesLeft(Eta.AsUtc(shop["BreakUntil"]), DateTime.UtcNow);

            dict["AverageServiceMinutes"] = average.AverageMinutes.HasValue ? (object?)Math.Round(average.AverageMinutes.Value, 1) : null;
            dict["AverageSampleCount"] = average.SampleCount;
            dict["AverageExcludedCount"] = average.ExcludedCount;
            dict["AverageMinSamples"] = Eta.MIN_SAMPLES;
            dict["EffectiveMinutesPerCustomer"] = effective;
            dict["EtaSource"] = source;
            dict["BreakMinutesLeft"] = breakLeft;
            if (breakLeft == 0)
            {
                dict["BreakUntil"] = null; // an expired break is the same as no break
            }

            // Plan in force (Plan column = what they pay for; ActivePlan = what applies now, e.g. Full during the trial).
            dict["ActivePlan"] = plan.Plan;
            dict["PlanSource"] = plan.Source;
            dict["TrialDaysLeft"] = TrialDaysLeft(plan.TrialEndsAt);
            dict["PlanLimits"] = Plans.Describe(limits);
            dict["WaitingCap"] = Plans.WaitingCap(limits, Convert.ToInt32(shop["QueueLimit"]));
            dict.Remove("StripeCustomerId");
            dict.Remove("StripeSubscriptionId");
            return dict;
        }

        private (Plans.Effective Effective, Plans.Limits Limits) PlanFor(DataRow shop)
        {
            Plans.Effective effective = Plans.EffectiveFor(shop, DateTime.UtcNow);
            return (effective, Plans.For(_config, effective.Plan));
        }

        private static int TrialDaysLeft(DateTime? trialEndsAt)
        {
            return trialEndsAt.HasValue && trialEndsAt.Value > DateTime.UtcNow
                ? (int)Math.Ceiling((trialEndsAt.Value - DateTime.UtcNow).TotalDays)
                : 0;
        }

        // ---------------------------------------------------------------- Billing

        /// <summary>Plan, trial, usage and the plans on offer (with live Stripe prices when configured).</summary>
        public GenericResponse GetBilling(int accountId)
        {
            try
            {
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                int shopId = Convert.ToInt32(shop["Id"]);
                (Plans.Effective plan, Plans.Limits limits) = PlanFor(shop);
                string? status = shop["SubscriptionStatus"] == DBNull.Value ? null : Convert.ToString(shop["SubscriptionStatus"]);
                bool configured = StripeBilling.IsConfigured(_config);

                List<object> offered = new();
                foreach (string p in Plans.ALL)
                {
                    offered.Add(new
                    {
                        Plan = p,
                        Limits = Plans.Describe(Plans.For(_config, p)),
                        Prices = p == Plans.FREE ? null : new
                        {
                            Monthly = PriceFor(p, StripeBilling.MONTHLY),
                            Yearly = PriceFor(p, StripeBilling.YEARLY),
                        },
                    });
                }

                return CreateSuccessResponseWithData(new
                {
                    ActivePlan = plan.Plan,
                    PlanSource = plan.Source,
                    PaidPlan = Plans.Normalise(Convert.ToString(shop["Plan"])),
                    SubscriptionStatus = status,
                    BillingInterval = shop["BillingInterval"] == DBNull.Value ? null : shop["BillingInterval"],
                    CurrentPeriodEnd = shop["CurrentPeriodEnd"] == DBNull.Value ? null : shop["CurrentPeriodEnd"],
                    CancelAtPeriodEnd = Convert.ToBoolean(shop["CancelAtPeriodEnd"]),
                    TrialEndsAt = plan.TrialEndsAt,
                    TrialDaysLeft = TrialDaysLeft(plan.TrialEndsAt),
                    HasSubscription = IsLiveSubscription(shop),
                    CanManageBilling = configured && shop["StripeCustomerId"] != DBNull.Value,
                    BillingConfigured = configured,
                    Limits = Plans.Describe(limits),
                    WaitingCap = Plans.WaitingCap(limits, Convert.ToInt32(shop["QueueLimit"])),
                    Usage = new
                    {
                        Staff = _db.EmployeesGet(shopId).Rows.Count,
                        SmsThisMonth = _db.ShopSmsUsageGet(shopId),
                        Waiting = Convert.ToInt32(shop["WaitingCount"]),
                    },
                    Plans = offered,
                });
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>Starts Stripe Checkout for Limited/Full, monthly/yearly. Returns { Url } to send the browser to.</summary>
        public GenericResponse CreateCheckout(int accountId, string? plan, string? interval, string? returnUrl)
        {
            try
            {
                string chosen = Plans.Normalise(plan);
                if (chosen == Plans.FREE)
                {
                    return CreateFailureResponse("Choose the Limited or Full plan.");
                }
                string? period = StripeBilling.INTERVALS.FirstOrDefault(i => string.Equals(i, interval, StringComparison.OrdinalIgnoreCase));
                if (period == null)
                {
                    return CreateFailureResponse("Choose monthly or yearly billing.");
                }
                string? origin = SafeOrigin(returnUrl);
                if (origin == null)
                {
                    return CreateFailureResponse("Missing return address.");
                }
                if (!StripeBilling.IsConfigured(_config))
                {
                    return CreateFailureResponse("Payments aren't set up on this server yet.");
                }
                if (StripeBilling.PriceId(_config, chosen, period) == null)
                {
                    return CreateFailureResponse($"The {chosen} plan isn't available with {period.ToLowerInvariant()} billing yet.");
                }

                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                if (IsLiveSubscription(shop))
                {
                    return CreateFailureResponse("You already have a subscription. Use \"Manage billing\" to change plan, billing period or card.");
                }

                DataRow user = _db.GetUserById(accountId);
                string? email = user == null ? null : Convert.ToString(user["Email"]);
                string url = StripeBilling.CreateCheckoutUrl(_config, _db, shop, email, chosen, period, origin);
                return CreateSuccessResponseWithData(new { Url = url });
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse("Couldn't start checkout. Try again in a moment.");
            }
        }

        /// <summary>Stripe customer portal: change plan or period, update card, cancel, invoices. Returns { Url }.</summary>
        public GenericResponse CreateBillingPortal(int accountId, string? returnUrl)
        {
            try
            {
                string? origin = SafeOrigin(returnUrl);
                if (origin == null)
                {
                    return CreateFailureResponse("Missing return address.");
                }
                if (!StripeBilling.IsConfigured(_config))
                {
                    return CreateFailureResponse("Payments aren't set up on this server yet.");
                }
                DataRow shop = _db.GetShopFromAccount(accountId);
                if (shop == null)
                {
                    return CreateFailureResponse("No shop is linked to this account.");
                }
                if (shop["StripeCustomerId"] == DBNull.Value)
                {
                    return CreateFailureResponse("There's no billing account yet. Choose a plan first.");
                }
                string url = StripeBilling.CreatePortalUrl(_config, Convert.ToString(shop["StripeCustomerId"])!, origin);
                return CreateSuccessResponseWithData(new { Url = url });
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse("Couldn't open billing. Try again in a moment.");
            }
        }

        private object? PriceFor(string plan, string interval)
        {
            if (StripeBilling.PriceId(_config, plan, interval) == null)
            {
                return null;
            }
            StripeBilling.PriceInfo? info = StripeBilling.GetPrice(_config, plan, interval);
            return new { Amount = info?.UnitAmount, Currency = info?.Currency };
        }

        private static bool IsLiveSubscription(DataRow shop)
        {
            if (shop["StripeSubscriptionId"] == DBNull.Value)
            {
                return false;
            }
            string status = (Convert.ToString(shop["SubscriptionStatus"]) ?? "").ToLowerInvariant();
            return status is "active" or "trialing" or "past_due" or "unpaid" or "incomplete";
        }

        /// <summary>"https://line.example/anything" -> "https://line.example". Null if not an http(s) URL.</summary>
        private static string? SafeOrigin(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }
            return $"{uri.Scheme}://{uri.Authority}";
        }

        private (GenericResponse? Error, decimal Latitude, decimal Longitude) LookupPostcode(string postcode)
        {
            try
            {
                (decimal Latitude, decimal Longitude)? found = Geocoding.LookupPostcodeAsync(postcode).GetAwaiter().GetResult();
                if (found == null)
                {
                    return (CreateFailureResponse($"We couldn't find the postcode {postcode}. Check it, or use this device's location instead."), 0, 0);
                }
                return (null, found.Value.Latitude, found.Value.Longitude);
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return (CreateFailureResponse("Postcode lookup isn't available right now. Use this device's location instead."), 0, 0);
            }
        }
    }
}
