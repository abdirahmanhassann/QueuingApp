using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using System.Xml;
using static QueueingApp.Model.ApiRequests;
using static QueueingApp.Model.ApiResponses;
using Newtonsoft.Json;
using WCL_BE.Security;

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

        [HttpPost]
        public string Login(LoginRequest req)
        {
            GenericResponse ret = _proc.Login(req.Email, req.Password);
            return JsonConvert.SerializeObject(ret, Newtonsoft.Json.Formatting.Indented);
        }

        [HttpPost]
        public string Signup(NewAccountRequest req)
        {
            GenericResponse ret = _proc.SignUp(req.Email, req.DisplayName, req.Password, req.GymName, req.Country, req.City);
            return JsonConvert.SerializeObject(ret, Newtonsoft.Json.Formatting.Indented);
        }

        [HttpPost]
        public string GetGymForToken(TokenOnlyRequest req)
        {
            long accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetGymFromAccount(accountId);
            return JsonConvert.SerializeObject(ret, Newtonsoft.Json.Formatting.Indented);

        }

    }
}
