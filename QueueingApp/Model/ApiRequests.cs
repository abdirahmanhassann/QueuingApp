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
            public string? Email { get; set; }
            public string? Password { get; set; }
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
            public string? QrCode { get; set; }
            public string? Address { get; set; }
            public string? Postcode { get; set; }
            public decimal? Latitude { get; set; }
            public decimal? Longitude { get; set; }
            public int? JoinRadiusMetres { get; set; }
        }

        public struct UpdateShopLocationRequest
        {
            public string Token { get; set; }
            public string? Address { get; set; }
            public string? Postcode { get; set; }
            public decimal? Latitude { get; set; }
            public decimal? Longitude { get; set; }
            public int? JoinRadiusMetres { get; set; }
        }

        public struct QueueSettingsRequest
        {
            public string Token { get; set; }
            /// <summary>Most customers allowed to wait at once. 0 = no limit.</summary>
            public int? QueueLimit { get; set; }
            /// <summary>"Manual" (MinutesPerCustomer) or "Average" (learned from recent services).</summary>
            public string? EtaMode { get; set; }
            public int? MinutesPerCustomer { get; set; }
        }

        public struct BreakRequest
        {
            public string Token { get; set; }
            public int? Minutes { get; set; }
            /// <summary>Stop new customers joining while on break.</summary>
            public bool? PauseJoins { get; set; }
        }

        public struct CheckoutRequest
        {
            public string Token { get; set; }
            /// <summary>"Limited" or "Full".</summary>
            public string? Plan { get; set; }
            /// <summary>"Monthly" or "Yearly".</summary>
            public string? Interval { get; set; }
            /// <summary>The frontend's origin (e.g. https://line.example) — Stripe sends the user back to /app/billing there.</summary>
            public string? ReturnUrl { get; set; }
        }

        public struct BillingPortalRequest
        {
            public string Token { get; set; }
            public string? ReturnUrl { get; set; }
        }

        public struct QrCodeRequest
        {
            public string QrCode { get; set; }
        }

        public struct RequestJoinRequest
        {
            public string QrCode { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public decimal? Latitude { get; set; }
            public decimal? Longitude { get; set; }
            public decimal? Accuracy { get; set; }
        }

        public struct VerifyJoinRequest
        {
            public int? PendingJoinId { get; set; }
            public string Code { get; set; }
        }

        public struct PendingJoinIdRequest
        {
            public int? PendingJoinId { get; set; }
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
            public string? AccessKey { get; set; }
        }

        public struct PingCustomerRequest
        {
            public string Token { get; set; }
            public int? Id { get; set; }
            public string? Message { get; set; }
        }

        public struct MoveQueueEntryRequest
        {
            public string Token { get; set; }
            public int? Id { get; set; }
            /// <summary>New 1-based place among waiting customers (1 = next). Clamped to the queue length.</summary>
            public int? Position { get; set; }
        }

        public struct UpdateQueueStatusRequest
        {
            public string Token { get; set; }
            public int? Id { get; set; }
            public int? Status { get; set; }
        }
    }
}
