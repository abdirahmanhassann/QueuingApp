using System.Data;
using System.Reflection;
using QueueingApp.Connectivity;
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

        public GenericResponse SignUp(string email, string name, string password, int shopId, string? phoneNumber = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                {
                    return CreateFailureResponse("Password must be at least 8 characters.");
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
                return CreateSuccessResponseWithData(new { User = user, Shop = shop });
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
                _db.CreateShop(name, adminId, openingTime, closingTime, queueLimit, lunchBreak, qrCode);
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
