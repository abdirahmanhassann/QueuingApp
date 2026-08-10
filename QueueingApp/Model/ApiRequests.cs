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
            public string Name { get; set; }
            public string Email { get; set; }
            public string Password { get; set; }
            public int? ShopId { get; set; }
        }

    }
}
