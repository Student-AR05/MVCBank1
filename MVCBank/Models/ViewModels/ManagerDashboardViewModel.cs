using System;

namespace MVCBank.Models.ViewModels
{
    public class ManagerDashboardViewModel
    {
        public int TotalCustomers { get; set; }
        public int TotalEmployees { get; set; }
        public int ManagersCount { get; set; }
        public int ActiveSavingsAccounts { get; set; }
        public int ActiveFDAccounts { get; set; }
        public int ActiveLoanAccounts { get; set; }
        public int PendingAccounts { get; set; }
        public decimal TotalSavingsBalance { get; set; }
        public decimal TotalFDDeposits { get; set; }
        public decimal TotalLoanPrincipal { get; set; }
        public DateTime AsOf { get; set; }
    }
}