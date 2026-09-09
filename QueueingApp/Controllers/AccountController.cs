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

        [HttpPost("signup")]
        public string Signup(NewEmployeeAccountRequest req)
        {
            GenericResponse ret = _proc.SignUp(req.Email, req.Name, req.Password, req.ShopId ?? 0, req.PhoneNumber);
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
            GenericResponse ret = _proc.CreateShop(accountId, req.Name, req.AdminId, req.OpeningTime, req.ClosingTime, req.QueueLimit, req.LunchBreak, req.QrCode);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }
    }
}
