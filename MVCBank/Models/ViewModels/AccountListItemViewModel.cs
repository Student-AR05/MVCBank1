using System;

namespace MVCBank.Models.ViewModels
{
    public class AccountListItemViewModel
    {
        public string Id { get; set; }
        public string CustomerID { get; set; }
        public string Status { get; set; }
        public string Col1 { get; set; }
        public decimal? Val1 { get; set; }
    }
}