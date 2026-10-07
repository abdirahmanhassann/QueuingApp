using QueueingApp.Helpers;

namespace QueueingApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Private settings for this computer only. Git ignores this file.
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            // Stripe keys and price ids from the database (dbo.AppSettings / dbo.PlanPrices). Added last so the
            // database wins; re-read every 30 seconds, so SQL changes apply without a restart.
            builder.Configuration.AddDbSettings(builder.Configuration.GetConnectionString("QADB"));

            foreach (var key in new[]
            {
                "TWILIO_ACCOUNT_SID",
                "TWILIO_AUTH_TOKEN",
                "TWILIO_FROM_NUMBER",
                "TWILIO_TO_NUMBER",
                "TWILIO_VERIFY_SERVICE_SID"
            })
            {
                var value = builder.Configuration[key];
                if (!string.IsNullOrWhiteSpace(value) &&
                    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            // <Nullable>enable</Nullable> makes [ApiController] treat every non-nullable string on a request model as
            // [Required] and reject the call with a 400 before our code runs ("The Token field is required.").
            // The controllers/processors validate their own inputs and return GenericResponse errors, so switch that off.
            builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    o.JsonSerializerOptions.NumberHandling =
                        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;
                });
            builder.Services.AddOpenApi();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader());
            });

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseCors();
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
