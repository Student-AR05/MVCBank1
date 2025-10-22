using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using MVCBank.Models;

namespace MVCBank.Models.ViewModels
{
    public class CustomerDashboardViewModel
    {
        public SavingsAccount SavingsAccount { get; set; }
        public List<SavingsTransaction> Transactions { get; set; }
    }
}
