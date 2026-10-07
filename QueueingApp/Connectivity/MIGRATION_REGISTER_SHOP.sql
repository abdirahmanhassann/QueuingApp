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
