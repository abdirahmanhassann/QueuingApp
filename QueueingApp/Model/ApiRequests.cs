namespace QueueingApp.Model
{
    public class ApiRequests
    {
        public struct TokenOnlyRequest
        {
            public string Token { get; set; }
        }

        public struct IdOnlyRequest
        {
            public string Token { get; set; }
            public long Id { get; set; }
        }

        public struct LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public struct NewEmployeeAccountRequest
        {
            public string Token { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public int? ShopId { get; set; }
            public string PhoneNumber { get; set; }
        }

        public struct NewShopRequest
        {
            public string Token { get; set; }
            public string Name { get; set; }
            public int AdminId { get; set; }
            public DateTime OpeningTime { get; set; }
            public DateTime ClosingTime { get; set; }
            public int? QueueLimit { get; set; }
            public DateTime? LunchBreak { get; set; }
            public string QrCode { get; set; }
        }

        public struct JoinCustomerRequest
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public int? ShopId { get; set; }
        }

        public struct GetQueueRequest
        {
            public string Token { get; set; }
            public int? Status { get; set; }
        }

        public struct QueueEntryIdRequest
        {
            public string Token { get; set; }
            public int? Id { get; set; }
        }

        public struct UpdateQueueStatusRequest
        {
            public string Token { get; set; }
            public int? Id { get; set; }
            public int? Status { get; set; }
        }
    }
}
