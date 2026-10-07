/*
    SMS live / demo switch. Run once against QUEUINGAPP. Safe to re-run (keeps the current setting).

    dbo.AppSettings is a single-row table (Id is always 1).
      SmsLive = 1  -> texts really go out through Twilio (verification codes, "you're next", staff pings).
      SmsLive = 0  -> demo mode: nothing is sent to Twilio. Verification codes are shown on screen instead,
                      notification texts are skipped, and staff pings are only shown on the customer's ticket.

    Starts in demo mode. To go live:   UPDATE dbo.AppSettings SET SmsLive = 1;
    To go back to demo:                UPDATE dbo.AppSettings SET SmsLive = 0;
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
