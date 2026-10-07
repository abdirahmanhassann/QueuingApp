/*
    ============================================================================================================
    QUEUINGAPP — all database changes in one script.

    Run this whole file against QUEUINGAPP (after the base schema in DBSCRIPT.txt). It is safe to run again at
    any time: tables and columns are only added if missing, procedures use CREATE OR ALTER, and data fixes
    only touch rows that need them. Whenever the app changes, re-run the latest copy of this file — that's it.

    Order matters and is handled here (later parts redefine some procedures from earlier ones):
      Part 1. QR + location + OTP join, pings, reordering, queue limit / breaks / ETA, subscription columns
      Part 2. Live vs demo SMS switch (AppSettings)
      Part 3. "You're next" texts
      Part 4. Security and race-condition fixes
      Part 5. Paid plans only (Limited / Full)
      Part 6. Business sign-up (RegisterShop)
      Part 7. Stripe settings in the database (keys and price ids)
      Part 8. Fixes from end-to-end testing

    Test data lives separately in SEED_TEST_DATA.sql (run that only on a test database).
    ============================================================================================================
*/


/* ##########################################################################################################
   PART 1. QR + location + OTP join, pings, reordering, queue limit / breaks / ETA, subscription columns
   ########################################################################################################## */
/*
    QR + location + OTP join flow
    Run once against QUEUINGAPP after DBSCRIPT.txt. Safe to re-run.

    1. Users.Role check constraint matched to the values the procs actually write ('Customer' / 'Employee')
    2. Shops: Address, Postcode, Latitude, Longitude, JoinRadiusMetres; every shop gets a unique QrCode
    3. PendingJoins: customer details held while the SMS code is outstanding
    4. Procs: ShopsGet (no longer exposes QrCode), ShopsGetByQrCode, CreateShop, ShopsUpdateLocation,
       ShopsRegenerateQrCode, PendingJoinsCreate, PendingJoinsGetById, PendingJoinsRecordAttempt,
       PendingJoinsResend, PendingJoinsComplete
    5. QueueEntries.AccessKey: secret per ticket, so only whoever joined can view it (QueueEntriesGetById returns it)
    6. EmployeesCountAll + ShopsGetById: used to lock down /Account/signup
    7. Shops.ShortCode: 6-character code customers can type instead of scanning (ShopsGetByQrCode accepts either).
    8. QueueEntries.LastPingedAt / LastPingMessage / PingCount: staff can text a customer from the queue board
       (QueueEntriesRecordPing); QueueEntriesGet and QueueEntriesGetById return the ping columns.
    9. QueueEntries.QueueOrder + QueueSortKey: staff can move waiting customers up/down (QueueEntriesMove).
       dbo.QueuePosition and QueueEntriesGet now follow QueueSortKey instead of join time alone.
   10. Shops.EtaMode / MinutesPerCustomer / BreakUntil / BreakPausesJoins, QueueEntries.ServingStartedAt:
       queue limit + break controls and wait-time estimates (QueueServiceDurations, ShopsUpdateQueueSettings,
       ShopsSetBreak). QueueEntriesUpdateStatus now stamps ServingStartedAt.
   11. Subscription plans: Shops.Plan / TrialEndsAt / Stripe* / SubscriptionStatus / BillingInterval /
       CurrentPeriodEnd / CancelAtPeriodEnd, ShopSmsUsage (monthly text allowance), BillingEvents (webhook dedupe).
       Every existing shop gets a 14-day Full trial from the moment this runs.
       Later sections redefine some procs from earlier ones (CreateShop, ShopsGetByQrCode, ShopsRegenerateQrCode,
       QueueEntriesGetById); the last definition wins.
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/****** 1. Users.Role ******/
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Users_Role' AND parent_object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    ALTER TABLE [dbo].[Users] DROP CONSTRAINT [CK_Users_Role];
END
GO
UPDATE [dbo].[Users] SET Role = N'Customer' WHERE Role = N'CUS';
UPDATE [dbo].[Users] SET Role = N'Employee' WHERE Role = N'EMP';
GO
ALTER TABLE [dbo].[Users] WITH CHECK ADD CONSTRAINT [CK_Users_Role] CHECK (([Role]=N'Customer' OR [Role]=N'Employee'))
GO
ALTER TABLE [dbo].[Users] CHECK CONSTRAINT [CK_Users_Role]
GO

/****** 2. Shops location + QR ******/
IF COL_LENGTH(N'dbo.Shops', N'Address') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [Address] [nvarchar](300) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'Postcode') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [Postcode] [nvarchar](10) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'Latitude') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [Latitude] [decimal](9, 6) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'Longitude') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [Longitude] [decimal](9, 6) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'JoinRadiusMetres') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [JoinRadiusMetres] [int] NOT NULL CONSTRAINT [DF_Shops_JoinRadiusMetres] DEFAULT ((150));
GO

UPDATE [dbo].[Shops]
SET QrCode = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''))
WHERE QrCode IS NULL OR LTRIM(RTRIM(QrCode)) = N'';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Shops_QrCode' AND object_id = OBJECT_ID(N'dbo.Shops'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_Shops_QrCode] ON [dbo].[Shops] ([QrCode]) WHERE [QrCode] IS NOT NULL;
GO

/****** 3. PendingJoins ******/
IF OBJECT_ID(N'dbo.PendingJoins', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PendingJoins](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [ShopId] [int] NOT NULL,
        [Name] [nvarchar](100) NOT NULL,
        [Email] [nvarchar](255) NOT NULL,
        [PhoneNumber] [nvarchar](20) NOT NULL,
        [OtpHash] [nvarchar](255) NOT NULL,
        [OtpSalt] [nvarchar](255) NOT NULL,
        [Attempts] [int] NOT NULL CONSTRAINT [DF_PendingJoins_Attempts] DEFAULT ((0)),
        [Sends] [int] NOT NULL CONSTRAINT [DF_PendingJoins_Sends] DEFAULT ((1)),
        [Created] [datetime2](7) NOT NULL CONSTRAINT [DF_PendingJoins_Created] DEFAULT (sysutcdatetime()),
        [LastSentAt] [datetime2](7) NOT NULL CONSTRAINT [DF_PendingJoins_LastSentAt] DEFAULT (sysutcdatetime()),
        [ExpiresAt] [datetime2](7) NOT NULL,
        [VerifiedAt] [datetime2](7) NULL,
        [QueueEntryId] [int] NULL,
     CONSTRAINT [PK_PendingJoins] PRIMARY KEY CLUSTERED ([Id] ASC)
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[PendingJoins] WITH CHECK ADD CONSTRAINT [FK_PendingJoins_Shops] FOREIGN KEY([ShopId])
    REFERENCES [dbo].[Shops] ([Id]);

    CREATE NONCLUSTERED INDEX [IX_PendingJoins_PhoneNumber_Created] ON [dbo].[PendingJoins] ([PhoneNumber], [Created]);
END
GO

/****** 4. Procs ******/

-- Public shop list: never return QrCode, otherwise anyone can join without scanning.
CREATE OR ALTER PROCEDURE [dbo].[ShopsGet]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.Id, s.Name, s.AdminId, s.OpeningTime, s.ClosingTime, s.QueueLimit, s.LunchBreak, s.Created,
           s.Address, s.Postcode,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    ORDER BY s.Name;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsGetByQrCode]
    @QrCode NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 s.Id, s.Name, s.OpeningTime, s.ClosingTime, s.QueueLimit, s.LunchBreak,
           s.Address, s.Postcode, s.Latitude, s.Longitude, s.JoinRadiusMetres,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.QrCode = @QrCode;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[CreateShop]
    @Name NVARCHAR(200),
    @AdminId INT,
    @OpeningTime DATETIME2,
    @ClosingTime DATETIME2,
    @QueueLimit INT,
    @LunchBreak DATETIME2 = NULL,
    @QrCode NVARCHAR(100) = NULL,
    @Address NVARCHAR(300) = NULL,
    @Postcode NVARCHAR(10) = NULL,
    @Latitude DECIMAL(9, 6) = NULL,
    @Longitude DECIMAL(9, 6) = NULL,
    @JoinRadiusMetres INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @QrCode IS NULL OR LTRIM(RTRIM(@QrCode)) = N''
        SET @QrCode = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''));

    INSERT INTO dbo.Shops (Name, AdminId, OpeningTime, ClosingTime, QueueLimit, LunchBreak, QrCode,
                           Address, Postcode, Latitude, Longitude, JoinRadiusMetres)
    VALUES (@Name, @AdminId, @OpeningTime, @ClosingTime, ISNULL(@QueueLimit, 0), @LunchBreak, @QrCode,
            @Address, @Postcode, @Latitude, @Longitude, ISNULL(@JoinRadiusMetres, 150));
    SELECT SCOPE_IDENTITY() AS Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsUpdateLocation]
    @ShopId INT,
    @Address NVARCHAR(300) = NULL,
    @Postcode NVARCHAR(10) = NULL,
    @Latitude DECIMAL(9, 6),
    @Longitude DECIMAL(9, 6),
    @JoinRadiusMetres INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops
    SET Address = ISNULL(@Address, Address),
        Postcode = ISNULL(@Postcode, Postcode),
        Latitude = @Latitude,
        Longitude = @Longitude,
        JoinRadiusMetres = ISNULL(@JoinRadiusMetres, JoinRadiusMetres)
    WHERE Id = @ShopId;

    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @ShopId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsRegenerateQrCode]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops
    SET QrCode = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''))
    WHERE Id = @ShopId;

    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @ShopId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsCreate]
    @ShopId INT,
    @Name NVARCHAR(100),
    @Email NVARCHAR(255),
    @PhoneNumber NVARCHAR(20),
    @OtpHash NVARCHAR(255),
    @OtpSalt NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    IF (SELECT COUNT(*) FROM dbo.PendingJoins
        WHERE PhoneNumber = @PhoneNumber AND Created > DATEADD(HOUR, -1, SYSUTCDATETIME())) >= 5
    BEGIN
        RAISERROR(N'Too many codes requested for this number. Try again later.', 16, 1);
        RETURN;
    END

    INSERT INTO dbo.PendingJoins (ShopId, Name, Email, PhoneNumber, OtpHash, OtpSalt, ExpiresAt)
    VALUES (@ShopId, @Name, @Email, @PhoneNumber, @OtpHash, @OtpSalt, DATEADD(MINUTE, 5, SYSUTCDATETIME()));

    SELECT Id, ShopId, PhoneNumber, ExpiresAt
    FROM dbo.PendingJoins
    WHERE Id = CAST(SCOPE_IDENTITY() AS INT);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 p.*, s.Name AS ShopName
    FROM dbo.PendingJoins p
    INNER JOIN dbo.Shops s ON s.Id = p.ShopId
    WHERE p.Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsRecordAttempt]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PendingJoins SET Attempts = Attempts + 1 WHERE Id = @Id;
    SELECT Attempts FROM dbo.PendingJoins WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsResend]
    @Id INT,
    @OtpHash NVARCHAR(255),
    @OtpSalt NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @LastSentAt DATETIME2, @Sends INT, @VerifiedAt DATETIME2;
    SELECT @LastSentAt = LastSentAt, @Sends = Sends, @VerifiedAt = VerifiedAt FROM dbo.PendingJoins WHERE Id = @Id;

    IF @LastSentAt IS NULL
    BEGIN
        RAISERROR(N'That request has expired. Start again.', 16, 1);
        RETURN;
    END
    IF @VerifiedAt IS NOT NULL
    BEGIN
        RAISERROR(N'This number is already verified.', 16, 1);
        RETURN;
    END
    IF @Sends >= 3
    BEGIN
        RAISERROR(N'Too many codes sent. Start again.', 16, 1);
        RETURN;
    END
    IF @LastSentAt > DATEADD(SECOND, -30, SYSUTCDATETIME())
    BEGIN
        RAISERROR(N'Wait a few seconds before asking for another code.', 16, 1);
        RETURN;
    END

    UPDATE dbo.PendingJoins
    SET OtpHash = @OtpHash,
        OtpSalt = @OtpSalt,
        Attempts = 0,
        Sends = Sends + 1,
        LastSentAt = SYSUTCDATETIME(),
        ExpiresAt = DATEADD(MINUTE, 5, SYSUTCDATETIME())
    WHERE Id = @Id;

    SELECT Id, ShopId, PhoneNumber, ExpiresAt FROM dbo.PendingJoins WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsComplete]
    @Id INT,
    @QueueEntryId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PendingJoins
    SET VerifiedAt = SYSUTCDATETIME(),
        QueueEntryId = @QueueEntryId
    WHERE Id = @Id;
END
GO

/****** 5. Ticket access keys ******/
IF COL_LENGTH(N'dbo.QueueEntries', N'AccessKey') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [AccessKey] [nvarchar](32) NOT NULL
        CONSTRAINT [DF_QueueEntries_AccessKey] DEFAULT (LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N'')));
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.AccessKey
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.Id = @Id;
END
GO

/****** 6. Staff signup checks ******/
CREATE OR ALTER PROCEDURE [dbo].[EmployeesCountAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM dbo.Users WHERE Role = N'Employee' AND Deleted = 0;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 Id, Name, AdminId FROM dbo.Shops WHERE Id = @Id;
END
GO

/****** 7. Short, typeable shop codes ******/
IF COL_LENGTH(N'dbo.Shops', N'ShortCode') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [ShortCode] [nvarchar](6) NULL;
GO

-- 6 characters from an alphabet without look-alikes (no 0/O, 1/I/L). 31^6 is about 887 million codes.
CREATE OR ALTER PROCEDURE [dbo].[ShopsNewShortCode]
    @ShortCode NVARCHAR(6) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Alphabet NVARCHAR(31) = N'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
    DECLARE @Bytes VARBINARY(6), @i INT;
    WHILE 1 = 1
    BEGIN
        SET @Bytes = CRYPT_GEN_RANDOM(6);
        SET @ShortCode = N'';
        SET @i = 1;
        WHILE @i <= 6
        BEGIN
            SET @ShortCode = @ShortCode + SUBSTRING(@Alphabet, (CAST(SUBSTRING(@Bytes, @i, 1) AS INT) % 31) + 1, 1);
            SET @i = @i + 1;
        END
        IF NOT EXISTS (SELECT 1 FROM dbo.Shops WHERE ShortCode = @ShortCode) BREAK;
    END
END
GO

DECLARE @BackfillId INT, @NewShort NVARCHAR(6);
WHILE EXISTS (SELECT 1 FROM dbo.Shops WHERE ShortCode IS NULL)
BEGIN
    SELECT TOP 1 @BackfillId = Id FROM dbo.Shops WHERE ShortCode IS NULL;
    EXEC dbo.ShopsNewShortCode @ShortCode = @NewShort OUTPUT;
    UPDATE dbo.Shops SET ShortCode = @NewShort WHERE Id = @BackfillId;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Shops_ShortCode' AND object_id = OBJECT_ID(N'dbo.Shops'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_Shops_ShortCode] ON [dbo].[Shops] ([ShortCode]) WHERE [ShortCode] IS NOT NULL;
GO

-- Accepts the long QR value or the short code (case, spaces and dashes ignored).
CREATE OR ALTER PROCEDURE [dbo].[ShopsGetByQrCode]
    @QrCode NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Short NVARCHAR(100) = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@QrCode)), N' ', N''), N'-', N''));
    SELECT TOP 1 s.Id, s.Name, s.OpeningTime, s.ClosingTime, s.QueueLimit, s.LunchBreak,
           s.Address, s.Postcode, s.Latitude, s.Longitude, s.JoinRadiusMetres,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.QrCode = @QrCode OR (LEN(@Short) = 6 AND s.ShortCode = @Short);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[CreateShop]
    @Name NVARCHAR(200),
    @AdminId INT,
    @OpeningTime DATETIME2,
    @ClosingTime DATETIME2,
    @QueueLimit INT,
    @LunchBreak DATETIME2 = NULL,
    @QrCode NVARCHAR(100) = NULL,
    @Address NVARCHAR(300) = NULL,
    @Postcode NVARCHAR(10) = NULL,
    @Latitude DECIMAL(9, 6) = NULL,
    @Longitude DECIMAL(9, 6) = NULL,
    @JoinRadiusMetres INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @QrCode IS NULL OR LTRIM(RTRIM(@QrCode)) = N''
        SET @QrCode = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''));
    DECLARE @ShortCode NVARCHAR(6);
    EXEC dbo.ShopsNewShortCode @ShortCode = @ShortCode OUTPUT;

    INSERT INTO dbo.Shops (Name, AdminId, OpeningTime, ClosingTime, QueueLimit, LunchBreak, QrCode, ShortCode,
                           Address, Postcode, Latitude, Longitude, JoinRadiusMetres)
    VALUES (@Name, @AdminId, @OpeningTime, @ClosingTime, ISNULL(@QueueLimit, 0), @LunchBreak, @QrCode, @ShortCode,
            @Address, @Postcode, @Latitude, @Longitude, ISNULL(@JoinRadiusMetres, 150));
    SELECT SCOPE_IDENTITY() AS Id;
END
GO

-- New QR value AND new short code together, so a leaked or printed code of either kind stops working.
CREATE OR ALTER PROCEDURE [dbo].[ShopsRegenerateQrCode]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ShortCode NVARCHAR(6);
    EXEC dbo.ShopsNewShortCode @ShortCode = @ShortCode OUTPUT;

    UPDATE dbo.Shops
    SET QrCode = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N'')),
        ShortCode = @ShortCode
    WHERE Id = @ShopId;

    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @ShopId;
END
GO

/****** 8. Staff can text ("ping") a customer in the queue ******/
IF COL_LENGTH(N'dbo.QueueEntries', N'LastPingedAt') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [LastPingedAt] [datetime2](7) NULL;
GO
IF COL_LENGTH(N'dbo.QueueEntries', N'LastPingMessage') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [LastPingMessage] [nvarchar](300) NULL;
GO
IF COL_LENGTH(N'dbo.QueueEntries', N'PingCount') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [PingCount] [int] NOT NULL CONSTRAINT [DF_QueueEntries_PingCount] DEFAULT ((0));
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.AccessKey,
        q.LastPingedAt, q.LastPingMessage, q.PingCount
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGet]
    @ShopId INT,
    @Status INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.LastPingedAt, q.LastPingMessage, q.PingCount
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.ShopId = @ShopId
      AND (
            (@Status > 0 AND q.Status = @Status)
            OR (@Status = 0 AND q.Status IN (1, 2))
          )
    ORDER BY CASE WHEN q.Status = 2 THEN 0 ELSE 1 END, q.JoinedAt;
END
GO

-- Records the message (shown on the customer's ticket page) before the SMS is attempted.
-- One ping per customer per minute so a double-tap can't spam them.
CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesRecordPing]
    @Id INT,
    @Message NVARCHAR(300)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Status INT, @LastPingedAt DATETIME2;
    SELECT @Status = Status, @LastPingedAt = LastPingedAt FROM dbo.QueueEntries WHERE Id = @Id;

    IF @Status IS NULL
    BEGIN
        RAISERROR(N'Queue entry not found.', 16, 1);
        RETURN;
    END
    IF @Status NOT IN (1, 2)
    BEGIN
        RAISERROR(N'Only customers who are waiting or in the chair can be texted.', 16, 1);
        RETURN;
    END
    IF @LastPingedAt IS NOT NULL AND @LastPingedAt > DATEADD(SECOND, -60, SYSUTCDATETIME())
    BEGIN
        RAISERROR(N'You texted this customer less than a minute ago.', 16, 1);
        RETURN;
    END

    UPDATE dbo.QueueEntries
    SET LastPingedAt = SYSUTCDATETIME(),
        LastPingMessage = @Message,
        PingCount = PingCount + 1
    WHERE Id = @Id;

    EXEC dbo.QueueEntriesGetById @Id = @Id;
END
GO

/****** 9. Staff can move waiting customers up/down the queue ******/
-- QueueOrder is NULL until staff reorder a shop's queue; then every waiting entry in that shop gets 1..N.
-- QueueSortKey is what everything orders by: QueueOrder if set, otherwise microseconds since 2000 of JoinedAt.
-- Those are ~10^17, so anyone joining after a reorder (QueueOrder NULL) still lands at the back.
IF COL_LENGTH(N'dbo.QueueEntries', N'QueueOrder') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [QueueOrder] [bigint] NULL;
GO
IF COL_LENGTH(N'dbo.QueueEntries', N'QueueSortKey') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [QueueSortKey] AS
        (COALESCE([QueueOrder], DATEDIFF_BIG(MICROSECOND, CONVERT(DATETIME2(7), '2000-01-01T00:00:00', 126), [JoinedAt])));
GO

-- Position among the shop's waiting customers (1 = next). Replaces the previous join-time-only version.
CREATE OR ALTER FUNCTION [dbo].[QueuePosition] (@Id INT)
RETURNS INT
AS
BEGIN
    DECLARE @ShopId INT, @Status INT, @Key BIGINT;
    SELECT @ShopId = ShopId, @Status = Status, @Key = QueueSortKey FROM dbo.QueueEntries WHERE Id = @Id;
    IF @Status IS NULL OR @Status <> 1 RETURN NULL;
    RETURN (
        SELECT COUNT(*) FROM dbo.QueueEntries
        WHERE ShopId = @ShopId AND Status = 1
          AND (QueueSortKey < @Key OR (QueueSortKey = @Key AND Id <= @Id))
    );
END
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGet]
    @ShopId INT,
    @Status INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.LastPingedAt, q.LastPingMessage, q.PingCount
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.ShopId = @ShopId
      AND (
            (@Status > 0 AND q.Status = @Status)
            OR (@Status = 0 AND q.Status IN (1, 2))
          )
    ORDER BY CASE WHEN q.Status = 2 THEN 0 ELSE 1 END, q.QueueSortKey, q.Id;
END
GO

-- Move a waiting customer to @NewPosition (1 = front; clamped to the queue length).
CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesMove]
    @Id INT,
    @NewPosition INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ShopId INT, @Status INT;
    SELECT @ShopId = ShopId, @Status = Status FROM dbo.QueueEntries WITH (UPDLOCK) WHERE Id = @Id;
    IF @Status IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'Queue entry not found.', 16, 1);
        RETURN;
    END
    IF @Status <> 1
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'Only waiting customers can be moved.', 16, 1);
        RETURN;
    END

    -- Lock this shop's waiting rows so two staff reordering at once can't interleave.
    DECLARE @Ordered TABLE (Id INT PRIMARY KEY, Pos INT NOT NULL);
    INSERT INTO @Ordered (Id, Pos)
    SELECT Id, ROW_NUMBER() OVER (ORDER BY QueueSortKey, Id)
    FROM dbo.QueueEntries WITH (UPDLOCK, HOLDLOCK)
    WHERE ShopId = @ShopId AND Status = 1;

    DECLARE @Count INT = (SELECT COUNT(*) FROM @Ordered);
    DECLARE @Current INT = (SELECT Pos FROM @Ordered WHERE Id = @Id);
    IF @NewPosition < 1 SET @NewPosition = 1;
    IF @NewPosition > @Count SET @NewPosition = @Count;

    IF @NewPosition <> @Current
    BEGIN
        UPDATE @Ordered
        SET Pos = CASE
            WHEN Id = @Id THEN @NewPosition
            WHEN @NewPosition < @Current AND Pos >= @NewPosition AND Pos < @Current THEN Pos + 1
            WHEN @NewPosition > @Current AND Pos <= @NewPosition AND Pos > @Current THEN Pos - 1
            ELSE Pos END;

        UPDATE q SET QueueOrder = o.Pos
        FROM dbo.QueueEntries q
        INNER JOIN @Ordered o ON o.Id = q.Id;
    END

    COMMIT TRANSACTION;
    EXEC dbo.QueueEntriesGetById @Id = @Id;
END
GO

/****** 10. Queue limit, breaks and wait-time estimates ******/
-- QueueLimit already exists on Shops (0 = no limit) and is enforced by CreateCustomerJoinQueue.
IF COL_LENGTH(N'dbo.Shops', N'EtaMode') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [EtaMode] [nvarchar](10) NOT NULL CONSTRAINT [DF_Shops_EtaMode] DEFAULT (N'Manual');
GO
IF COL_LENGTH(N'dbo.Shops', N'MinutesPerCustomer') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [MinutesPerCustomer] [int] NOT NULL CONSTRAINT [DF_Shops_MinutesPerCustomer] DEFAULT ((15));
GO
IF COL_LENGTH(N'dbo.Shops', N'BreakUntil') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [BreakUntil] [datetime2](7) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'BreakPausesJoins') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [BreakPausesJoins] [bit] NOT NULL CONSTRAINT [DF_Shops_BreakPausesJoins] DEFAULT ((0));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Shops_EtaMode')
    ALTER TABLE [dbo].[Shops] WITH CHECK ADD CONSTRAINT [CK_Shops_EtaMode] CHECK ([EtaMode] IN (N'Manual', N'Average'));
GO
-- When the customer was taken to the chair: ServedAt - ServingStartedAt is the service time the average learns from.
IF COL_LENGTH(N'dbo.QueueEntries', N'ServingStartedAt') IS NULL
    ALTER TABLE [dbo].[QueueEntries] ADD [ServingStartedAt] [datetime2](7) NULL;
GO

-- Same rules as before, plus it stamps ServingStartedAt when someone is taken to the chair.
CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesUpdateStatus]
    @Id INT,
    @Status INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Current INT;
    SELECT @Current = Status FROM dbo.QueueEntries WHERE Id = @Id;
    IF @Current IS NULL
    BEGIN
        RAISERROR(N'Queue entry not found.', 16, 1);
        RETURN;
    END
    IF @Status = 2 AND @Current <> 1
    BEGIN
        RAISERROR(N'Only waiting customers can be taken to the chair.', 16, 1);
        RETURN;
    END
    IF @Status = 3 AND @Current NOT IN (1, 2)
    BEGIN
        RAISERROR(N'That visit is already closed.', 16, 1);
        RETURN;
    END

    UPDATE dbo.QueueEntries
    SET Status = @Status,
        ServedAt = CASE WHEN @Status = 3 THEN SYSUTCDATETIME() ELSE ServedAt END,
        ServingStartedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE ServingStartedAt END
    WHERE Id = @Id;

    EXEC dbo.QueueEntriesGetById @Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.AccessKey,
        q.LastPingedAt, q.LastPingMessage, q.PingCount,
        q.ServingStartedAt
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesGet]
    @ShopId INT,
    @Status INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        q.Id, q.UserId, q.ShopId, q.JoinedAt, q.ServedAt, q.Status,
        qs.Status AS StatusName,
        u.Name, u.Email, u.PhoneNumber,
        s.Name AS ShopName,
        CASE WHEN q.Status = 1 THEN dbo.QueuePosition(q.Id) ELSE NULL END AS Position,
        q.LastPingedAt, q.LastPingMessage, q.PingCount,
        q.ServingStartedAt
    FROM dbo.QueueEntries q
    INNER JOIN dbo.Users u ON u.Id = q.UserId
    INNER JOIN dbo.Shops s ON s.Id = q.ShopId
    INNER JOIN dbo.QueueStatus qs ON qs.Id = q.Status
    WHERE q.ShopId = @ShopId
      AND (
            (@Status > 0 AND q.Status = @Status)
            OR (@Status = 0 AND q.Status IN (1, 2))
          )
    ORDER BY CASE WHEN q.Status = 2 THEN 0 ELSE 1 END, q.QueueSortKey, q.Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsGetByQrCode]
    @QrCode NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Short NVARCHAR(100) = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@QrCode)), N' ', N''), N'-', N''));
    SELECT TOP 1 s.Id, s.Name, s.OpeningTime, s.ClosingTime, s.QueueLimit, s.LunchBreak,
           s.Address, s.Postcode, s.Latitude, s.Longitude, s.JoinRadiusMetres,
           s.EtaMode, s.MinutesPerCustomer, s.BreakUntil, s.BreakPausesJoins,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.QrCode = @QrCode OR (LEN(@Short) = 6 AND s.ShortCode = @Short);
END
GO

-- Seconds each recent completed service took (taken to the chair -> marked served). Last 30 days, newest 50.
CREATE OR ALTER PROCEDURE [dbo].[QueueServiceDurations]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 50 DATEDIFF(SECOND, ServingStartedAt, ServedAt) AS Seconds
    FROM dbo.QueueEntries
    WHERE ShopId = @ShopId
      AND Status = 3
      AND ServingStartedAt IS NOT NULL
      AND ServedAt IS NOT NULL
      AND ServedAt > ServingStartedAt
      AND ServedAt > DATEADD(DAY, -30, SYSUTCDATETIME())
    ORDER BY ServedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsUpdateQueueSettings]
    @ShopId INT,
    @QueueLimit INT,
    @EtaMode NVARCHAR(10),
    @MinutesPerCustomer INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops
    SET QueueLimit = @QueueLimit,
        EtaMode = @EtaMode,
        MinutesPerCustomer = @MinutesPerCustomer
    WHERE Id = @ShopId;

    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @ShopId;
END
GO

-- @Minutes = 0 ends the break.
CREATE OR ALTER PROCEDURE [dbo].[ShopsSetBreak]
    @ShopId INT,
    @Minutes INT,
    @PauseJoins BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops
    SET BreakUntil = CASE WHEN @Minutes > 0 THEN DATEADD(MINUTE, @Minutes, SYSUTCDATETIME()) ELSE NULL END,
        BreakPausesJoins = CASE WHEN @Minutes > 0 THEN @PauseJoins ELSE 0 END
    WHERE Id = @ShopId;

    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @ShopId;
END
GO

-- Now returns the whole shop row (wait-time settings, break) as well as Id/Name/AdminId.
CREATE OR ALTER PROCEDURE [dbo].[ShopsGetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.Id = @Id;
END
GO

/****** 11. Subscription plans (Limited / Full) via Stripe ******/
-- Plan = what the shop pays for. The plan actually in force is worked out in code (Helpers/Plans.cs):
-- an active/trialing subscription wins, then a past_due grace period, then the free Full trial, else None (expired).
IF COL_LENGTH(N'dbo.Shops', N'Plan') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [Plan] [nvarchar](10) NOT NULL CONSTRAINT [DF_Shops_Plan] DEFAULT (N'None');
GO
-- New shops get a 14-day Full trial. WITH VALUES gives every existing shop one too, so nobody is capped overnight.
IF COL_LENGTH(N'dbo.Shops', N'TrialEndsAt') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [TrialEndsAt] [datetime2](7) NULL
        CONSTRAINT [DF_Shops_TrialEndsAt] DEFAULT (DATEADD(DAY, 14, SYSUTCDATETIME())) WITH VALUES;
GO
IF COL_LENGTH(N'dbo.Shops', N'StripeCustomerId') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [StripeCustomerId] [nvarchar](64) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'StripeSubscriptionId') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [StripeSubscriptionId] [nvarchar](64) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'SubscriptionStatus') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [SubscriptionStatus] [nvarchar](20) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'BillingInterval') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [BillingInterval] [nvarchar](10) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'CurrentPeriodEnd') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [CurrentPeriodEnd] [datetime2](7) NULL;
GO
IF COL_LENGTH(N'dbo.Shops', N'CancelAtPeriodEnd') IS NULL
    ALTER TABLE [dbo].[Shops] ADD [CancelAtPeriodEnd] [bit] NOT NULL CONSTRAINT [DF_Shops_CancelAtPeriodEnd] DEFAULT ((0));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Shops_Plan')
    ALTER TABLE [dbo].[Shops] WITH CHECK ADD CONSTRAINT [CK_Shops_Plan] CHECK ([Plan] IN (N'None', N'Limited', N'Full'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Shops_StripeCustomerId' AND object_id = OBJECT_ID(N'dbo.Shops'))
    CREATE NONCLUSTERED INDEX [IX_Shops_StripeCustomerId] ON [dbo].[Shops] ([StripeCustomerId]) WHERE [StripeCustomerId] IS NOT NULL;
GO

-- Texts sent per shop per calendar month (UTC), for the plan's SMS allowance.
IF OBJECT_ID(N'dbo.ShopSmsUsage', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ShopSmsUsage](
        [ShopId] [int] NOT NULL,
        [Month] [date] NOT NULL,
        [Sent] [int] NOT NULL CONSTRAINT [DF_ShopSmsUsage_Sent] DEFAULT ((0)),
     CONSTRAINT [PK_ShopSmsUsage] PRIMARY KEY CLUSTERED ([ShopId] ASC, [Month] ASC)
    ) ON [PRIMARY];
END
GO

-- Stripe webhook event ids already handled (Stripe can deliver the same event more than once).
IF OBJECT_ID(N'dbo.BillingEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BillingEvents](
        [EventId] [nvarchar](100) NOT NULL,
        [Type] [nvarchar](100) NOT NULL,
        [ShopId] [int] NULL,
        [Received] [datetime2](7) NOT NULL CONSTRAINT [DF_BillingEvents_Received] DEFAULT (sysutcdatetime()),
     CONSTRAINT [PK_BillingEvents] PRIMARY KEY CLUSTERED ([EventId] ASC)
    ) ON [PRIMARY];
END
GO

-- Uses one text from this month's allowance. Returns 1 if allowed (and counts it), 0 if the allowance is used up.
-- @Limit < 0 = unlimited (still counted).
CREATE OR ALTER PROCEDURE [dbo].[ShopSmsTryConsume]
    @ShopId INT,
    @Limit INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Month DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM dbo.ShopSmsUsage WITH (UPDLOCK, HOLDLOCK) WHERE ShopId = @ShopId AND [Month] = @Month)
        INSERT INTO dbo.ShopSmsUsage (ShopId, [Month], Sent) VALUES (@ShopId, @Month, 0);

    UPDATE dbo.ShopSmsUsage
    SET Sent = Sent + 1
    WHERE ShopId = @ShopId AND [Month] = @Month AND (@Limit < 0 OR Sent < @Limit);
    DECLARE @Allowed INT = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @Allowed;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopSmsUsageGet]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Month DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
    SELECT ISNULL((SELECT Sent FROM dbo.ShopSmsUsage WHERE ShopId = @ShopId AND [Month] = @Month), 0);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsSetStripeCustomer]
    @ShopId INT,
    @StripeCustomerId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops SET StripeCustomerId = @StripeCustomerId WHERE Id = @ShopId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[ShopsGetByStripeCustomer]
    @StripeCustomerId NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 * FROM dbo.Shops WHERE StripeCustomerId = @StripeCustomerId;
END
GO

-- Written only by the Stripe webhook.
CREATE OR ALTER PROCEDURE [dbo].[ShopsUpdateSubscription]
    @ShopId INT,
    @StripeCustomerId NVARCHAR(64),
    @StripeSubscriptionId NVARCHAR(64),
    @Plan NVARCHAR(10),
    @SubscriptionStatus NVARCHAR(20),
    @BillingInterval NVARCHAR(10) = NULL,
    @CurrentPeriodEnd DATETIME2 = NULL,
    @CancelAtPeriodEnd BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Shops
    SET StripeCustomerId = @StripeCustomerId,
        StripeSubscriptionId = @StripeSubscriptionId,
        [Plan] = @Plan,
        SubscriptionStatus = @SubscriptionStatus,
        BillingInterval = @BillingInterval,
        CurrentPeriodEnd = @CurrentPeriodEnd,
        CancelAtPeriodEnd = @CancelAtPeriodEnd
    WHERE Id = @ShopId;
END
GO

-- Returns 1 the first time an event id is seen, 0 for repeats.
CREATE OR ALTER PROCEDURE [dbo].[BillingEventsTryRecord]
    @EventId NVARCHAR(100),
    @Type NVARCHAR(100),
    @ShopId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        INSERT INTO dbo.BillingEvents (EventId, [Type], ShopId) VALUES (@EventId, @Type, @ShopId);
        SELECT 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2627, 2601) SELECT 0; ELSE THROW;
    END CATCH
END
GO

-- Customer join lookups also need the plan columns (queue cap per plan).
CREATE OR ALTER PROCEDURE [dbo].[ShopsGetByQrCode]
    @QrCode NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Short NVARCHAR(100) = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@QrCode)), N' ', N''), N'-', N''));
    SELECT TOP 1 s.Id, s.Name, s.OpeningTime, s.ClosingTime, s.QueueLimit, s.LunchBreak,
           s.Address, s.Postcode, s.Latitude, s.Longitude, s.JoinRadiusMetres,
           s.EtaMode, s.MinutesPerCustomer, s.BreakUntil, s.BreakPausesJoins,
           s.[Plan], s.SubscriptionStatus, s.CurrentPeriodEnd, s.TrialEndsAt,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    WHERE s.QrCode = @QrCode OR (LEN(@Short) = 6 AND s.ShortCode = @Short);
END
GO

/* ##########################################################################################################
   PART 2. Live vs demo SMS switch (AppSettings)
   ########################################################################################################## */
/*
    SMS live / demo switch. Run once against QUEUINGAPP. Safe to re-run (keeps the current setting).

    dbo.AppSettings is a single-row table (Id is always 1).
      SmsLive = 1  -> texts really go out through Twilio (verification codes, "you're next", staff pings).
      SmsLive = 0  -> texts off: nothing is sent to Twilio. Customers can't get a verification code (so can't
                      join), notification texts are skipped, and staff pings only show on the customer's ticket.
                      Codes are never shown on screen.

    A new database starts with texts off. Turn them on:   UPDATE dbo.AppSettings SET SmsLive = 1;
    Turn them off again:                                   UPDATE dbo.AppSettings SET SmsLive = 0;
    The API picks up a change within 30 seconds (no restart needed).
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AppSettings](
        [Id] [int] NOT NULL CONSTRAINT [DF_AppSettings_Id] DEFAULT ((1)),
        [SmsLive] [bit] NOT NULL CONSTRAINT [DF_AppSettings_SmsLive] DEFAULT ((0)),
        [UpdatedAt] [datetime2](7) NOT NULL CONSTRAINT [DF_AppSettings_UpdatedAt] DEFAULT (sysutcdatetime()),
     CONSTRAINT [PK_AppSettings] PRIMARY KEY CLUSTERED ([Id] ASC),
     CONSTRAINT [CK_AppSettings_SingleRow] CHECK ([Id] = 1)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE Id = 1)
    INSERT INTO dbo.AppSettings (Id, SmsLive) VALUES (1, 0);
GO

-- Keep UpdatedAt honest when someone flips the switch by hand.
CREATE OR ALTER TRIGGER [dbo].[TR_AppSettings_Touch] ON [dbo].[AppSettings]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL() > 1 RETURN;
    UPDATE s SET UpdatedAt = SYSUTCDATETIME()
    FROM dbo.AppSettings s INNER JOIN inserted i ON i.Id = s.Id;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[AppSettingsGet]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 Id, SmsLive, UpdatedAt FROM dbo.AppSettings WHERE Id = 1;
END
GO

/* ##########################################################################################################
   PART 3. "You're next" texts
   ########################################################################################################## */
/*
    "You're next" texts. Run once against QUEUINGAPP after MIGRATION_QR_OTP_JOIN.sql. Safe to re-run.

    Whoever is first among the waiting customers gets one "you're next" text. It goes out whenever the front of
    the queue changes: someone is taken to the chair, marked served, cancelled, or staff move a customer.
    QueueEntries.NextNotifiedAt remembers who has already been told, so nobody gets the same text twice.
    Someone who is moved off the front loses the flag, so they're told again if they get back to the front.
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF COL_LENGTH(N'dbo.QueueEntries', N'NextNotifiedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[QueueEntries] ADD [NextNotifiedAt] [datetime2](7) NULL;
    -- Only when the column is first added: don't text everyone already at the front of a queue.
    EXEC(N'UPDATE q SET NextNotifiedAt = SYSUTCDATETIME()
          FROM dbo.QueueEntries q
          WHERE q.Status = 1
            AND q.Id = (SELECT TOP 1 w.Id FROM dbo.QueueEntries w
                        WHERE w.ShopId = q.ShopId AND w.Status = 1
                        ORDER BY w.QueueSortKey, w.Id);');
END
GO

-- Claims the first waiting customer for a "you're next" text.
-- Returns their entry (same shape as QueueEntriesGetById) only if they haven't been told yet; otherwise no row.
CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesClaimNextNotify]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    -- Lock this shop's waiting rows so two staff actions at once can't both send the text.
    DECLARE @NextId INT, @AlreadyNotified DATETIME2(7);
    SELECT TOP 1 @NextId = Id, @AlreadyNotified = NextNotifiedAt
    FROM dbo.QueueEntries WITH (UPDLOCK, HOLDLOCK)
    WHERE ShopId = @ShopId AND Status = 1
    ORDER BY QueueSortKey, Id;

    -- Anyone no longer at the front can be told again later.
    UPDATE dbo.QueueEntries
    SET NextNotifiedAt = NULL
    WHERE ShopId = @ShopId AND Status = 1 AND NextNotifiedAt IS NOT NULL
      AND (@NextId IS NULL OR Id <> @NextId);

    IF @NextId IS NULL OR @AlreadyNotified IS NOT NULL
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    UPDATE dbo.QueueEntries SET NextNotifiedAt = SYSUTCDATETIME() WHERE Id = @NextId;
    COMMIT TRANSACTION;

    EXEC dbo.QueueEntriesGetById @Id = @NextId;
END
GO

/* ##########################################################################################################
   PART 4. Security and race-condition fixes
   ########################################################################################################## */
/*
    Fixes from the October 2026 review. Run once against QUEUINGAPP after MIGRATION_QR_OTP_JOIN.sql,
    MIGRATION_SMS_MODE.sql and MIGRATION_NEXT_NOTIFY.sql. Safe to re-run.

    1. CreateCustomerJoinQueue: customers are recognised by their VERIFIED phone number, not by the (unverified)
       email. Before, typing someone else's email handed you their ticket and moved their texts to your phone.
       Joins for one shop are now serialised, so two people can't both take the last place, a double tap can't
       create two tickets, and the plan's waiting cap is enforced inside the transaction (@WaitingCap).
    2. PendingJoinsTakeAttempt: a code guess is counted BEFORE it is checked, so firing many guesses at once
       can't get past the 5-try limit.
    3. QueueEntriesUpdateStatus: only valid moves (waiting -> chair, waiting/chair -> served or cancelled), and the
       update only applies if the ticket hasn't changed in the meantime (no double "your turn" texts).
    4. CheckToken / GetShopFromAccount: deleted staff lose access straight away.
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/****** 1. Join the queue ******/
CREATE OR ALTER PROCEDURE [dbo].[CreateCustomerJoinQueue]
    @Name NVARCHAR(200),
    @Email NVARCHAR(255),
    @PhoneNumber NVARCHAR(30),
    @ShopId INT,
    @WaitingCap INT = 0    -- the plan's cap (0 = none); the shop's own QueueLimit applies too, whichever is lower
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @UserId INT;
        DECLARE @AlreadyWaiting BIT = 0;
        DECLARE @CreatedUser BIT = 0;
        DECLARE @QueueEntryId INT;
        DECLARE @QueueLimit INT = 0;
        DECLARE @WaitingNow INT = 0;
        DECLARE @ShopExists BIT = 0;

        -- Lock the shop row: joins for this shop now happen one at a time.
        SELECT @QueueLimit = QueueLimit, @ShopExists = 1
        FROM dbo.Shops WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @ShopId;
        IF @ShopExists = 0
        BEGIN
            RAISERROR(N'Shop not found.', 16, 1);
        END

        -- The phone number is the one they proved they own with the code.
        SELECT TOP 1 @UserId = Id
        FROM dbo.Users
        WHERE PhoneNumber = @PhoneNumber AND Role = N'Customer' AND Deleted = 0
        ORDER BY Id DESC;

        IF @UserId IS NULL
        BEGIN
            INSERT INTO dbo.Users (Name, Email, PhoneNumber, ShopId, Role, Created, Deleted)
            VALUES (@Name, @Email, @PhoneNumber, @ShopId, N'Customer', SYSUTCDATETIME(), 0);
            SET @UserId = CAST(SCOPE_IDENTITY() AS INT);
            SET @CreatedUser = 1;
        END
        ELSE
        BEGIN
            UPDATE dbo.Users
            SET Name = @Name,
                Email = @Email,
                ShopId = @ShopId
            WHERE Id = @UserId;
        END

        SELECT TOP 1 @QueueEntryId = Id
        FROM dbo.QueueEntries
        WHERE UserId = @UserId AND ShopId = @ShopId AND Status = 1
        ORDER BY JoinedAt DESC;

        IF @QueueEntryId IS NOT NULL
        BEGIN
            SET @AlreadyWaiting = 1;
        END
        ELSE
        BEGIN
            SELECT @WaitingNow = COUNT(*)
            FROM dbo.QueueEntries
            WHERE ShopId = @ShopId AND Status = 1;

            DECLARE @Cap INT = CASE
                WHEN ISNULL(@QueueLimit, 0) > 0 AND ISNULL(@WaitingCap, 0) > 0
                    THEN CASE WHEN @QueueLimit < @WaitingCap THEN @QueueLimit ELSE @WaitingCap END
                WHEN ISNULL(@QueueLimit, 0) > 0 THEN @QueueLimit
                ELSE ISNULL(@WaitingCap, 0) END;

            IF @Cap > 0 AND @WaitingNow >= @Cap
            BEGIN
                RAISERROR(N'This shop''s queue is full right now.', 16, 1);
            END

            INSERT INTO dbo.QueueEntries (UserId, ShopId, Status)
            VALUES (@UserId, @ShopId, 1);
            SET @QueueEntryId = CAST(SCOPE_IDENTITY() AS INT);
        END

        COMMIT TRANSACTION;

        SELECT
            @UserId AS UserId,
            @QueueEntryId AS QueueEntryId,
            @CreatedUser AS CreatedUser,
            @AlreadyWaiting AS AlreadyWaiting;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/****** 2. Count a code guess before checking it ******/
-- Returns the new number of attempts, or -1 if there are none left.
CREATE OR ALTER PROCEDURE [dbo].[PendingJoinsTakeAttempt]
    @Id INT,
    @MaxAttempts INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Used TABLE (Attempts INT);

    UPDATE dbo.PendingJoins
    SET Attempts = Attempts + 1
    OUTPUT inserted.Attempts INTO @Used
    WHERE Id = @Id AND Attempts < @MaxAttempts;

    SELECT ISNULL((SELECT TOP 1 Attempts FROM @Used), -1);
END
GO

/****** 3. Queue status changes ******/
CREATE OR ALTER PROCEDURE [dbo].[QueueEntriesUpdateStatus]
    @Id INT,
    @Status INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Current INT;
    SELECT @Current = Status FROM dbo.QueueEntries WHERE Id = @Id;
    IF @Current IS NULL
    BEGIN
        RAISERROR(N'Queue entry not found.', 16, 1);
        RETURN;
    END
    IF @Status NOT IN (2, 3, 4)
    BEGIN
        RAISERROR(N'That change isn''t allowed.', 16, 1);
        RETURN;
    END
    IF @Status = 2 AND @Current <> 1
    BEGIN
        RAISERROR(N'Only waiting customers can be taken to the chair.', 16, 1);
        RETURN;
    END
    IF @Status IN (3, 4) AND @Current NOT IN (1, 2)
    BEGIN
        RAISERROR(N'That visit is already closed.', 16, 1);
        RETURN;
    END

    -- Only if nobody else changed it since we looked (e.g. two staff tapping at once).
    UPDATE dbo.QueueEntries
    SET Status = @Status,
        ServedAt = CASE WHEN @Status = 3 THEN SYSUTCDATETIME() ELSE ServedAt END,
        ServingStartedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE ServingStartedAt END
    WHERE Id = @Id AND Status = @Current;

    IF @@ROWCOUNT = 0
    BEGIN
        RAISERROR(N'Someone else just updated this ticket. Refresh and try again.', 16, 1);
        RETURN;
    END

    EXEC dbo.QueueEntriesGetById @Id = @Id;
END
GO

/****** 4. Deleted staff lose access ******/
CREATE OR ALTER PROCEDURE [dbo].[CheckToken]
    @Token NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @AccountId BIGINT = 0;
    SELECT @AccountId = t.AccountId
    FROM dbo.Token t
    INNER JOIN dbo.Users u ON u.Id = t.AccountId AND u.Deleted = 0
    WHERE t.Token = @Token AND t.ValidTo > GETUTCDATE();
    SELECT ISNULL(@AccountId, 0);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[GetShopFromAccount]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.*,
           (SELECT COUNT(*) FROM dbo.QueueEntries q WHERE q.ShopId = s.Id AND q.Status = 1) AS WaitingCount
    FROM dbo.Shops s
    INNER JOIN dbo.Users u ON u.ShopId = s.Id
    WHERE u.Id = @Id AND u.Deleted = 0;
END
GO

/* ##########################################################################################################
   PART 5. Paid plans only (Limited / Full)
   ########################################################################################################## */
/*
    Paid plans only: Limited and Full. Run once against QUEUINGAPP after MIGRATION_QR_OTP_JOIN.sql. Safe to re-run.

    There's no Free plan any more. Shops.Plan says what the shop pays for: 'Limited', 'Full', or 'None' (nothing).
    New shops still get a 14-day Full trial (TrialEndsAt). Once the trial is over and there's no live subscription,
    the shop is expired: customers can't join and no texts are sent until it subscribes (see Helpers/Plans.cs).
    Staff can still sign in, finish the customers already waiting, and subscribe on the Billing page.
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Shops_Plan')
    ALTER TABLE [dbo].[Shops] DROP CONSTRAINT [CK_Shops_Plan];
GO
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_Shops_Plan')
    ALTER TABLE [dbo].[Shops] DROP CONSTRAINT [DF_Shops_Plan];
GO

UPDATE dbo.Shops SET [Plan] = N'None' WHERE [Plan] NOT IN (N'Limited', N'Full');
GO

ALTER TABLE [dbo].[Shops] ADD CONSTRAINT [DF_Shops_Plan] DEFAULT (N'None') FOR [Plan];
GO
ALTER TABLE [dbo].[Shops] WITH CHECK ADD CONSTRAINT [CK_Shops_Plan] CHECK ([Plan] IN (N'None', N'Limited', N'Full'));
GO

/* ##########################################################################################################
   PART 6. Business sign-up (RegisterShop)
   ########################################################################################################## */
/*
    Business sign-up ("Register your shop"). Run once against QUEUINGAPP after the other migrations. Safe to re-run.

    RegisterShop creates a new shop and its owner's staff login together, in one transaction:
      - the owner becomes the shop's admin (Shops.AdminId) and its first employee (Users.ShopId)
      - the shop gets a random QR code, a 6-character short code, and the 14-day Full trial (column default)
      - no location yet unless one is passed in; customers can't join until staff set it on the Shop page
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[RegisterShop]
    @OwnerName NVARCHAR(200),
    @Email NVARCHAR(255),
    @Password NVARCHAR(255),
    @Salt NVARCHAR(50),
    @PhoneNumber NVARCHAR(30) = NULL,
    @ShopName NVARCHAR(200),
    @Address NVARCHAR(300) = NULL,
    @Postcode NVARCHAR(10) = NULL,
    @Latitude DECIMAL(9, 6) = NULL,
    @Longitude DECIMAL(9, 6) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Same rule as staff sign-up: one staff login per email.
        IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
                   WHERE Email = @Email AND Role = N'Employee' AND Deleted = 0)
        BEGIN
            RAISERROR(N'That email is already in use.', 16, 1);
        END

        DECLARE @ShortCode NVARCHAR(6);
        EXEC dbo.ShopsNewShortCode @ShortCode = @ShortCode OUTPUT;

        INSERT INTO dbo.Shops (Name, AdminId, OpeningTime, ClosingTime, QueueLimit, LunchBreak, QrCode, ShortCode,
                               Address, Postcode, Latitude, Longitude, JoinRadiusMetres)
        VALUES (@ShopName, NULL, NULL, NULL, 0, NULL,
                LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N'')), @ShortCode,
                @Address, @Postcode, @Latitude, @Longitude, 150);
        DECLARE @ShopId INT = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.Users (Email, Name, Password, Salt, ShopId, PhoneNumber, Role, Created, Deleted)
        VALUES (@Email, @OwnerName, @Password, @Salt, @ShopId, @PhoneNumber, N'Employee', SYSUTCDATETIME(), 0);
        DECLARE @UserId INT = CAST(SCOPE_IDENTITY() AS INT);

        UPDATE dbo.Shops SET AdminId = @UserId WHERE Id = @ShopId;

        COMMIT TRANSACTION;

        SELECT @UserId AS UserId, @ShopId AS ShopId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* ##########################################################################################################
   PART 7. Stripe settings in the database (AppSettings Stripe keys, PlanPrices)
   ########################################################################################################## */
/*
    Stripe keys and price ids live here, so they can be changed with SQL instead of editing settings files.
    The API re-reads them every 30 seconds — no restart needed. Anything left NULL/empty here falls back to
    appsettings(.Local).json.

    Secret key / webhook signing secret (never commit these in a .sql file that goes to git):
        UPDATE dbo.AppSettings SET StripeSecretKey = N'sk_test_...', StripeWebhookSecret = N'whsec_...' WHERE Id = 1;

    Prices — one row per plan and billing period. New price in Stripe? Point the row at it:
        UPDATE dbo.PlanPrices SET StripePriceId = N'price_...' WHERE [Plan] = N'Full' AND BillingInterval = N'Monthly';
    Turn a period off (e.g. no yearly Limited):
        DELETE FROM dbo.PlanPrices WHERE [Plan] = N'Limited' AND BillingInterval = N'Yearly';
    See what's set:
        EXEC dbo.StripeSettingsGet;
*/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF COL_LENGTH(N'dbo.AppSettings', N'StripeSecretKey') IS NULL
    ALTER TABLE [dbo].[AppSettings] ADD [StripeSecretKey] [nvarchar](255) NULL;
GO
IF COL_LENGTH(N'dbo.AppSettings', N'StripeWebhookSecret') IS NULL
    ALTER TABLE [dbo].[AppSettings] ADD [StripeWebhookSecret] [nvarchar](255) NULL;
GO

IF OBJECT_ID(N'dbo.PlanPrices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PlanPrices](
        [Plan] [nvarchar](10) NOT NULL,
        [BillingInterval] [nvarchar](10) NOT NULL,
        [StripePriceId] [nvarchar](100) NOT NULL,
        [UpdatedAt] [datetime2](7) NOT NULL CONSTRAINT [DF_PlanPrices_UpdatedAt] DEFAULT (sysutcdatetime()),
     CONSTRAINT [PK_PlanPrices] PRIMARY KEY CLUSTERED ([Plan] ASC, [BillingInterval] ASC),
     CONSTRAINT [CK_PlanPrices_Plan] CHECK ([Plan] IN (N'Limited', N'Full')),
     CONSTRAINT [CK_PlanPrices_BillingInterval] CHECK ([BillingInterval] IN (N'Monthly', N'Yearly'))
    ) ON [PRIMARY];
END
GO

-- Stripe TEST-mode prices created for the app (account "New business"). Only added if that row is missing,
-- so re-running this never overwrites a price you've changed.
INSERT INTO dbo.PlanPrices ([Plan], BillingInterval, StripePriceId)
SELECT v.[Plan], v.BillingInterval, v.StripePriceId
FROM (VALUES
    (N'Limited', N'Monthly', N'price_1UNxIOIDc8sWc79fCGQpajjw'),
    (N'Limited', N'Yearly',  N'price_1UNxIOIDc8sWc79f1OrZt87j'),
    (N'Full',    N'Monthly', N'price_1UNxIPIDc8sWc79fCkEncRIq'),
    (N'Full',    N'Yearly',  N'price_1UNxIQIDc8sWc79fTldS4w09')
) AS v([Plan], BillingInterval, StripePriceId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PlanPrices p WHERE p.[Plan] = v.[Plan] AND p.BillingInterval = v.BillingInterval);
GO

CREATE OR ALTER TRIGGER [dbo].[TR_PlanPrices_Touch] ON [dbo].[PlanPrices]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL() > 1 RETURN;
    UPDATE p SET UpdatedAt = SYSUTCDATETIME()
    FROM dbo.PlanPrices p INNER JOIN inserted i ON i.[Plan] = p.[Plan] AND i.BillingInterval = p.BillingInterval;
END
GO

-- Everything the API needs from Stripe settings, as one row.
CREATE OR ALTER PROCEDURE [dbo].[StripeSettingsGet]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        s.StripeSecretKey AS SecretKey,
        s.StripeWebhookSecret AS WebhookSecret,
        (SELECT StripePriceId FROM dbo.PlanPrices WHERE [Plan] = N'Limited' AND BillingInterval = N'Monthly') AS LimitedMonthly,
        (SELECT StripePriceId FROM dbo.PlanPrices WHERE [Plan] = N'Limited' AND BillingInterval = N'Yearly') AS LimitedYearly,
        (SELECT StripePriceId FROM dbo.PlanPrices WHERE [Plan] = N'Full' AND BillingInterval = N'Monthly') AS FullMonthly,
        (SELECT StripePriceId FROM dbo.PlanPrices WHERE [Plan] = N'Full' AND BillingInterval = N'Yearly') AS FullYearly
    FROM dbo.AppSettings s
    WHERE s.Id = 1;
END
GO

/* ##########################################################################################################
   PART 8. Fixes from end-to-end testing
   ########################################################################################################## */
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- A text that was counted but Twilio didn't send gives its place in the monthly allowance back.
CREATE OR ALTER PROCEDURE [dbo].[ShopSmsRelease]
    @ShopId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Month DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
    UPDATE dbo.ShopSmsUsage
    SET Sent = Sent - 1
    WHERE ShopId = @ShopId AND [Month] = @Month AND Sent > 0;
END
GO

PRINT N'QUEUINGAPP database is up to date.';
GO
