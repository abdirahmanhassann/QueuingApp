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
