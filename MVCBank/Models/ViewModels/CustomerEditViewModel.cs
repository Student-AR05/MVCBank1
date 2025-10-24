using System;
using System.ComponentModel.DataAnnotations;
using MVCBank.Models.Validation;

namespace MVCBank.Models.ViewModels
{
    public class CustomerEditViewModel
    {
        [Required]
        public string CustID { get; set; }
        [Required]
        [RegularExpression("^[A-Za-z][A-Za-z ]{1,}$", ErrorMessage = "Name must contain only letters and spaces, min 2 characters.")]
        public string CustName { get; set; }
        [Required]
        [MinAge(18, ErrorMessage = "Customer must be at least 18 years old.")]
        public DateTime DOB { get; set; }
        [Required]
        public string PAN { get; set; }
        [Required]
        [RegularExpression("^\\d{10}$", ErrorMessage = "Phone number must be exactly 10 digits.")]
        public string PhoneNumber { get; set; }
        [Required]
        public string Address { get; set; }
        public bool Gender { get; set; }
    }
}