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
    public class QueueController : ControllerBase
    {
        private QueueProcessor _proc;
        private Encryptor _security;

        public QueueController(IConfiguration config)
        {
            _proc = new QueueProcessor(config);
            _security = new(config);
        }

        [HttpPost("getshops")]
        public string GetShops()
        {
            GenericResponse ret = _proc.GetShops();
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("joincustomer")]
        public string JoinCustomer(JoinCustomerRequest req)
        {
            GenericResponse ret = _proc.JoinCustomer(req.Name, req.Email, req.PhoneNumber, req.ShopId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("createcustomer")]
        public string CreateCustomer(JoinCustomerRequest req)
        {
            return JoinCustomer(req);
        }

        [HttpPost("getqueue")]
        public string GetQueue(GetQueueRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetQueue(accountId, req.Status);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("gethistory")]
        public string GetHistory(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetHistory(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("getqueueentry")]
        public string GetQueueEntry(QueueEntryIdRequest req)
        {
            GenericResponse ret = _proc.GetQueueEntry(req.Id);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("serve")]
        public string Serve(QueueEntryIdRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.Serve(accountId, req.Id);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("cancel")]
        public string Cancel(QueueEntryIdRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.Cancel(accountId, req.Id);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("updatequeuestatus")]
        public string UpdateQueueStatus(UpdateQueueStatusRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.UpdateQueueStatus(accountId, req.Id, req.Status);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("getemployees")]
        public string GetEmployees(TokenOnlyRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetEmployees(accountId);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }
    }
}
