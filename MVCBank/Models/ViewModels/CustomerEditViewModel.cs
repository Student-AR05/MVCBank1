using System;
using System.ComponentModel.DataAnnotations;

namespace MVCBank.Models.ViewModels
{
    public class CustomerEditViewModel
    {
        [Required]
        public string CustID { get; set; }
        [Required]
        public string CustName { get; set; }
        [Required]
        public DateTime DOB { get; set; }
        [Required]
        public string PAN { get; set; }
        [Required]
        public string PhoneNumber { get; set; }
        [Required]
        public string Address { get; set; }
        public bool Gender { get; set; }
    }
}