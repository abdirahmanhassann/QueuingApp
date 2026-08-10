namespace QueueingApp.Model
{
    public class Model
    {
        public class Shop
        {
            public string Name;
            public int AdminId;
            public DateTime OpeningTime;
            public DateTime ClosingTime;
            public int QueueLimit;
            //##TODO: Potentially create a BreakToShop mapping table to allow shop to take multiple breaks?
            public DateTime LunchBreak;
            public string QrCodeId;
        }

        public class Employee
        {
            public int Id;
            public string Name;
            public string Email;
            public int ShopId;
            public DateTime Created;
            public bool Deleted;
            public DateTime LastLoginDate;
        }

        public class Customer
        {
            public int Id;
            public string Name;
            public int ShopId;
            public bool IsWaiting;
            public DateTime Created;
            public DateTime ServedTime;
            public string Email;
            public string PhoneNumber;
        }

    }
}
