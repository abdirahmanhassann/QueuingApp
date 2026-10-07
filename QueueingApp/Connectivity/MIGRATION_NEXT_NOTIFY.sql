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
    ALTER TABLE [dbo].[QueueEntries] ADD [NextNotifiedAt] [datetime2](7) NULL;
GO

-- Don't text everyone already at the front of a queue the first time this runs.
UPDATE q SET NextNotifiedAt = SYSUTCDATETIME()
FROM dbo.QueueEntries q
WHERE q.Status = 1
  AND q.NextNotifiedAt IS NULL
  AND q.Id = (SELECT TOP 1 w.Id FROM dbo.QueueEntries w
              WHERE w.ShopId = q.ShopId AND w.Status = 1
              ORDER BY w.QueueSortKey, w.Id);
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
