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
