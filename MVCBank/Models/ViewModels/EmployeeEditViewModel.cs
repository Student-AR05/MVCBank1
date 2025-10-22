using System.ComponentModel.DataAnnotations;

namespace MVCBank.Models.ViewModels
{
    public class EmployeeEditViewModel
    {
        [Required]
        public string EmpID { get; set; }
        [Required]
        public string EmpName { get; set; }
        [Required]
        public string DeptID { get; set; }
        [Required]
        public string PAN { get; set; }
    }
}