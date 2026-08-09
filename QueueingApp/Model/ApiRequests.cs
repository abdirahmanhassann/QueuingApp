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

        public struct NewAccountRequest
        {
            public string Email { get; set; }
            public string DisplayName { get; set; }
            public string Password { get; set; }
            public string GymName { get; set; }
            public long Country { get; set; }
            public long City { get; set; }
        }

    }
}
