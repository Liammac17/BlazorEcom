-- Optional database hardening for MyShop's existing eShop database.
-- This script is NOT run by the application. Back up the database before applying it.
-- Review it against your deployment and run with sqlcmd -b so errors stop execution.
-- It preserves table names and IDs, validates existing data, and rolls back as a unit.
USE [eShop];
GO
-- Filtered indexes require these session options regardless of legacy database defaults.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.Product WHERE Price < 0 OR ABS(Price - ROUND(Price, 2)) > 0.0000001)
       OR EXISTS (SELECT 1 FROM dbo.OrderLineItem WHERE Price < 0 OR ABS(Price - ROUND(Price, 2)) > 0.0000001)
        THROW 51000, 'Prices must be nonnegative and have at most two decimal places before conversion.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Product WHERE Price >= 10000000000000000)
       OR EXISTS (SELECT 1 FROM dbo.OrderLineItem WHERE Price >= 10000000000000000)
        THROW 51001, 'A price exceeds decimal(18,2). No conversion was applied.', 1;
    IF EXISTS (SELECT 1 FROM dbo.OrderLineItem WHERE Quantity <= 0)
        THROW 51002, 'Invalid line-item quantities must be corrected first.', 1;
    IF EXISTS (SELECT 1 FROM dbo.OrderLineItem li LEFT JOIN dbo.[Order] o ON o.OrderId = li.OrderId WHERE o.OrderId IS NULL)
       OR EXISTS (SELECT 1 FROM dbo.OrderLineItem li LEFT JOIN dbo.Product p ON p.ProductId = li.ProductId WHERE p.ProductId IS NULL)
        THROW 51003, 'Orphaned line items must be corrected first.', 1;
    IF EXISTS (SELECT UniqueId FROM dbo.[Order] WHERE UniqueId IS NOT NULL GROUP BY UniqueId HAVING COUNT(*) > 1)
        THROW 51004, 'Duplicate order identifiers must be corrected first.', 1;

    -- Skip already-converted columns so reruns do not conflict with the constraints
    -- and indexes installed below.
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Product')
               AND name = N'Price' AND (system_type_id <> TYPE_ID(N'decimal') OR [precision] <> 18 OR scale <> 2 OR is_nullable <> 0))
        ALTER TABLE dbo.Product ALTER COLUMN Price decimal(18,2) NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.OrderLineItem')
               AND name = N'Price' AND (system_type_id <> TYPE_ID(N'decimal') OR [precision] <> 18 OR scale <> 2 OR is_nullable <> 0))
        ALTER TABLE dbo.OrderLineItem ALTER COLUMN Price decimal(18,2) NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Product_Price')
        ALTER TABLE dbo.Product WITH CHECK ADD CONSTRAINT CK_Product_Price CHECK (Price >= 0);
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrderLineItem_Price')
        ALTER TABLE dbo.OrderLineItem WITH CHECK ADD CONSTRAINT CK_OrderLineItem_Price CHECK (Price >= 0);
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrderLineItem_Quantity')
        ALTER TABLE dbo.OrderLineItem WITH CHECK ADD CONSTRAINT CK_OrderLineItem_Quantity CHECK (Quantity > 0);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OrderLineItem_Order')
        ALTER TABLE dbo.OrderLineItem WITH CHECK ADD CONSTRAINT FK_OrderLineItem_Order FOREIGN KEY (OrderId) REFERENCES dbo.[Order](OrderId);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_OrderLineItem_Product')
        ALTER TABLE dbo.OrderLineItem WITH CHECK ADD CONSTRAINT FK_OrderLineItem_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product(ProductId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'UX_Order_UniqueId')
        CREATE UNIQUE INDEX UX_Order_UniqueId ON dbo.[Order](UniqueId) WHERE UniqueId IS NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OrderLineItem') AND name = N'IX_OrderLineItem_OrderId')
        CREATE INDEX IX_OrderLineItem_OrderId ON dbo.OrderLineItem(OrderId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'IX_Order_StatusDate')
        CREATE INDEX IX_Order_StatusDate ON dbo.[Order](DateProcessed, DatePlaced, OrderId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
