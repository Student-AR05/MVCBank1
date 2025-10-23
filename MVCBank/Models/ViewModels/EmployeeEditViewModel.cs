using System.ComponentModel.DataAnnotations;

namespace MVCBank.Models.ViewModels
{
    public class EmployeeEditViewModel
    {
        [Required]
        public string EmpID { get; set; }
        [Required]
        [RegularExpression("^[A-Za-z][A-Za-z ]{1,}$", ErrorMessage = "Name must contain only letters and spaces, min 2 characters.")]
        public string EmpName { get; set; }
        [Required]
        public string DeptID { get; set; }
        [Required]
        public string PAN { get; set; }
    }
}