using MVCBank.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Text.RegularExpressions;


namespace MVCBank.Controllers
{
    public class AuthController : Controller
    {
        BankDbEntities db = new BankDbEntities();

        [HttpGet]
        public ActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Login(string RoleType, string UserID, string Password)
        {
            if (string.IsNullOrEmpty(Password))
            {
                ViewBag.Message = "Password is required.";
                return View();
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(Password);

            if (RoleType == "CUSTOMER")
            {
                var cust = db.Customers.FirstOrDefault(c => c.CustID == UserID);
                if (cust == null)
                {
                    ViewBag.Message = "Customer not found.";
                    return View();
                }

                if (cust.CustPassword == null || !cust.CustPassword.SequenceEqual(passwordBytes))
                {
                    ViewBag.Message = "Incorrect password for customer.";
                    return View();
                }

                Session["UserID"] = cust.CustID;
                Session["UserName"] = cust.CustName;
                Session["Role"] = "CUSTOMER";
                // redirect to Customer dashboard
                return RedirectToAction("Dashboard", "Customer");
            }
            else if (RoleType == "EMPLOYEE")
            {
                // only employees (type E)
                var emp = db.Employees.FirstOrDefault(e => e.EmpID == UserID);
                if (emp == null)
                {
                    ViewBag.Message = "Employee not found.";
                    return View();
                }

                var empType = (emp.EmpType ?? string.Empty).Trim().ToUpperInvariant();
                if (empType != "E")
                {
                    ViewBag.Message = "User is not an employee (EmpType mismatch).";
                    return View();
                }

                if (emp.EmpPassword == null || !emp.EmpPassword.SequenceEqual(passwordBytes))
                {
                    ViewBag.Message = "Incorrect password for employee.";
                    return View();
                }

                Session["UserID"] = emp.EmpID;
                Session["UserName"] = emp.EmpName;
                Session["Role"] = "EMPLOYEE";
                Session["DeptID"] = emp.DeptID; // store dept for later use

                // determine department and redirect to department-specific controller
                var dept = db.Departments.FirstOrDefault(d => d.DeptID == emp.DeptID);
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

                // fallback to generic Employee controller
                return RedirectToAction("Index", "Employee");
            }
            else if (RoleType == "MANAGER")
            {
                var mgr = db.Employees.FirstOrDefault(e => e.EmpID == UserID);
                if (mgr == null)
                {
                    ViewBag.Message = "Manager not found.";
                    return View();
                }

                var mgrType = (mgr.EmpType ?? string.Empty).Trim().ToUpperInvariant();
                if (mgrType != "M")
                {
                    ViewBag.Message = "User is not a manager (EmpType mismatch).";
                    return View();
                }

                if (mgr.EmpPassword == null || !mgr.EmpPassword.SequenceEqual(passwordBytes))
                {
                    ViewBag.Message = "Incorrect password for manager.";
                    return View();
                }

                Session["UserID"] = mgr.EmpID;
                Session["UserName"] = mgr.EmpName;
                Session["Role"] = "MANAGER";
                // redirect to Manager controller
                return RedirectToAction("Dashboard", "Manager");
            }

            ViewBag.Message = "Invalid role selected.";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            // Clear and abandon the session to logout user
            Session.Clear();
            Session.Abandon();

            // remove auth cookie if any
            if (Request.Cookies[".ASPXAUTH"] != null)
            {
                var c = new HttpCookie(".ASPXAUTH") { Expires = DateTime.Now.AddDays(-1) };
                Response.Cookies.Add(c);
            }

            return RedirectToAction("Login", "Auth");
        }



        [HttpGet]
        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Register(Customer c)
        {
            try
            {
                // Normalize and validate input
                c.CustName = (c.CustName ?? string.Empty).Trim();
                c.PAN = (c.PAN ?? string.Empty).Trim().ToUpperInvariant();
                c.PhoneNumber = (c.PhoneNumber ?? string.Empty).Trim(); 
                c.Address = (c.Address ?? string.Empty).Trim();

                var plain = Request.Form["PlainPassword"];

                // Required checks
                if (string.IsNullOrWhiteSpace(c.CustName))
                    ModelState.AddModelError("CustName", "Customer name is required.");

                if (c.DOB == default(DateTime))
                    ModelState.AddModelError("DOB", "Date of birth is required.");

                if (string.IsNullOrWhiteSpace(c.PAN))
                    ModelState.AddModelError("PAN", "PAN is required.");

                if (string.IsNullOrWhiteSpace(c.PhoneNumber))
                    ModelState.AddModelError("PhoneNumber", "Phone number is required.");

                if (string.IsNullOrWhiteSpace(c.Address))
                    ModelState.AddModelError("Address", "Address is required.");

                if (string.IsNullOrEmpty(plain))
                    ModelState.AddModelError("CustPassword", "Password is required.");

                // Ensure a gender option was selected (radio group posts a value only when selected)
                if (Request.Form["Gender"] == null)
                    ModelState.AddModelError("Gender", "Please select gender.");

                // Name format: letters and spaces only, at least 2 characters
                var namePattern = new Regex("^[A-Za-z][A-Za-z ]{1,}$");
                if (!string.IsNullOrEmpty(c.CustName) && !namePattern.IsMatch(c.CustName))
                    ModelState.AddModelError("CustName", "Name must contain only letters and spaces, min 2 characters.");

                // DOB: Age must be 18+
                if (c.DOB != default(DateTime))
                {
                    var today = DateTime.Today;
                    int age = today.Year - c.DOB.Year;
                    if (c.DOB > today.AddYears(-age)) age--;
                    if (age < 18)
                        ModelState.AddModelError("DOB", "You must be at least 18 years old.");
                }

                // PAN format AAAA1234
                var panPattern = new Regex("^[A-Z]{4}[0-9]{4}$");
                if (!string.IsNullOrEmpty(c.PAN) && !panPattern.IsMatch(c.PAN))
                    ModelState.AddModelError("PAN", "PAN must be in format AAAA1234.");

                // Phone number: exactly 10 digits
                var phonePattern = new Regex("^\\d{10}$");
                if (!string.IsNullOrEmpty(c.PhoneNumber) && !phonePattern.IsMatch(c.PhoneNumber))
                    ModelState.AddModelError("PhoneNumber", "Phone number must be exactly 10 digits.");

                // Duplicate PAN check (optional, avoids SQL errors)
                if (!string.IsNullOrEmpty(c.PAN) && db.Customers.Any(x => x.PAN == c.PAN))
                    ModelState.AddModelError("PAN", "PAN already exists for another customer.");

                // Prevent employees or managers from registering as customers using same PAN
                if (!string.IsNullOrEmpty(c.PAN))
                {
                    var conflictingEmp = db.Employees.FirstOrDefault(e => e.PAN == c.PAN);
                    if (conflictingEmp != null)
                    {
                        var etype = (conflictingEmp.EmpType ?? string.Empty).Trim().ToUpperInvariant();
                        if (etype == "E" || etype == "M")
                        {
                            ModelState.AddModelError("PAN", "Employees and Managers cannot register as customers.");
                        }
                    }
                }

                if (!ModelState.IsValid)
                {
                    return View(c);
                }

                c.CustPassword = Encoding.UTF8.GetBytes(plain);


                var sql = @"
                INSERT INTO dbo.Customer (CustName, DOB, PAN, PhoneNumber, Gender, Address, CustPassword)
                VALUES (@CustName, @DOB, @PAN, @PhoneNumber, @Gender, @Address, @CustPassword)";

                var parameters = new[]
                {
                new SqlParameter("@CustName", (object)c.CustName ?? DBNull.Value),
                new SqlParameter("@DOB", (object)c.DOB ?? DBNull.Value),
                new SqlParameter("@PAN", (object)c.PAN ?? DBNull.Value),
                new SqlParameter("@PhoneNumber", (object)c.PhoneNumber ?? DBNull.Value),
                new SqlParameter("@Gender", c.Gender),
                new SqlParameter("@Address", (object)c.Address ?? DBNull.Value),
                new SqlParameter("@CustPassword", SqlDbType.VarBinary) { Value = (object)c.CustPassword ?? DBNull.Value }
            };

                int i = db.Database.ExecuteSqlCommand(sql, parameters);

                if (i > 0)
                {
                    var newCustomer = db.Customers.FirstOrDefault(x => x.PAN == c.PAN);
                    ViewBag.Message = "Customer Registered Successfully";
                    ViewBag.CustID = newCustomer?.CustID;
                }
                else
                {
                    ViewBag.Message = "Error in Registration";
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = "Registration failed: " + (ex.InnerException?.Message ?? ex.Message);
            }

            return View(c);
        }
    }
}