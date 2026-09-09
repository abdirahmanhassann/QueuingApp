namespace QueueingApp.Model
{
    public class Model
    {
        public static class QueueStatuses
        {
            public const int Waiting = 1;
            public const int Serving = 2;
            public const int Served = 3;
            public const int Cancelled = 4;
        }

        public static class UserRoles
        {
            public const string Customer = "Customer";
            public const string Employee = "Employee";
        }

        public class Shop
        {
            public int Id;
            public string Name;
            public int? AdminId;
            public DateTime? OpeningTime;
            public DateTime? ClosingTime;
            public int QueueLimit;
            public DateTime? LunchBreak;
            public string QrCode;
            public int WaitingCount;
        }

        public class Users
        {
            public int Id;
            public string Name;
            public string Email;
            public string Password;
            public string Salt;
            public string PhoneNumber;
            public int? ShopId;
            public string Role;
            public DateTime Created;
            public bool Deleted;
            public DateTime? LastLoginDate;
        }

        public class QueueEntry
        {
            public int Id;
            public int UserId;
            public int ShopId;
            public DateTime JoinedAt;
            public DateTime? ServedAt;
            public int Status;
            public string StatusName;
            public string Name;
            public string Email;
            public string PhoneNumber;
            public string ShopName;
            public int? Position;
        }
    }
}
