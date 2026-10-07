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

        // Customer join flow: scan shop QR -> getshopbycode -> requestjoin (location check + SMS code) -> verifyjoin.
        // The old open joincustomer/createcustomer endpoints are gone on purpose: they let anyone join from anywhere.

        [HttpPost("getshopbycode")]
        public string GetShopByCode(QrCodeRequest req)
        {
            GenericResponse ret = _proc.GetShopByCode(req.QrCode);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("requestjoin")]
        public string RequestJoin(RequestJoinRequest req)
        {
            GenericResponse ret = _proc.RequestJoin(req.QrCode, req.Name, req.Email, req.PhoneNumber,
                req.Latitude, req.Longitude, req.Accuracy);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("verifyjoin")]
        public string VerifyJoin(VerifyJoinRequest req)
        {
            if (req.PendingJoinId is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "That request has expired. Start again." });
            }
            GenericResponse ret = _proc.VerifyJoin(id, req.Code);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("resendjoincode")]
        public string ResendJoinCode(PendingJoinIdRequest req)
        {
            if (req.PendingJoinId is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "That request has expired. Start again." });
            }
            GenericResponse ret = _proc.ResendJoinCode(id);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        [HttpPost("getqueue")]
        public string GetQueue(GetQueueRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            GenericResponse ret = _proc.GetQueue(accountId, req.Status ?? 0);
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
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            GenericResponse ret = _proc.GetQueueEntry(id, req.AccessKey);
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
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            GenericResponse ret = _proc.Serve(accountId, id);
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
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            GenericResponse ret = _proc.Cancel(accountId, id);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        // Staff: move a waiting customer to another place in the line (1 = next).
        [HttpPost("movequeueentry")]
        public string MoveQueueEntry(MoveQueueEntryRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            if (req.Position is not int position || position < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Choose a place in the line." });
            }
            GenericResponse ret = _proc.MoveInQueue(accountId, id, position);
            return JsonConvert.SerializeObject(ret, Formatting.Indented);
        }

        // Staff: text a waiting/serving customer. The message also shows on their ticket page.
        [HttpPost("pingcustomer")]
        public string PingCustomer(PingCustomerRequest req)
        {
            int accountId = _security.CheckToken(req.Token);
            if (accountId < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = TOKEN_ERROR });
            }
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            GenericResponse ret = _proc.PingCustomer(accountId, id, req.Message);
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
            if (req.Id is not int id || id < 1)
            {
                return JsonConvert.SerializeObject(new GenericResponse() { Result = false, ErrorMessage = "Queue entry not found." });
            }
            GenericResponse ret = _proc.UpdateQueueStatus(accountId, id, req.Status ?? 0);
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
