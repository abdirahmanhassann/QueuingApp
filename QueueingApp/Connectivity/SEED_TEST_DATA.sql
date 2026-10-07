/*
    Test data for the QR + location + OTP join flow.
    Run AFTER DBSCRIPT.txt and MIGRATION_QR_OTP_JOIN.sql (including its section 7). Safe to re-run (updates instead of duplicating).

    Creates:
      - Shop "Test Barbers" with QR code 'test-shop-qr'  -> customers open /join?code=test-shop-qr
        and short code TST-B4R (type it on the join page)
      - Staff login  test.staff@line.test / LinePass1!   (admin of the shop)
      - 3 waiting customers so positions show on the board

    BEFORE RUNNING: set @Latitude / @Longitude to where you'll be testing from
    (Google Maps: right-click your location -> click the coordinates to copy them).
    Or leave them and use Staff console -> Shop -> "Use this device's location" afterwards.
*/

SET NOCOUNT ON;

DECLARE @ShopName      NVARCHAR(200) = N'Test Barbers';
DECLARE @QrCode        NVARCHAR(100) = N'test-shop-qr';
DECLARE @ShortCode     NVARCHAR(6)   = N'TSTB4R';
DECLARE @Latitude      DECIMAL(9, 6) = 51.507400;   -- <-- change me
DECLARE @Longitude     DECIMAL(9, 6) = -0.127800;   -- <-- change me
DECLARE @RadiusMetres  INT           = 150;

DECLARE @StaffEmail    NVARCHAR(255) = N'test.staff@line.test';
DECLARE @StaffName     NVARCHAR(100) = N'Test Staff';
-- Password "LinePass1!" encrypted exactly as Security/Encryptor.Encrypt does with this salt.
DECLARE @StaffPassword NVARCHAR(255) = N'uIpzKmaPUisxwsVV/tOWpw==';
DECLARE @StaffSalt     NVARCHAR(255) = N'482917365019';

DECLARE @ShopId INT, @StaffId INT;

BEGIN TRY
    BEGIN TRANSACTION;

    /* Shop */
    SELECT @ShopId = Id FROM dbo.Shops WHERE QrCode = @QrCode;
    IF @ShopId IS NULL
    BEGIN
        UPDATE dbo.Shops SET ShortCode = NULL WHERE ShortCode = @ShortCode;
        INSERT INTO dbo.Shops (Name, AdminId, OpeningTime, ClosingTime, QueueLimit, LunchBreak, QrCode, ShortCode,
                               Address, Postcode, Latitude, Longitude, JoinRadiusMetres)
        VALUES (@ShopName, NULL, '2026-01-01T09:00:00', '2026-01-01T18:00:00', 20, NULL, @QrCode, @ShortCode,
                N'Test address', NULL, @Latitude, @Longitude, @RadiusMetres);
        SET @ShopId = CAST(SCOPE_IDENTITY() AS INT);
    END
    ELSE
    BEGIN
        UPDATE dbo.Shops SET ShortCode = NULL WHERE ShortCode = @ShortCode AND Id <> @ShopId;
        UPDATE dbo.Shops
        SET Name = @ShopName, ShortCode = @ShortCode, Latitude = @Latitude, Longitude = @Longitude, JoinRadiusMetres = @RadiusMetres
        WHERE Id = @ShopId;
    END

    /* Staff login */
    SELECT @StaffId = Id FROM dbo.Users WHERE Email = @StaffEmail AND Role = N'Employee';
    IF @StaffId IS NULL
    BEGIN
        INSERT INTO dbo.Users (Name, Email, Password, Salt, PhoneNumber, ShopId, Role, Created, Deleted)
        VALUES (@StaffName, @StaffEmail, @StaffPassword, @StaffSalt, NULL, @ShopId, N'Employee', SYSUTCDATETIME(), 0);
        SET @StaffId = CAST(SCOPE_IDENTITY() AS INT);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users
        SET Name = @StaffName, Password = @StaffPassword, Salt = @StaffSalt, ShopId = @ShopId, Deleted = 0
        WHERE Id = @StaffId;
    END

    UPDATE dbo.Shops SET AdminId = @StaffId WHERE Id = @ShopId;

    /* Waiting customers (only added if they aren't already waiting here) */
    DECLARE @Customers TABLE (Name NVARCHAR(100), Email NVARCHAR(255), PhoneNumber NVARCHAR(20), MinutesAgo INT);
    INSERT INTO @Customers VALUES
        (N'Test Customer One',   N'test.customer1@line.test', N'+447700900001', 20),
        (N'Test Customer Two',   N'test.customer2@line.test', N'+447700900002', 12),
        (N'Test Customer Three', N'test.customer3@line.test', N'+447700900003', 5);
    -- +44 7700 900xxx is Ofcom's reserved range for fiction/testing: these numbers never receive real texts.

    INSERT INTO dbo.Users (Name, Email, PhoneNumber, ShopId, Role, Created, Deleted)
    SELECT c.Name, c.Email, c.PhoneNumber, @ShopId, N'Customer', SYSUTCDATETIME(), 0
    FROM @Customers c
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Email = c.Email AND u.Role = N'Customer' AND u.Deleted = 0);

    INSERT INTO dbo.QueueEntries (UserId, ShopId, Status, JoinedAt)
    SELECT u.Id, @ShopId, 1, DATEADD(MINUTE, -c.MinutesAgo, GETUTCDATE())
    FROM @Customers c
    INNER JOIN dbo.Users u ON u.Email = c.Email AND u.Role = N'Customer' AND u.Deleted = 0
    WHERE NOT EXISTS (SELECT 1 FROM dbo.QueueEntries q WHERE q.UserId = u.Id AND q.ShopId = @ShopId AND q.Status IN (1, 2));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH

/* What you've got */
SELECT s.Id AS ShopId, s.Name, s.QrCode, s.ShortCode, s.Latitude, s.Longitude, s.JoinRadiusMetres, s.AdminId,
       '/join?code=' + s.QrCode AS JoinPath
FROM dbo.Shops s WHERE s.Id = @ShopId;

SELECT Id, Name, Email, Role, ShopId FROM dbo.Users WHERE Id = @StaffId;

SELECT q.Id AS TicketId, u.Name, q.Status, q.JoinedAt, q.AccessKey
FROM dbo.QueueEntries q INNER JOIN dbo.Users u ON u.Id = q.UserId
WHERE q.ShopId = @ShopId AND q.Status IN (1, 2)
ORDER BY q.JoinedAt;
GO


/* ---------------------------------------------------------------------------
   CLEAN UP — removes everything above plus any test joins made against the shop.
   Select from here down and run it on its own when you're finished.
--------------------------------------------------------------------------- */
/*
DECLARE @ShopId INT = (SELECT Id FROM dbo.Shops WHERE QrCode = N'test-shop-qr');
DECLARE @StaffId INT = (SELECT Id FROM dbo.Users WHERE Email = N'test.staff@line.test' AND Role = N'Employee');

BEGIN TRANSACTION;
    DELETE FROM dbo.PendingJoins WHERE ShopId = @ShopId;
    DELETE FROM dbo.QueueEntries WHERE ShopId = @ShopId;
    DELETE FROM dbo.Token WHERE AccountId = @StaffId;
    DELETE u FROM dbo.Users u
    WHERE u.Email LIKE N'test.customer_@line.test' AND u.Role = N'Customer'
      AND NOT EXISTS (SELECT 1 FROM dbo.QueueEntries q WHERE q.UserId = u.Id);
    DELETE FROM dbo.Users WHERE Id = @StaffId;
    DELETE FROM dbo.Shops WHERE Id = @ShopId;
COMMIT TRANSACTION;
*/
