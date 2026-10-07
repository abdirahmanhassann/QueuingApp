using System.Data;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using QueueingApp.Connectivity;
using QueueingApp.Helpers;
using QueueingApp.Security;
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

        private const int MAX_OTP_ATTEMPTS = 5;
        private const int RESEND_AFTER_SECONDS = 30;
        private const double MAX_ACCURACY_ALLOWANCE_METRES = 100;

        /// <summary>Public: what the customer sees after scanning the shop's QR code.</summary>
        public GenericResponse GetShopByCode(string qrCode)
        {
            try
            {
                DataRow shop = string.IsNullOrWhiteSpace(qrCode) ? null! : _db.ShopsGetByQrCode(qrCode.Trim());
                if (shop == null)
                {
                    return CreateFailureResponse("We couldn't find a shop with that code. Check it, or scan the QR code in the shop.");
                }
                int waitingCount = Convert.ToInt32(shop["WaitingCount"]);
                int limit = WaitingCapFor(shop) ?? 0;
                bool active = PlanFor(shop).Effective.IsActive;
                WaitContext wait = LoadWaitContext(shop, Convert.ToInt32(shop["Id"]));
                return CreateSuccessResponseWithData(new
                {
                    Name = Convert.ToString(shop["Name"]),
                    WaitingCount = waitingCount,
                    QueueLimit = limit,
                    QueueFull = limit > 0 && waitingCount >= limit,
                    // Estimated wait for someone joining now (they'd be last in line).
                    EtaMinutes = WaitFor(wait, waitingCount + 1),
                    BreakMinutesLeft = wait.BreakMinutesLeft,
                    JoinsPaused = wait.JoinsPaused,
                    // No plan in force (trial over, no subscription): not taking customers online.
                    Inactive = !active,
                    OpeningTime = shop["OpeningTime"] == DBNull.Value ? null : shop["OpeningTime"],
                    ClosingTime = shop["ClosingTime"] == DBNull.Value ? null : shop["ClosingTime"],
                    JoinRadiusMetres = Convert.ToInt32(shop["JoinRadiusMetres"]),
                    LocationSet = shop["Latitude"] != DBNull.Value && shop["Longitude"] != DBNull.Value
                });
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CreateFailureResponse(GENERIC_ERROR);
            }
        }

        /// <summary>Step 1: QR code + location check + details, then text a one-time code.</summary>
        public GenericResponse RequestJoin(string qrCode, string name, string email, string phoneNumber,
            decimal? latitude, decimal? longitude, decimal? accuracy)
        {
            try
            {
                name = (name ?? "").Trim();
                email = (email ?? "").Trim();
                string phone = ToE164(phoneNumber);

                if (string.IsNullOrWhiteSpace(qrCode))
                {
                    return CreateFailureResponse("Scan the QR code in the shop to join its queue.");
                }
                if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                {
                    return CreateFailureResponse("Enter your name (up to 100 characters).");
                }
                if (!IsValidEmail(email))
                {
                    return CreateFailureResponse("Enter a valid email address.");
                }
                int phoneDigits = phone.Count(char.IsDigit);
                if (phoneDigits < 10 || phoneDigits > 15)
                {
                    return CreateFailureResponse("Enter a valid mobile number so we can text you a code.");
                }

                DataRow shop = _db.ShopsGetByQrCode(qrCode.Trim());
                if (shop == null)
                {
                    return CreateFailureResponse("We couldn't find a shop with that code. Check it, or scan the QR code in the shop.");
                }
                int shopId = Convert.ToInt32(shop["Id"]);
                string shopName = Convert.ToString(shop["Name"]) ?? "";

                if (shop["Latitude"] == DBNull.Value || shop["Longitude"] == DBNull.Value)
                {
                    return CreateFailureResponse($"{shopName} hasn't finished setting up yet. Ask a member of staff.");
                }
                if (!Geocoding.IsValidCoordinate(latitude, longitude))
                {
                    return CreateFailureResponse("We need your location to check you're at the shop. Allow location access and try again.");
                }

                double distance = Geocoding.DistanceMetres(
                    (double)latitude!.Value, (double)longitude!.Value,
                    Convert.ToDouble(shop["Latitude"]), Convert.ToDouble(shop["Longitude"]));
                double accuracyAllowance = Math.Min(Math.Max((double)(accuracy ?? 0), 0), MAX_ACCURACY_ALLOWANCE_METRES);
                int radius = Convert.ToInt32(shop["JoinRadiusMetres"]);
                if (distance > radius + accuracyAllowance)
                {
                    return CreateFailureResponse($"You need to be at {shopName} to join. You're about {DescribeDistance(distance)} away.");
                }

                if (!PlanFor(shop).Effective.IsActive)
                {
                    return CreateFailureResponse($"{shopName} isn't taking customers online right now. Ask a member of staff.");
                }

                int? queueCap = WaitingCapFor(shop);
                if (queueCap.HasValue && Convert.ToInt32(shop["WaitingCount"]) >= queueCap.Value)
                {
                    return CreateFailureResponse("This shop's queue is full right now.");
                }

                int breakLeft = Eta.BreakMinutesLeft(Eta.AsUtc(shop["BreakUntil"]), DateTime.UtcNow);
                if (breakLeft > 0 && Convert.ToBoolean(shop["BreakPausesJoins"]))
                {
                    return CreateFailureResponse($"{shopName} is on a break for about {breakLeft} more minute{(breakLeft == 1 ? "" : "s")}. Try again then.");
                }

                int code = GenerateNumericOtp();
                Encryptor enc = new(_config);
                string salt = enc.GenerateRandomSalt();
                DataRow pending = _db.PendingJoinsCreate(shopId, name, email, phone, enc.Encrypt(code.ToString("D6"), salt), salt);
                int pendingJoinId = Convert.ToInt32(pending["Id"]);

                string? sendError = SendOtp(shopId, phone, code);
                if (sendError != null)
                {
                    return CreateFailureResponse(sendError);
                }

                return CreateSuccessResponseWithData(new
                {
                    PendingJoinId = pendingJoinId,
                    ShopName = shopName,
                    PhoneHint = MaskPhone(phone),
                    ExpiresInSeconds = SecondsUntil(pending["ExpiresAt"]),
                    ResendAfterSeconds = RESEND_AFTER_SECONDS
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

        /// <summary>Step 2: check the code; only then is the customer put in the queue.</summary>
        public GenericResponse VerifyJoin(int pendingJoinId, string code)
        {
            try
            {
                DataRow pending = _db.PendingJoinsGetById(pendingJoinId);
                if (pending == null)
                {
                    return CreateFailureResponse("That request has expired. Start again.");
                }

                bool alreadyVerified = pending["VerifiedAt"] != DBNull.Value && pending["QueueEntryId"] != DBNull.Value;
                if (!alreadyVerified && SecondsUntil(pending["ExpiresAt"]) <= 0)
                {
                    return CreateFailureResponse("That code has expired. Request a new one.");
                }
                // Each guess is counted before it's checked, so many guesses sent at once can't beat the limit.
                int used = _db.PendingJoinsTakeAttempt(pendingJoinId, MAX_OTP_ATTEMPTS);
                if (used < 0)
                {
                    return CreateFailureResponse("Too many wrong attempts. Request a new code.");
                }

                // The code is checked on every call, including repeats: pending ids are sequential, so the id alone
                // must never be enough to get a ticket back.
                string digits = new string((code ?? "").Where(char.IsDigit).ToArray());
                Encryptor enc = new(_config);
                string expected = Convert.ToString(pending["OtpHash"]) ?? "";
                if (digits.Length != 6 || !KeysMatch(expected, enc.Encrypt(digits, Convert.ToString(pending["OtpSalt"])!)))
                {
                    int left = Math.Max(MAX_OTP_ATTEMPTS - used, 0);
                    return CreateFailureResponse(left > 0
                        ? $"That code isn't right. {left} {(left == 1 ? "try" : "tries")} left."
                        : "Too many wrong attempts. Request a new code.");
                }

                // Already verified (double tap / refresh): hand back the same ticket.
                if (alreadyVerified)
                {
                    DataRow existing = _db.QueueEntriesGetById(Convert.ToInt32(pending["QueueEntryId"]));
                    return CreateSuccessResponseWithData(new
                    {
                        UserId = existing == null ? 0 : Convert.ToInt32(existing["UserId"]),
                        QueueEntryId = Convert.ToInt32(pending["QueueEntryId"]),
                        AccessKey = existing == null ? "" : Convert.ToString(existing["AccessKey"]),
                        CreatedUser = false,
                        AlreadyWaiting = true,
                        Entry = QADB.RowToObject(existing)
                    });
                }

                // The queue may have filled up (or the plan changed) while they were typing the code.
                DataRow joinShop = _db.ShopsGetById(Convert.ToInt32(pending["ShopId"]));
                if (joinShop != null && !PlanFor(joinShop).Effective.IsActive)
                {
                    return CreateFailureResponse($"{joinShop["Name"]} isn't taking customers online right now. Ask a member of staff.");
                }
                int? capNow = joinShop == null ? null : WaitingCapFor(joinShop);
                if (capNow.HasValue && Convert.ToInt32(joinShop!["WaitingCount"]) >= capNow.Value)
                {
                    return CreateFailureResponse("This shop's queue filled up while you were confirming. Try again in a little while.");
                }
                if (joinShop != null)
                {
                    int breakLeft = Eta.BreakMinutesLeft(Eta.AsUtc(joinShop["BreakUntil"]), DateTime.UtcNow);
                    if (breakLeft > 0 && Convert.ToBoolean(joinShop["BreakPausesJoins"]))
                    {
                        return CreateFailureResponse($"{joinShop["Name"]} went on a break while you were confirming. Try again in about {breakLeft} minute{(breakLeft == 1 ? "" : "s")}.");
                    }
                }

                (object data, int queueEntryId) = CompleteJoin(
                    Convert.ToString(pending["Name"])!,
                    Convert.ToString(pending["Email"])!,
                    Convert.ToString(pending["PhoneNumber"])!,
                    Convert.ToInt32(pending["ShopId"]),
                    capNow ?? 0);
                _db.PendingJoinsComplete(pendingJoinId, queueEntryId);
                return CreateSuccessResponseWithData(data);
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

        public GenericResponse ResendJoinCode(int pendingJoinId)
        {
            try
            {
                int code = GenerateNumericOtp();
                Encryptor enc = new(_config);
                string salt = enc.GenerateRandomSalt();
                DataRow pending = _db.PendingJoinsResend(pendingJoinId, enc.Encrypt(code.ToString("D6"), salt), salt);
                if (pending == null)
                {
                    return CreateFailureResponse("That request has expired. Start again.");
                }
                string phone = Convert.ToString(pending["PhoneNumber"]) ?? "";

                string? sendError = SendOtp(Convert.ToInt32(pending["ShopId"]), phone, code);
                if (sendError != null)
                {
                    return CreateFailureResponse(sendError);
                }

                return CreateSuccessResponseWithData(new
                {
                    PendingJoinId = pendingJoinId,
                    PhoneHint = MaskPhone(phone),
                    ExpiresInSeconds = SecondsUntil(pending["ExpiresAt"]),
                    ResendAfterSeconds = RESEND_AFTER_SECONDS
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

        /// <summary>Puts a verified customer in the queue. Same response shape the old joincustomer returned.</summary>
        private (object Data, int QueueEntryId) CompleteJoin(string name, string email, string phoneNumber, int shopId, int waitingCap)
        {
            // The database re-checks the cap while holding the shop's lock, so the last place can't be taken twice.
            DataRow join = _db.CreateCustomerJoinQueue(name, email, phoneNumber, shopId, waitingCap);
            if (join == null)
            {
                throw new InvalidOperationException("CreateCustomerJoinQueue returned no row.");
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
                TrySms(shopId, t => t.QueueJoinedAsync(ToE164(phoneNumber), shopName, position).GetAwaiter().GetResult());
                // Joining straight in at the front: their "you've joined" text already says they're next.
                NotifyNextWaiting(shopId, skipEntryId: queueEntryId);
            }

            return (new
            {
                UserId = userId,
                QueueEntryId = queueEntryId,
                AccessKey = entry == null ? "" : Convert.ToString(entry["AccessKey"]),
                CreatedUser = createdUser,
                AlreadyWaiting = alreadyWaiting,
                Entry = QADB.RowToObject(entry)
            }, queueEntryId);
        }

        /// <summary>Null on success, otherwise a message for the customer.
        /// Demo mode (AppSettings.SmsLive = 0): nothing is sent and the code is handed back to show on screen.
        /// Live mode: the code is texted. In Development the code is also handed back if the text fails
        /// (e.g. a Twilio trial account texting an unverified number).</summary>
        /// <summary>
        /// Texts the verification code. Codes are only ever sent by text, never shown on screen, so joining needs
        /// AppSettings.SmsLive = 1 and Twilio configured. Returns an error for the customer, or null when sent.
        /// </summary>
        private string? SendOtp(int shopId, string phone, int code)
        {
            const string CANT_SEND = "This shop can't send verification texts right now. Ask a member of staff.";
            if (!SmsSettings.CanSend(_db))
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    "Verification code not sent: texts are switched off (AppSettings.SmsLive = 0) or Twilio isn't configured.", 0);
                return CANT_SEND;
            }

            TwilioHelper twilio;
            try
            {
                twilio = new();
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return CANT_SEND;
            }

            // This month's text allowance (verification codes included).
            if (!TryUseSms(shopId))
            {
                return CANT_SEND;
            }

            try
            {
                twilio.SendMessageAsync(phone, code).GetAwaiter().GetResult();
                return null;
            }
            catch (Exception ex)
            {
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                ReleaseSms(shopId); // not sent, so it doesn't count
                return "We couldn't text that number. Check it and try again.";
            }
        }

        /// <summary>Everything needed to estimate waits at one shop right now.</summary>
        private sealed record WaitContext(int MinutesPerCustomer, DateTime? ServingStartedAt, DateTime? BreakUntil,
            int BreakMinutesLeft, bool JoinsPaused);

        /// <param name="queue">The shop's current queue if already loaded (must include in-chair rows); else it's fetched.</param>
        private WaitContext LoadWaitContext(DataRow shop, int shopId, DataTable? queue = null)
        {
            Eta.ServiceAverage average = Eta.LoadAverage(_db, shopId);
            string? mode = PlanFor(shop).Limits.CanUseLearnedEta ? Convert.ToString(shop["EtaMode"]) : Eta.MODE_MANUAL;
            (int minutes, _) = Eta.EffectiveMinutes(mode, Convert.ToInt32(shop["MinutesPerCustomer"]), average);
            DataTable inChair = queue ?? _db.QueueEntriesGet(shopId, QueueStatuses.Serving);
            DateTime? breakUntil = Eta.AsUtc(shop["BreakUntil"]);
            int breakLeft = Eta.BreakMinutesLeft(breakUntil, DateTime.UtcNow);
            return new WaitContext(minutes, Eta.CurrentServiceStart(inChair), breakLeft > 0 ? breakUntil : null,
                breakLeft, breakLeft > 0 && Convert.ToBoolean(shop["BreakPausesJoins"]));
        }

        private static int WaitFor(WaitContext wait, int position)
        {
            return Eta.WaitMinutes(position, wait.MinutesPerCustomer, wait.ServingStartedAt, wait.BreakUntil, DateTime.UtcNow);
        }

        private static bool KeysMatch(string? expected, string? given)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given))
            {
                return false;
            }
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given.Trim()));
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 255)
            {
                return false;
            }
            try
            {
                return new System.Net.Mail.MailAddress(email).Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static string MaskPhone(string phone)
        {
            return phone.Length <= 4 ? phone : new string('•', phone.Length - 3) + phone.Substring(phone.Length - 3);
        }

        private static int SecondsUntil(object utcValue)
        {
            if (utcValue == null || utcValue == DBNull.Value)
            {
                return 0;
            }
            DateTime when = DateTime.SpecifyKind(Convert.ToDateTime(utcValue), DateTimeKind.Utc);
            return Math.Max((int)Math.Ceiling((when - DateTime.UtcNow).TotalSeconds), 0);
        }

        private static string DescribeDistance(double metres)
        {
            return metres >= 1000 ? $"{metres / 1000:0.#} km" : $"{Math.Round(metres / 10) * 10:0} m";
        }

        public GenericResponse GetQueue(int accountId, int status)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataTable table = _db.QueueEntriesGet(shopId, status);
                List<Dictionary<string, object?>> rows = QADB.TableToObject(table);

                // Estimated wait for each waiting customer (needs the in-chair rows even when filtering by status).
                DataTable full = status == 0 ? table : _db.QueueEntriesGet(shopId, 0);
                DataRow shop = _db.ShopsGetById(shopId);
                if (shop != null)
                {
                    WaitContext wait = LoadWaitContext(shop, shopId, full);
                    foreach (Dictionary<string, object?> row in rows)
                    {
                        if (Convert.ToInt32(row["Status"]) == QueueStatuses.Waiting && row["Position"] != null)
                        {
                            row["EtaMinutes"] = WaitFor(wait, Convert.ToInt32(row["Position"]));
                        }
                    }
                }
                return CreateSuccessResponseWithData(rows);
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

        /// <summary>Public ticket view. Needs the ticket's AccessKey (handed out once, on verifyjoin), and never returns
        /// email or phone, so ticket ids can't be enumerated to harvest customer details.</summary>
        public GenericResponse GetQueueEntry(int id, string? accessKey)
        {
            try
            {
                DataRow row = string.IsNullOrWhiteSpace(accessKey) ? null! : _db.QueueEntriesGetById(id);
                if (row == null || !KeysMatch(Convert.ToString(row["AccessKey"]), accessKey))
                {
                    return CreateFailureResponse("Queue entry not found.");
                }

                int? etaMinutes = null;
                int breakMinutesLeft = 0;
                if (Convert.ToInt32(row["Status"]) == QueueStatuses.Waiting && row["Position"] != DBNull.Value)
                {
                    int shopId = Convert.ToInt32(row["ShopId"]);
                    DataRow shop = _db.ShopsGetById(shopId);
                    if (shop != null)
                    {
                        WaitContext wait = LoadWaitContext(shop, shopId);
                        etaMinutes = WaitFor(wait, Convert.ToInt32(row["Position"]));
                        breakMinutesLeft = wait.BreakMinutesLeft;
                    }
                }

                return CreateSuccessResponseWithData(new
                {
                    EtaMinutes = etaMinutes,
                    BreakMinutesLeft = breakMinutesLeft,
                    Id = row["Id"],
                    ShopId = row["ShopId"],
                    ShopName = row["ShopName"],
                    Name = row["Name"],
                    Status = row["Status"],
                    StatusName = row["StatusName"],
                    Position = row["Position"] == DBNull.Value ? null : row["Position"],
                    JoinedAt = row["JoinedAt"],
                    ServedAt = row["ServedAt"] == DBNull.Value ? null : row["ServedAt"],
                    LastPingedAt = row["LastPingedAt"] == DBNull.Value ? null : row["LastPingedAt"],
                    LastPingMessage = row["LastPingMessage"] == DBNull.Value ? null : row["LastPingMessage"]
                });
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
                // Staff can take someone to the chair, finish them, or cancel. Nothing goes back to waiting.
                if (status != QueueStatuses.Serving && status != QueueStatuses.Served && status != QueueStatuses.Cancelled)
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
                // Taking someone to the chair, finishing or cancelling can change who's at the front.
                NotifyNextWaiting(shopId);

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

        /// <summary>Staff: move a waiting customer to another place among the waiting (1 = next).</summary>
        public GenericResponse MoveInQueue(int accountId, int id, int newPosition)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataRow existing = _db.QueueEntriesGetById(id);
                if (existing == null || Convert.ToInt32(existing["ShopId"]) != shopId)
                {
                    return CreateFailureResponse("Queue entry not found.");
                }

                DataRow shop = _db.ShopsGetById(shopId);
                if (shop != null && !PlanFor(shop).Limits.CanReorderQueue)
                {
                    return CreateFailureResponse(Plans.UpgradeMessage("Moving customers in the queue"));
                }

                DataRow updated = _db.QueueEntriesMove(id, newPosition);
                NotifyNextWaiting(shopId);
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

        public const int MAX_PING_LENGTH = 280;
        public const string DEFAULT_PING_MESSAGE = "You're nearly up - please head back to the shop now.";

        /// <summary>
        /// Staff: text a customer who is waiting or in the chair. The message is saved first (it shows on their
        /// ticket page), then sent by SMS. A failed SMS doesn't fail the call: Texted=false + SmsError says why.
        /// </summary>
        public GenericResponse PingCustomer(int accountId, int id, string? message)
        {
            try
            {
                int shopId = ShopIdForAccount(accountId);
                DataRow existing = _db.QueueEntriesGetById(id);
                if (existing == null || Convert.ToInt32(existing["ShopId"]) != shopId)
                {
                    return CreateFailureResponse("Queue entry not found.");
                }

                DataRow pingShop = _db.ShopsGetById(shopId);
                if (pingShop != null && !PlanFor(pingShop).Limits.CanTextCustomers)
                {
                    return CreateFailureResponse(Plans.UpgradeMessage("Texting customers"));
                }

                string text = string.IsNullOrWhiteSpace(message) ? DEFAULT_PING_MESSAGE : message.Trim();
                if (text.Length > MAX_PING_LENGTH)
                {
                    return CreateFailureResponse($"Keep the message under {MAX_PING_LENGTH} characters.");
                }

                DataRow updated = _db.QueueEntriesRecordPing(id, text);
                string shopName = Convert.ToString(updated["ShopName"]) ?? "";
                string phone = ToE164(Convert.ToString(updated["PhoneNumber"]));

                bool texted = false;
                string? smsError = null;
                if (!SmsSettings.IsLive(_db))
                {
                    smsError = "Texts are in demo mode, so nothing was sent (AppSettings.SmsLive = 0).";
                }
                else if (!SmsSettings.TwilioConfigured)
                {
                    smsError = "Text messages aren't set up on this server.";
                }
                else if (string.IsNullOrWhiteSpace(phone))
                {
                    smsError = "This customer has no phone number.";
                }
                else if (!TryUseSms(shopId))
                {
                    smsError = "Your shop has used this month's text allowance. Upgrade on the Billing page for more.";
                }
                else
                {
                    try
                    {
                        TwilioHelper twilio = new();
                        twilio.SendPlainMessageAsync(phone, $"{shopName}: {text}").GetAwaiter().GetResult();
                        texted = true;
                    }
                    catch (Exception ex)
                    {
                        _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                            ex.Message + " " + ex.StackTrace, accountId);
                        ReleaseSms(shopId); // not sent, so it doesn't count
                        smsError = "The text couldn't be delivered to this number.";
                    }
                }

                return CreateSuccessResponseWithData(new
                {
                    Texted = texted,
                    SmsError = smsError,
                    Entry = QADB.RowToObject(updated)
                });
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
            int shopId = Convert.ToInt32(entry["ShopId"]);
            if (status == QueueStatuses.Serving)
            {
                TrySms(shopId, t => t.NowServingAsync(phone, shopName).GetAwaiter().GetResult());
            }
            else if (status == QueueStatuses.Cancelled)
            {
                TrySms(shopId, t => t.QueueCancelledAsync(phone, shopName).GetAwaiter().GetResult());
            }
        }

        /// <summary>
        /// Texts whoever is now first in the queue "you're next" - once. The database remembers who has been told,
        /// so calling this after every queue change is safe. skipEntryId: claim without texting (they already know).
        /// </summary>
        private void NotifyNextWaiting(int shopId, int? skipEntryId = null)
        {
            DataRow next;
            try
            {
                next = _db.QueueEntriesClaimNextNotify(shopId);
            }
            catch (Exception ex)
            {
                // Best-effort, like the texts themselves (e.g. MIGRATION_NEXT_NOTIFY.sql not run yet).
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
                return;
            }
            if (next == null || (skipEntryId.HasValue && Convert.ToInt32(next["Id"]) == skipEntryId.Value))
            {
                return;
            }
            string phone = ToE164(Convert.ToString(next["PhoneNumber"]));
            string shopName = Convert.ToString(next["ShopName"]) ?? "";
            TrySms(shopId, t => t.YoureNextAsync(phone, shopName).GetAwaiter().GetResult());
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

        /// <summary>Best-effort notification text, counted against the shop's monthly allowance (skipped once it's used up).</summary>
        private void TrySms(int shopId, Action<TwilioHelper> send)
        {
            try
            {
                if (!SmsSettings.CanSend(_db))
                {
                    return; // demo mode or Twilio not configured
                }
                // Set up Twilio first so a config problem doesn't use up the shop's allowance.
                TwilioHelper twilio = new();
                if (!TryUseSms(shopId))
                {
                    return;
                }
                try
                {
                    send(twilio);
                }
                catch
                {
                    ReleaseSms(shopId); // not sent, so it doesn't count
                    throw;
                }
            }
            catch (Exception ex)
            {
                // SMS is best-effort; queue mutations must still succeed. Log it so a broken setup gets noticed.
                _db.LogError(MethodBase.GetCurrentMethod()?.Module + "/" + MethodBase.GetCurrentMethod()?.Name!,
                    ex.Message + " " + ex.StackTrace, 0);
            }
        }

        /// <summary>Plan in force for a shop row, and its limits.</summary>
        private (Plans.Effective Effective, Plans.Limits Limits) PlanFor(DataRow shop)
        {
            Plans.Effective effective = Plans.EffectiveFor(shop, DateTime.UtcNow);
            return (effective, Plans.For(_config, effective.Plan));
        }

        /// <summary>Waiting-customer cap that applies: lower of the plan's and the shop's own setting. Null = none.</summary>
        private int? WaitingCapFor(DataRow shop)
        {
            return Plans.WaitingCap(PlanFor(shop).Limits, Convert.ToInt32(shop["QueueLimit"]));
        }

        private void ReleaseSms(int shopId)
        {
            try
            {
                _db.ShopSmsRelease(shopId);
            }
            catch
            {
                // best-effort
            }
        }

        /// <summary>Uses one text from the shop's monthly allowance. False when it's used up.</summary>
        private bool TryUseSms(int shopId)
        {
            DataRow shop = _db.ShopsGetById(shopId);
            int? allowance = shop == null ? Plans.For(_config, Plans.NONE).SmsPerMonth : PlanFor(shop).Limits.SmsPerMonth;
            return _db.ShopSmsTryConsume(shopId, allowance ?? -1) == 1;
        }
    }
}
