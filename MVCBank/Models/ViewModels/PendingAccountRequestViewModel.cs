using System;

namespace MVCBank.Models.ViewModels
{
    public class PendingAccountRequestViewModel
    {
        public string AccountID { get; set; }
        public string AccountType { get; set; } // SAVINGS, FD, LOAN
        public string CustomerID { get; set; }
        public string CustomerName { get; set; }
        public decimal? Amount { get; set; } // Deposit/Loan amount when applicable
        public DateTime? StartDate { get; set; } // When applicable
    }
}