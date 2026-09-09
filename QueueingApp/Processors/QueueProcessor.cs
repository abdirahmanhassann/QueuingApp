using System.Data;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using QueueingApp.Connectivity;
using static QueueingApp.Helpers.ApiHelper;
using static QueueingApp.Model.ApiResponses;
using static QueueingApp.Model.Model;

namespace QueueingApp.Processors
{
    public class QueueProcessor
    {
        private IConfiguration _config;
        private QADB _db;

        public QueueProcessor(IConfiguration config)
        {
            _config = config;
            _db = new(config.GetSection("ConnectionStrings:QADB").Get<string>()!, false);
        }

        public GenericResponse GetShops()
        {
            try
            {
                DataTable table = _db.ShopsGet();
                return CreateSuccessResponseWithData(QADB.TableToObject(table));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse JoinCustomer(string name, string email, string phoneNumber, int shopId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || shopId < 1)
                {
                    return CreateFailureResponse("Name, email and shop are required.");
                }

                DataRow join = _db.CreateCustomerJoinQueue(name.Trim(), email.Trim(), phoneNumber?.Trim() ?? "", shopId);
                if (join == null)
                {
                    return CreateFailureResponse(GENERIC_ERROR);
                }

                int queueEntryId = Convert.ToInt32(join["QueueEntryId"]);
                int userId = Convert.ToInt32(join["UserId"]);
                bool createdUser = Convert.ToBoolean(join["CreatedUser"]);
                bool alreadyWaiting = Convert.ToBoolean(join["AlreadyWaiting"]);
                DataRow entry = _db.QueueEntriesGetById(queueEntryId);

                if (!alreadyWaiting)
                {
                    int? position = entry == null || entry["Position"] == DBNull.Value ? null : Convert.ToInt32(entry["Position"]);
                    string shopName = entry == null ? "" : Convert.ToString(entry["ShopName"]) ?? "";
                    TrySms(t => t.QueueJoinedAsync(ToE164(phoneNumber), shopName, position).GetAwaiter().GetResult());
                }

                return CreateSuccessResponseWithData(new
                {
                    UserId = userId,
                    QueueEntryId = queueEntryId,
                    CreatedUser = createdUser,
                    AlreadyWaiting = alreadyWaiting,
                    Entry = QADB.RowToObject(entry)
                });
            }
            catch (SqlException ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(string.IsNullOrWhiteSpace(ex.Message) ? GENERIC_ERROR : ex.Message);
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse GetQueue(int accountId, int status)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataTable table = _db.QueueEntriesGet(shopId, status);
                return CreateSuccessResponseWithData(QADB.TableToObject(table));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(string.IsNullOrWhiteSpace(ex.Message) ? GENERIC_ERROR : ex.Message);
            }
        }

        public GenericResponse GetHistory(int accountId)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataTable table = _db.QueueEntriesGetHistory(shopId);
                return CreateSuccessResponseWithData(QADB.TableToObject(table));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(string.IsNullOrWhiteSpace(ex.Message) ? GENERIC_ERROR : ex.Message);
            }
        }

        public GenericResponse GetQueueEntry(int id)
        {
            try
            {
                DataRow row = _db.QueueEntriesGetById(id);
                if (row == null)
                {
                    return CreateFailureResponse("Queue entry not found.");
                }
                return CreateSuccessResponseWithData(QADB.RowToObject(row));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        public GenericResponse Serve(int accountId, int id)
        {
            return UpdateQueueStatus(accountId, id, QueueStatuses.Serving);
        }

        public GenericResponse Cancel(int accountId, int id)
        {
            return UpdateQueueStatus(accountId, id, QueueStatuses.Cancelled);
        }

        public GenericResponse UpdateQueueStatus(int accountId, int id, int status)
        {
            try
            {
                if (status < QueueStatuses.Waiting || status > QueueStatuses.Cancelled)
                {
                    return CreateFailureResponse("Unknown queue status.");
                }

                int shopId = ShopIdForAccount(accountId);
                DataRow existing = _db.QueueEntriesGetById(id);
                if (existing == null || Convert.ToInt32(existing["ShopId"]) != shopId)
                {
                    return CreateFailureResponse("Queue entry not found.");
                }

                DataRow updated = _db.QueueEntriesUpdateStatus(id, status);
                NotifyStatusSms(updated);

                if (status == QueueStatuses.Serving)
                {
                    NotifyNextWaiting(shopId);
                }

                return CreateSuccessResponseWithData(QADB.RowToObject(updated));
            }
            catch (SqlException ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(string.IsNullOrWhiteSpace(ex.Message) ? GENERIC_ERROR : ex.Message);
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(string.IsNullOrWhiteSpace(ex.Message) ? GENERIC_ERROR : ex.Message);
            }
        }

        public GenericResponse GetEmployees(int accountId)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataTable table = _db.EmployeesGet(shopId);
                return CreateSuccessResponseWithData(QADB.TableToObject(table));
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, accountId);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        private int ShopIdForAccount(int accountId)
        {
            DataRow user = _db.GetUserById(accountId);
            if (user == null || user["ShopId"] == DBNull.Value)
            {
                throw new InvalidOperationException("No shop is linked to this account.");
            }
            return Convert.ToInt32(user["ShopId"]);
        }

        private void NotifyStatusSms(DataRow entry)
        {
            if (entry == null)
            {
                return;
            }
            string phone = ToE164(Convert.ToString(entry["PhoneNumber"]));
            string shopName = Convert.ToString(entry["ShopName"]) ?? "";
            int status = Convert.ToInt32(entry["Status"]);
            if (status == QueueStatuses.Serving)
            {
                TrySms(t => t.NowServingAsync(phone, shopName).GetAwaiter().GetResult());
            }
            else if (status == QueueStatuses.Cancelled)
            {
                TrySms(t => t.QueueCancelledAsync(phone, shopName).GetAwaiter().GetResult());
            }
        }

        private void NotifyNextWaiting(int shopId)
        {
            DataTable waiting = _db.QueueEntriesGet(shopId, QueueStatuses.Waiting);
            if (waiting.Rows.Count == 0)
            {
                return;
            }
            DataRow next = waiting.Rows[0];
            string phone = ToE164(Convert.ToString(next["PhoneNumber"]));
            string shopName = Convert.ToString(next["ShopName"]) ?? "";
            TrySms(t => t.YoureNextAsync(phone, shopName).GetAwaiter().GetResult());
        }

        private static string ToE164(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return "";
            }
            string trimmed = phone.Trim();
            StringBuilder digits = new();
            foreach (char c in trimmed)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                }
            }
            string value = digits.ToString();
            if (trimmed.StartsWith("+"))
            {
                return "+" + value;
            }
            if (value.StartsWith("0") && value.Length == 11)
            {
                return "+44" + value.Substring(1);
            }
            if (value.StartsWith("44"))
            {
                return "+" + value;
            }
            return string.IsNullOrEmpty(value) ? "" : "+" + value;
        }

        private static void TrySms(Action<TwilioHelper> send)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID")))
                {
                    return;
                }
                TwilioHelper twilio = new();
                send(twilio);
            }
            catch
            {
                // SMS is best-effort; queue mutations must still succeed.
            }
        }
    }
}
