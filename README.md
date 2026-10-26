# Line – Virtual Queueing API

Line is a virtual queueing platform for walk-in businesses such as barbers and salons. Customers scan a shop's QR code, verify their phone number with an SMS code, and join the queue from their phone. They can see their live position and estimated wait, and get a text when they're next. Staff manage the queue from a dashboard.

This repository contains the **ASP.NET Core Web API**. The frontend lives in a separate repository.

## Features

- **QR code join with SMS verification.** Customers join a shop's queue by scanning its QR code and confirming a one-time code sent by SMS (Twilio). Repeated wrong guesses are limited.
- **Live queue management for staff.** Serve, cancel, reorder and message customers, and pause the queue for breaks.
- **Wait-time estimates.** Based on recent service durations, with unusually short or long services filtered out, and taking any running break into account.
- **SMS notifications.** Customers get a text when they join, when they're next and when they're called.
- **Subscriptions with Stripe.** Two paid plans after a 14-day free trial, a self-service billing portal, and per-plan limits on staff accounts, queue size and monthly SMS usage.
- **Multi-tenant.** Each shop has its own staff, queue, settings and plan.

## Tech stack

- C# / .NET 10, ASP.NET Core Web API
- SQL Server (T-SQL stored procedures, accessed through a dependency-injected data layer)
- Twilio (SMS and Verify)
- Stripe (Checkout, Customer Portal, webhooks)
- Swagger / OpenAPI

## Project structure

```
QueueingApp/
├── Controllers/    HTTP endpoints (Account, Queue, Billing, Sms)
├── Processors/     Business logic (AccountProcessor, QueueProcessor)
├── Connectivity/   Data access (QADB, DatabaseConnectivity) and SQL scripts/migrations
├── Interfaces/     IDatabaseConnectivity, registered with dependency injection
├── Helpers/        Plans, ETA calculation, Stripe, Twilio, geocoding
├── Security/       Token checks and password encryption
└── Model/          Request and response models
deploy/             Docker files and scripts from a personal test deployment
```

## Running locally

**Prerequisites:** .NET 10 SDK and SQL Server (Express is fine).

1. **Create the database.** Run `Connectivity/DBSCRIPT.txt`, then the `MIGRATION_*.sql` scripts. `SEED_TEST_DATA.sql` adds sample data.
2. **Set the connection string.** Either edit `ConnectionStrings:QADB` in `appsettings.json`, or override it in an `appsettings.Local.json` file next to it. That file is git-ignored.
3. **Set the secrets with .NET user secrets.** They are never stored in the repository.

   ```bash
   cd QueueingApp
   dotnet user-secrets set "Security:EncryptionKey" "<a long random string>"
   dotnet user-secrets set "TWILIO_ACCOUNT_SID" "<your Twilio account SID>"
   dotnet user-secrets set "TWILIO_AUTH_TOKEN" "<your Twilio auth token>"
   dotnet user-secrets set "TWILIO_FROM_NUMBER" "<your Twilio number>"
   dotnet user-secrets set "TWILIO_VERIFY_SERVICE_SID" "<your Verify service SID>"
   ```

   Stripe keys and price IDs are read from the `dbo.AppSettings` and `dbo.PlanPrices` tables.
4. **Run it.**

   ```bash
   dotnet run
   ```

   Swagger UI is then available at `/swagger`.
