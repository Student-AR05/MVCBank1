using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MVCBank.Models;
using MVCBank.Filters;

namespace MVCBank.Controllers
{
    [SessionAuthorize(RolesCsv = "EMPLOYEE")]
    public class EmployeeController : Controller
    {
        private BankDbEntities db = new BankDbEntities();

        // GET: Employee
        public ActionResult Index()
        {
            return View();
        }

        // Dashboard action used after login for employees
        public ActionResult Dashboard()
        {
            // ensure user is an employee
            var role = Session["Role"] as string;
            if (string.IsNullOrEmpty(role) || !role.Equals("EMPLOYEE", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Login", "Auth");
            }

            var deptId = Session["DeptID"] as string;
            if (!string.IsNullOrEmpty(deptId))
            {
                var dept = db.Departments.FirstOrDefault(d => d.DeptID == deptId);
                if (dept != null && !string.IsNullOrEmpty(dept.DeptName))
                {
                    var name = dept.DeptName.ToLowerInvariant();
                    if (name.Contains("saving") || name.Contains("savings") || name.Contains("sb"))
                    {
                        return RedirectToAction("Index", "SavingEmployee");
                    }
                    if (name.Contains("loan"))
                    {
                        return RedirectToAction("Index", "LoanEmployee");
                    }
                }
            }

            // fallback to generic employee view
            return View();
        }
    }
}
