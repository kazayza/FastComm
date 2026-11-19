-- Add OPERATION.VIEW_FINANCIALS permission (idempotent).
-- Grants visibility of revenue/cost/profit in operations/bookings/transport requests.
-- Operations staff should NOT receive this; finance/admin roles should.
IF NOT EXISTS (SELECT 1 FROM AppPermissions WHERE PermissionCode = N'OPERATION.VIEW_FINANCIALS')
BEGIN
    DECLARE @maxSort int = ISNULL((SELECT MAX(SortOrder) FROM AppPermissions WHERE ModuleCode = N'OPERATION'), 907);
    INSERT INTO AppPermissions (PermissionCode, NameAr, NameEn, ModuleCode, ActionCode, SortOrder)
    VALUES (N'OPERATION.VIEW_FINANCIALS', N'عرض المبالغ والأرباح التشغيلية', N'View Operation Financials', N'OPERATION', N'VIEW_FINANCIALS', @maxSort + 1);
END;
GO

DECLARE @permId int = (SELECT PermissionId FROM AppPermissions WHERE PermissionCode = N'OPERATION.VIEW_FINANCIALS');
IF @permId IS NOT NULL
BEGIN
    -- Grant to roles that already manage/approve finance or operations at manager level.
    INSERT INTO AppRolePermissions (RoleId, PermissionId)
    SELECT r.Id, @permId
    FROM AspNetRoles r
    WHERE r.RoleCode IN (N'ADMIN', N'MANAGER', N'OPSMGR', N'FINMGR', N'ACCT')
      AND NOT EXISTS (SELECT 1 FROM AppRolePermissions arp WHERE arp.RoleId = r.Id AND arp.PermissionId = @permId);
END;
GO

SELECT PermissionId, PermissionCode, NameAr FROM AppPermissions WHERE PermissionCode = N'OPERATION.VIEW_FINANCIALS';
