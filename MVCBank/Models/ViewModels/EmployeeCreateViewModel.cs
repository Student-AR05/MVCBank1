using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MVCBank.Models.ViewModels
{
    public class EmployeeCreateViewModel
    {
        public string EmpName { get; set; }
        public string DeptID { get; set; }
        public string EmpType { get; set; }
        public string PAN { get; set; }
    }
}