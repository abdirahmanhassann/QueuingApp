using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Rest.Verify.V2.Service;
using Twilio.Types;

public class TwilioHelper
{
    private readonly string _serviceSid;
    private readonly string _fromNumber;

    public TwilioHelper(string serviceSid = null)
    {
        string accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID") ?? throw new InvalidOperationException("TWILIO_ACCOUNT_SID not set");
        string authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN") ?? throw new InvalidOperationException("TWILIO_AUTH_TOKEN not set");

        if (!accountSid.StartsWith("AC", StringComparison.OrdinalIgnoreCase))
        {
            if (accountSid.StartsWith("SK", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("TWILIO_ACCOUNT_SID appears to be an API Key SID (starts with 'SK'). Set TWILIO_ACCOUNT_SID to your Account SID (starts with 'AC') or provide API key env vars and update initialization.");
            }
            throw new InvalidOperationException("TWILIO_ACCOUNT_SID does not look like a valid Account SID (should start with 'AC').");
        }

        TwilioClient.Init(accountSid, authToken);

        _fromNumber = Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER") ?? throw new InvalidOperationException("TWILIO_FROM_NUMBER not set");

        // Only Twilio Verify (SendVerificationAsync) needs this; plain texts and our own codes don't.
        _serviceSid = serviceSid ?? Environment.GetEnvironmentVariable("TWILIO_VERIFY_SERVICE_SID") ?? "";
        if (_serviceSid.Length > 0 && !_serviceSid.StartsWith("VA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("TWILIO_VERIFY_SERVICE_SID does not look like a Verify Service SID (should start with 'VA').");
        }
    }

    public async Task<MessageResource> SendPlainMessageAsync(string toNumber, string body)
    {
        if (string.IsNullOrWhiteSpace(toNumber)) throw new ArgumentException(nameof(toNumber));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException(nameof(body));
        return await MessageResource.CreateAsync(
            body: body,
            from: new PhoneNumber(_fromNumber),
            to: new PhoneNumber(toNumber)
        );
    }
    public async Task<MessageResource> SendMessageAsync(string toNumber, int code)
    {
        if (string.IsNullOrWhiteSpace(toNumber)) throw new ArgumentException(nameof(toNumber));
        return await MessageResource.CreateAsync(
            body: $"Your Line code is {code:D6}. It expires in 5 minutes. Don't share it with anyone.",
            from: new PhoneNumber(_fromNumber),
            to: new PhoneNumber(toNumber)
        );
    }
    public async Task<MessageResource> StatusUpdateAsync(string status, string toNumber, string roomName, string comment)
    {
        string smsBody = $"Your access request to {roomName} has been {status}. \r\n";
        if (status == "REJECTED".ToLower())
        {
            smsBody += $"REASON: {comment}";
        }
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException(nameof(status));
        try
        {
            return await MessageResource.CreateAsync(
                body: smsBody,
                from: new PhoneNumber(_fromNumber),
                to: new PhoneNumber(toNumber)
            );
        }
        catch (ApiException)
        {
            return null;
        }
    }
    public async Task<MessageResource> ApprovalRequestAsync(string firstname, string lastname, string site, string workarea, string roomName, string toNumber)
    {
        string smsBody = $"{firstname} {lastname} requested access to {roomName} at {site} ({workarea}). Open SiteGate to approve or reject.";
        if (string.IsNullOrWhiteSpace(toNumber)) throw new ArgumentException(nameof(toNumber));
        try
        {
            return await MessageResource.CreateAsync(
                body: smsBody,
                from: new PhoneNumber(_fromNumber),
                to: new PhoneNumber(toNumber)
            );
        }
        catch (ApiException)
        {
            return null;
        }
    }

    // Queue notification texts. Errors from Twilio are left to the caller (QueueProcessor.TrySms), which logs them
    // and gives the text back to the shop's monthly allowance.

    public Task<MessageResource> QueueJoinedAsync(string toNumber, string shopName, int? position)
    {
        string smsBody = position == 1
            // No separate "you're next" text follows for someone who joins at the front, so say it now.
            ? $"You're in the queue at {shopName} and you're next. Please stay nearby."
            : position.HasValue
                ? $"You're in the queue at {shopName}. You're #{position}. We'll text you when you're next."
                : $"You're in the queue at {shopName}. We'll text you when you're next.";
        return SendPlainMessageAsync(toNumber, smsBody);
    }

    public Task<MessageResource> NowServingAsync(string toNumber, string shopName)
    {
        return SendPlainMessageAsync(toNumber, $"It's your turn at {shopName}. Come to the chair.");
    }

    public Task<MessageResource> YoureNextAsync(string toNumber, string shopName)
    {
        return SendPlainMessageAsync(toNumber, $"You're next at {shopName}. Please stay nearby.");
    }

    public Task<MessageResource> QueueCancelledAsync(string toNumber, string shopName)
    {
        return SendPlainMessageAsync(toNumber, $"Your place in the queue at {shopName} was cancelled. You can join again any time.");
    }

    public async Task<VerificationResource> SendVerificationAsync(string toNumber, string channel = "sms", string fallbackChannel = null)
    {
        if (string.IsNullOrWhiteSpace(toNumber)) throw new ArgumentException(nameof(toNumber));
        if (string.IsNullOrWhiteSpace(_serviceSid)) throw new InvalidOperationException("TWILIO_VERIFY_SERVICE_SID not set");

        try
        {
            return await VerificationResource.CreateAsync(
                to: toNumber,
                channel: channel,
                pathServiceSid: _serviceSid
            );
        }
        catch (ApiException ex)
        {
            if (ex.Message?.Contains("Delivery channel disabled", StringComparison.OrdinalIgnoreCase) == true &&
                string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(fallbackChannel))
                {
                    return await VerificationResource.CreateAsync(
                        to: toNumber,
                        channel: fallbackChannel,
                        pathServiceSid: _serviceSid
                    );
                }

                throw new InvalidOperationException($"Twilio Verify service {_serviceSid} has SMS disabled. Enable SMS in the Twilio Console or provide a fallback channel.", ex);
            }

            throw;
        }
    }
}
