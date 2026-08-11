using System.Data;
using System.Net;
using System.Reflection;
using QueueingApp.Connectivity;
using QueueingApp.Security;
using static QueueingApp.Model.ApiResponses;
using static QueueingApp.Helpers.ApiHelper;
using static QueueingApp.Model.ApiResponses;
//using WCL_BE.Security;
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

                // ###TODO: log IP address? (for multiple account detection, new device login check)
                // ###TODO: log browser agent? (for multiple account detection, new device login check)

                DataRow dr = _db.GetAccountByEmail(email);
                if (dr == null)
                {
                    return  CreateFailureResponse("Either the email specified is not signed up, or the password is incorrect");
                }

                Encryptor enc = new(_config);
                string salt = dr.Field<string>("Salt")!;
                string pwdEncrypted = enc.Encrypt(password, salt);
                if (pwdEncrypted != dr.Field<string>("Password"))
                {
                    return CreateFailureResponse("Either the email specified is not signed up, or the password is incorrect");
                }

                string token = Guid.NewGuid().ToString();
                _db.AddAccessToken(dr.Field<int>("Id"), token);

                return CreateSuccessResponseWithData(token);
            }
            catch (Exception ex)
            {
                // We do not have a userId at this point, so log under account 0
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse SignUp(string email, string name, string password, int shopId)
        {
            try
            {
                // ###TODO check for profanity in displayName or Shop
                Encryptor enc = new(_config);
                string salt = enc.GenerateRandomSalt();
                string pwdEncrypted = enc.Encrypt(password, salt);

                _db.Signup(email, name, pwdEncrypted, salt, shopId);
                return CreateSuccessResponseNoData();
            }
            catch (Exception ex)
            {
                // We do not have a userId at this point, so log under account 0
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }
        public GenericResponse GetShopFromAccount(long accountId)
        {
            try
            {
                DataRow dr =  _db.GetShopFromAccount(accountId);
                return CreateSuccessResponseWithData(dr);
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse CreateShop(int accountId, string name, int adminId, DateTime openingTime,
            DateTime closingTime, int? queueLimit, DateTime? lunchBreak, string qrCode)
        {
            try
            {
                 _db.CreateShop(name, adminId, openingTime, closingTime,queueLimit.Value, lunchBreak.Value, qrCode);
                return CreateSuccessResponseNoData();
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }
    }
}