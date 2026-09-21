namespace FastCom.Client.Shared.Components.Dashboard;

/// <summary>DTOs بتاع شاشة الهوم — مطابقة لردود DashboardController.</summary>
public class Summary
{
    public int OpenOperations { get; set; }
    public int DelayedOperations { get; set; }
    public int ClosedThisMonth { get; set; }
    public int OpenTrips { get; set; }
    public int TripsToday { get; set; }
    public int DelayedTrips { get; set; }
    public int OpenCustodies { get; set; }
    public decimal CustodyOutstanding { get; set; }
    public int InvoicesIssued { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public decimal Receivables { get; set; }
    public int OverdueCount { get; set; }
    public decimal CashBalance { get; set; }
    public int ActiveDrivers { get; set; }
    public int ActiveVehicles { get; set; }
    public int VehiclesInTrip { get; set; }
}

public class AlertItem
{
    public string Kind { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Date { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Days { get; set; }
    public string Severity { get; set; } = "";
    public string Route { get; set; } = "";
}

public class MonthFlow
{
    public string Month { get; set; } = "";
    public string MonthName { get; set; } = "";
    public decimal Collected { get; set; }
    public decimal Expenses { get; set; }
    public decimal Invoiced { get; set; }
}

public class TopCustomer
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Invoiced { get; set; }
    public decimal Paid { get; set; }
    public int Invoices { get; set; }
}

public class TrendPoint
{
    public decimal Current { get; set; }
    public decimal Previous { get; set; }
    public decimal? ChangePct { get; set; }
}

public class TrendsOut
{
    public TrendPoint Invoiced { get; set; } = new();
    public TrendPoint Collected { get; set; } = new();
    public TrendPoint Expenses { get; set; } = new();
    public TrendPoint ClosedOps { get; set; } = new();
}

public class StatusCount
{
    public string Status { get; set; } = "";
    public int Count { get; set; }
}

public class ActivityItem
{
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Description { get; set; } = "";
    public string UserName { get; set; } = "";
    public string At { get; set; } = "";
}

public class AgingOut
{
    public decimal Current { get; set; }
    public decimal D30 { get; set; }
    public decimal D60 { get; set; }
    public decimal D90 { get; set; }
    public decimal D90Plus { get; set; }
    public decimal Total { get; set; }
}
