/* End-customer manual name (free text) for bookings.
   Keeps EndCustomerId FK; when user types a name not linked to a customer, store it in EndCustomerName.
   Invoices display EndCustomerName ?? EndCustomer.NameAr ?? Customer.NameAr (display only; accounting stays on CustomerId).
   Safe to run multiple times. */
IF COL_LENGTH('dbo.Bookings', 'EndCustomerName') IS NULL
BEGIN
    ALTER TABLE dbo.Bookings
        ADD EndCustomerName nvarchar(200) NULL;
END
GO
