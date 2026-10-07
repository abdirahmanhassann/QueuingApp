using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using QueueingApp.Processors;
using QueueingApp.Security;
using static QueueingApp.Helpers.ApiHelper;
using static QueueingApp.Model.ApiRequests;
using static QueueingApp.Model.ApiResponses;

namespace QueueingApp.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AccountController : ControllerBase
    {
        private AccountProcessor _proc;
        private Encryptor _security;

        public AccountController(IConfiguration config)
        {
            _proc = new AccountProcessor(config);
            _security = new(config);
        }

        [HttpPost("login")]
        public string Login(LoginRequest req)
        {
            GenericResponse ret = _proc.Login(req.Email, req.Password);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        // Public: "Register your shop". Creates a shop and its owner's login, then returns a session token.
        // Limited to a few sign-ups per network address per hour so it can't be used to mass-create trials.
        [HttpPost("registershop")]
        public string RegisterShop(RegisterShopRequest req)
        {
            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!RegistrationLimiter.TryTake(ip))
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false,
                    ErrorMessage = "Too many sign-ups from this network. Try again in an hour." });
            }
            GenericResponse ret = _proc.RegisterShop(req.Name, req.Email, req.Password, req.PhoneNumber,
                req.ShopName, req.Address, req.Postcode);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        private static class RegistrationLimiter
        {
            private const int PER_HOUR = 5;
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

            public static bool TryTake(string key)
            {
                Queue<DateTime> q = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
                lock (q)
                {
                    DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                    while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
                    if (q.Count >= PER_HOUR) return false;
                    q.Enqueue(DateTime.UtcNow);
                    return true;
                }
            }
        }

        // Staff only: the caller's token decides which shop(s) they may add people to.
        // With no token, this works exactly once — to create the very first employee on an empty system.
        [HttpPost("signup")]
        public string Signup(NewEmployeeAccountRequest req)
        {
            int accountId = string.IsNullOrWhiteSpace(req.Token) ? 0 : _security.CheckToken(req.Token);
            if (!string.IsNullOrWhiteSpace(req.Token) && accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.SignUp(accountId, req.Email, req.Name, req.Password, req.ShopId, req.PhoneNumber);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("me")]
        public string Me(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetMe(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("getshopfortoken")]
        public string GetShopForToken(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetShopFromAccount(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("CreateShop")]
        public string CreateShop(NewShopRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.CreateShop(accountId, req.Name, req.AdminId, req.OpeningTime, req.ClosingTime, req.QueueLimit, req.LunchBreak, req.QrCode,
                req.Address, req.Postcode, req.Latitude, req.Longitude, req.JoinRadiusMetres);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("updateshoplocation")]
        public string UpdateShopLocation(UpdateShopLocationRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.UpdateShopLocation(accountId, req.Address, req.Postcode, req.Latitude, req.Longitude, req.JoinRadiusMetres);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("updatequeuesettings")]
        public string UpdateQueueSettings(QueueSettingsRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.UpdateQueueSettings(accountId, req.QueueLimit, req.EtaMode, req.MinutesPerCustomer);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("startbreak")]
        public string StartBreak(BreakRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            if (req.Minutes is not int minutes || minutes < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Choose how long the break is." });
            }
            GenericResponse ret = _proc.SetBreak(accountId, minutes, req.PauseJoins ?? false);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("endbreak")]
        public string EndBreak(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.SetBreak(accountId, 0, false);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("getbilling")]
        public string GetBilling(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetBilling(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("createcheckout")]
        public string CreateCheckout(CheckoutRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.CreateCheckout(accountId, req.Plan, req.Interval, req.ReturnUrl);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("billingportal")]
        public string BillingPortal(BillingPortalRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.CreateBillingPortal(accountId, req.ReturnUrl);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("regenerateqrcode")]
        public string RegenerateQrCode(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.RegenerateQrCode(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }
    }
}
