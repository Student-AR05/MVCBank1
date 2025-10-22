using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MVCBank.Models;
using System.Data.Entity.Infrastructure;
using System.Text.RegularExpressions;
using System.Data.SqlClient;
using System.Text;
using MVCBank.Models.ViewModels;
using MVCBank.Filters;

namespace MVCBank.Controllers
{
    [SessionAuthorize(RolesCsv = "MANAGER")]
    public class ManagerController : Controller
    {
        private BankDbEntities db = new BankDbEntities();

        // GET: Manager
        public ActionResult Index()
        {
            return View();
        }

        // Dashboard action used after login
        public ActionResult Dashboard()
        {
            var vm = new ManagerDashboardViewModel
            {
                TotalCustomers = db.Customers.Count(),
                TotalEmployees = db.Employees.Count(e => (e.EmpType ?? "").Trim().ToUpper() == "E"),
                ManagersCount = db.Employees.Count(e => (e.EmpType ?? "").Trim().ToUpper() == "M"),
                ActiveSavingsAccounts = db.SavingsAccounts.Count(a => (a.Status ?? "").ToLower() != "closed"),
                ActiveFDAccounts = db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() != "closed"),
                ActiveLoanAccounts = db.LoanAccounts.Count(a => (a.Status ?? "").ToLower() != "closed"),
                PendingAccounts = db.SavingsAccounts.Count(a => (a.Status ?? "").ToLower() == "pending")
                                 + db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() == "pending")
                                 + db.LoanAccounts.Count(a => (a.Status ?? "").ToLower() == "pending"),
                TotalSavingsBalance = db.SavingsAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.Balance).Sum() ?? 0m,
                TotalFDDeposits = db.FixedDepositAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.DepositAmount).Sum() ?? 0m,
                TotalLoanPrincipal = db.LoanAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.LoanAmount).Sum() ?? 0m,
                AsOf = DateTime.Now
            };

            return View(vm);
        }

        // View all accounts with search
        [HttpGet]
        public ActionResult ViewAccounts(string accountType, string q)
        {
            var selectedType = (accountType ?? "ALL").Trim().ToUpperInvariant();
            var lowered = (q ?? string.Empty).Trim().ToLowerInvariant();
            ViewBag.AccountTypeSel = selectedType;
            ViewBag.Query = q;

            var accounts = new List<AccountListItemViewModel>();

            if (selectedType == "SAVINGS" || selectedType == "ALL")
            {
                var query = db.SavingsAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.SBAccountID.ToLower().Contains(lowered)
                                           || a.CustomerID.ToLower().Contains(lowered));
                }
                accounts.AddRange(query
                    .OrderBy(a => a.SBAccountID)
                    .Take(500)
                    .ToList()
                    .Select(a => new AccountListItemViewModel
                    {
                        Id = a.SBAccountID,
                        CustomerID = a.CustomerID,
                        Status = a.Status,
                        Col1 = "Balance",
                        Val1 = (decimal?)a.Balance
                    }));
            }

            if (selectedType == "FD" || selectedType == "ALL")
            {
                var query = db.FixedDepositAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.FDAccountID.ToLower().Contains(lowered)
                                           || a.CustomerID.ToLower().Contains(lowered));
                }
                accounts.AddRange(query
                    .OrderBy(a => a.FDAccountID)
                    .Take(500)
                    .ToList()
                    .Select(a => new AccountListItemViewModel
                    {
                        Id = a.FDAccountID,
                        CustomerID = a.CustomerID,
                        Status = a.Status,
                        Col1 = "Deposit",
                        Val1 = (decimal?)a.DepositAmount
                    }));
            }

            if (selectedType == "LOAN" || selectedType == "ALL")
            {
                var query = db.LoanAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.LNAccountID.ToLower().Contains(lowered)
                                           || a.CustomerID.ToLower().Contains(lowered));
                }
                accounts.AddRange(query
                    .OrderBy(a => a.LNAccountID)
                    .Take(500)
                    .ToList()
                    .Select(a => new AccountListItemViewModel
                    {
                        Id = a.LNAccountID,
                        CustomerID = a.CustomerID,
                        Status = a.Status,
                        Col1 = "Loan Amount",
                        Val1 = (decimal?)a.LoanAmount
                    }));
            }

            // order combined list consistently
            var ordered = accounts
                .OrderBy(a => a.Id)
                .Take(1000)
                .ToList();

            return View(ordered);
        }

        // Open account page (admin creates accounts)
        [HttpGet]
        public ActionResult OpenAccount()
        {
            ViewBag.Customers = db.Customers.ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OpenAccount(FormCollection form)
        {
            ViewBag.Customers = db.Customers.ToList();

            var customerOption = (form["customerOption"] ?? string.Empty).Trim().ToUpper(); // EXISTING or NEW
            var accountType = (form["accountType"] ?? string.Empty).Trim().ToUpper(); // SAVINGS, FD, LOAN

            string custId = null;

            try
            {
                if (customerOption == "EXISTING")
                {
                    custId = form["ExistingCustomerID"];
                    if (string.IsNullOrEmpty(custId))
                    {
                        ModelState.AddModelError("", "Select an existing customer.");
                        return View();
                    }
                    var c = db.Customers.FirstOrDefault(x => x.CustID == custId);
                    if (c == null)
                    {
                        ModelState.AddModelError("", "Selected customer not found.");
                        return View();
                    }
                }
                else // NEW
                {
                    // Collect customer fields
                    var name = form["New_CustName"];
                    var dobStr = form["New_DOB"];
                    var pan = (form["New_PAN"] ?? string.Empty).Trim().ToUpper();
                    var phone = form["New_PhoneNumber"];
                    var genderStr = form["New_Gender"];
                    var address = form["New_Address"];
                    var providedPassword = form["New_Password"];

                    DateTime dob;
                    if (!DateTime.TryParse(dobStr, out dob))
                    {
                        ModelState.AddModelError("", "Invalid date of birth for new customer.");
                        return View();
                    }

                    bool gender = false;
                    if (!bool.TryParse(genderStr, out gender)) gender = false;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(address))
                    {
                        ModelState.AddModelError("", "Name, phone and address are required for new customer.");
                        return View();
                    }

                    var panPattern = new Regex("^[A-Z]{4}[0-9]{4}$");
                    if (!panPattern.IsMatch(pan))
                    {
                        ModelState.AddModelError("", "PAN must be in format AAAA1234 for new customer.");
                        return View();
                    }

                    if (db.Customers.Any(c => c.PAN == pan))
                    {
                        ModelState.AddModelError("", "PAN already exists for another customer.");
                        return View();
                    }

                    string pwd = string.IsNullOrEmpty(providedPassword) ? GenerateRandomPassword(8) : providedPassword;
                    var pwdBytes = System.Text.Encoding.UTF8.GetBytes(pwd);

                    var sql = @"INSERT INTO dbo.Customer (CustName, DOB, PAN, PhoneNumber, Gender, Address, CustPassword)
                                VALUES (@CustName, @DOB, @PAN, @PhoneNumber, @Gender, @Address, @CustPassword)";

                    var parameters = new[] {
                        new SqlParameter("@CustName", (object)name ?? DBNull.Value),
                        new SqlParameter("@DOB", (object)dob ?? DBNull.Value),
                        new SqlParameter("@PAN", (object)pan ?? DBNull.Value),
                        new SqlParameter("@PhoneNumber", (object)phone ?? DBNull.Value),
                        new SqlParameter("@Gender", gender),
                        new SqlParameter("@Address", (object)address ?? DBNull.Value),
                        new SqlParameter("@CustPassword", System.Data.SqlDbType.VarBinary) { Value = (object)pwdBytes ?? DBNull.Value }
                    };

                    db.Database.ExecuteSqlCommand(sql, parameters);

                    var newCust = db.Customers.FirstOrDefault(x => x.PAN == pan);
                    if (newCust == null)
                    {
                        ModelState.AddModelError("", "Failed to create new customer.");
                        return View();
                    }

                    custId = newCust.CustID;
                    ViewBag.NewCustomerPassword = pwd;
                }

                // Create account depending on accountType
                if (accountType == "SAVINGS")
                {
                    decimal initial = 0;
                    decimal.TryParse(form["initialDeposit"], out initial);
                    if (initial < 1000)
                    {
                        ModelState.AddModelError("", "Minimum initial deposit for savings is 1000.");
                        return View();
                    }

                    var sql = @"INSERT INTO dbo.SavingsAccount (CustomerID, Balance, Status) VALUES (@CustomerID, @Balance, @Status)";
                    var parameters = new[] {
                        new SqlParameter("@CustomerID", custId),
                        new SqlParameter("@Balance", initial),
                        new SqlParameter("@Status", "Active")
                    };

                    try
                    {
                        db.Database.ExecuteSqlCommand(sql, parameters);
                    }
                    catch (SqlException sqlex)
                    {
                        // 2601 and 2627 indicate duplicate key / unique constraint violations
                        if (sqlex.Number == 2601 || sqlex.Number == 2627)
                        {
                            // find existing savings account for this customer and show friendly message
                            var existing = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == custId);
                            if (existing != null)
                            {
                                ModelState.AddModelError("", $"A savings account already exists for this customer (Account ID: {existing.SBAccountID}).");
                            }
                            else
                            {
                                ModelState.AddModelError("", "A savings account already exists for this customer.");
                            }

                            return View();
                        }

                        throw; // rethrow other SqlExceptions
                    }

                    var acc = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == custId);
                    ViewBag.SuccessMessage = "Savings account added successfully.";
                    ViewBag.CreatedAccountID = acc?.SBAccountID;
                    return View();
                }

                if (accountType == "FD")
                {
                    decimal amount = 0; int months = 0;
                    decimal.TryParse(form["fdAmount"], out amount);
                    int.TryParse(form["fdDuration"], out months);

                    if (amount < 10000)
                    {
                        ModelState.AddModelError("", "Minimum FD deposit is 10,000.");
                        return View();
                    }
                    if (months <= 0)
                    {
                        ModelState.AddModelError("", "Duration must be at least 1 month.");
                        return View();
                    }

                    // Get customer DOB for senior citizen check
                    Customer fdCustomer = db.Customers.FirstOrDefault(c => c.CustID == custId);
                    if (fdCustomer == null)
                    {
                        ModelState.AddModelError("", "Customer not found for FD.");
                        return View();
                    }
                    var today = DateTime.Today;
                    int age = today.Year - fdCustomer.DOB.Year;
                    if (fdCustomer.DOB > today.AddYears(-age)) age--;
                    bool isSenior = age >= 60;

                    // Determine ROI
                    decimal roi = 0;
                    if (months <= 12) roi = 6m;
                    else if (months <= 24) roi = 7m;
                    else roi = 8m;
                    if (isSenior) roi += 0.5m;

                    // Calculate maturity amount (compound interest, annually)
                    double principal = (double)amount;
                    double rate = (double)roi / 100.0;
                    double years = months / 12.0;
                    double maturity = principal * Math.Pow(1 + rate, years);
                    decimal maturityAmount = (decimal)Math.Round(maturity, 2);

                    var start = DateTime.Now.Date;
                    var end = start.AddMonths(months);

                    // Insert FD without FDAccountID (let DB generate it)
                    var sql = @"INSERT INTO dbo.FixedDepositAccount (CustomerID, StartDate, EndDate, DepositAmount, FDROI, Status) VALUES (@CustomerID, @StartDate, @EndDate, @DepositAmount, @FDROI, @Status)";
                    var parameters = new[] {
                        new SqlParameter("@CustomerID", custId),
                        new SqlParameter("@StartDate", start),
                        new SqlParameter("@EndDate", end),
                        new SqlParameter("@DepositAmount", amount),
                        new SqlParameter("@FDROI", roi),
                        new SqlParameter("@Status", "Active")
                    };
                    db.Database.ExecuteSqlCommand(sql, parameters);

                    // Fetch the newly created FD account
                    var acc = db.FixedDepositAccounts
                        .Where(a => a.CustomerID == custId && a.StartDate == start && a.DepositAmount == amount)
                        .OrderByDescending(a => a.FDNum)
                        .FirstOrDefault();

                    ViewBag.SuccessMessage = "Fixed deposit created successfully.";
                    ViewBag.CreatedAccountID = acc?.FDAccountID;
                    ViewBag.FDMaturityAmount = maturityAmount;
                    ViewBag.FDROI = roi;
                    ViewBag.FDMaturityDate = end.ToString("yyyy-MM-dd");
                    return View();
                }

                if (accountType == "LOAN")
                {
                    decimal amount = 0; int months = 0; decimal roi = 0;
                    decimal.TryParse(form["loanAmount"], out amount);
                    int.TryParse(form["loanTenure"], out months);
                    decimal.TryParse(form["loanROI"], out roi);

                    if (amount < 10000)
                    {
                        ModelState.AddModelError("", "Minimum loan amount is 10000.");
                        return View();
                    }

                    decimal emi = months == 0 ? 0 : Math.Round((amount * roi / 100) / months, 2);
                    var start = DateTime.Now.Date;

                    var sql = @"INSERT INTO dbo.LoanAccount (CustomerID, LoanAmount, StartDate, TenureMonths, LNROI, EMIAmount, Status) VALUES (@CustomerID, @LoanAmount, @StartDate, @TenureMonths, @LNROI, @EMIAmount, @Status)";
                    var parameters = new[] {
                        new SqlParameter("@CustomerID", custId),
                        new SqlParameter("@LoanAmount", amount),
                        new SqlParameter("@StartDate", start),
                        new SqlParameter("@TenureMonths", months),
                        new SqlParameter("@LNROI", roi),
                        new SqlParameter("@EMIAmount", emi),
                        new SqlParameter("@Status", "Active")
                    };

                    db.Database.ExecuteSqlCommand(sql, parameters);

                    var acc = db.LoanAccounts.FirstOrDefault(a => a.CustomerID == custId && a.StartDate == start && a.LoanAmount == amount);
                    ViewBag.SuccessMessage = "Loan account created successfully.";
                    ViewBag.CreatedAccountID = acc?.LNAccountID;
                    return View();
                }

                ModelState.AddModelError("", "Unknown account type.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to add account: " + inner);
            }

            return View();
        }

        // Add employee - GET
        [HttpGet]
        public ActionResult AddEmployee()
        {
            ViewBag.Departments = db.Departments.ToList();

            // carry over temp data if redirected after create
            if (TempData["CreatedEmpID"] != null)
            {
                ViewBag.CreatedEmpID = TempData["CreatedEmpID"];
                ViewBag.CreatedEmpPassword = TempData["CreatedEmpPassword"];
                ViewBag.SuccessMessage = "Employee created successfully.";
            }

            return View();
        }

        // Add employee - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddEmployee(FormCollection form)
        {
            ViewBag.Departments = db.Departments.ToList();

            var name = (form["EmpName"] ?? string.Empty).Trim();
            var deptId = (form["DeptID"] ?? string.Empty).Trim();
            var pan = (form["PAN"] ?? string.Empty).Trim().ToUpper();
            var providedPwd = form["Password"];

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(deptId))
            {
                ModelState.AddModelError("", "Name and Department are required.");
                return View();
            }

            if (!string.IsNullOrEmpty(pan) && db.Employees.Any(e => e.PAN == pan))
            {
                ModelState.AddModelError("", "An employee with this PAN already exists.");
                return View();
            }

            // ensure PAN is not used by any customer as well
            if (!string.IsNullOrEmpty(pan) && db.Customers.Any(c => c.PAN == pan))
            {
                ModelState.AddModelError("", "This PAN is already registered to a customer and cannot be used for an employee.");
                return View();
            }

            var pwd = string.IsNullOrEmpty(providedPwd) ? GenerateRandomPassword(8) : providedPwd;
            var pwdBytes = Encoding.UTF8.GetBytes(pwd);

            var sql = @"INSERT INTO dbo.Employee (EmpName, DeptID, EmpType, PAN, EmpPassword) VALUES (@EmpName, @DeptID, @EmpType, @PAN, @EmpPassword)";
            var parameters = new[] {
                new SqlParameter("@EmpName", (object)name ?? DBNull.Value),
                new SqlParameter("@DeptID", (object)deptId ?? DBNull.Value),
                new SqlParameter("@EmpType", "E"),
                new SqlParameter("@PAN", (object)pan ?? DBNull.Value),
                new SqlParameter("@EmpPassword", System.Data.SqlDbType.VarBinary) { Value = (object)pwdBytes ?? DBNull.Value }
            };

            try
            {
                db.Database.ExecuteSqlCommand(sql, parameters);

                // fetch created employee by PAN if provided, otherwise by name (best-effort)
                Employee created = null;
                if (!string.IsNullOrEmpty(pan)) created = db.Employees.FirstOrDefault(e => e.PAN == pan);
                if (created == null) created = db.Employees.FirstOrDefault(e => e.EmpName == name && e.DeptID == deptId);

                if (created == null)
                {
                    ModelState.AddModelError("", "Employee created but failed to retrieve generated ID.");
                    return View();
                }

                TempData["CreatedEmpID"] = created.EmpID;
                TempData["CreatedEmpPassword"] = pwd;

                // redirect to GET to show success (Post/Redirect/Get)
                return RedirectToAction("AddEmployee");
            }
            catch (SqlException sqlex)
            {
                var msg = sqlex.InnerException?.Message ?? sqlex.Message;
                ModelState.AddModelError("", "Failed to create employee: " + msg);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to create employee: " + inner);
            }

            return View();
        }

        // Close account - GET (searchable)
        [HttpGet]
        [ActionName("CloseAccount")]
        public ActionResult CloseAccountSearch(string accountType, string q)
        {
            var selectedType = (accountType ?? string.Empty).Trim().ToUpperInvariant();
            ViewBag.AccountTypeSel = selectedType;
            ViewBag.Query = q;

            var accounts = new List<AccountListItemViewModel>();
            var lowered = (q ?? string.Empty).Trim().ToLowerInvariant();

            if (selectedType == "SAVINGS")
            {
                var query = db.SavingsAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.SBAccountID.ToLower().Contains(lowered) || a.CustomerID.ToLower().Contains(lowered));
                }
                var list = query.OrderBy(a => a.SBAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.SBAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Balance",
                    Val1 = (decimal?)a.Balance
                }).ToList();
            }
            else if (selectedType == "FD")
            {
                var query = db.FixedDepositAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.FDAccountID.ToLower().Contains(lowered) || a.CustomerID.ToLower().Contains(lowered));
                }
                var list = query.OrderBy(a => a.FDAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.FDAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Deposit",
                    Val1 = (decimal?)a.DepositAmount
                }).ToList();
            }
            else if (selectedType == "LOAN")
            {
                var query = db.LoanAccounts.AsQueryable();
                if (!string.IsNullOrEmpty(lowered))
                {
                    query = query.Where(a => a.LNAccountID.ToLower().Contains(lowered) || a.CustomerID.ToLower().Contains(lowered));
                }
                var list = query.OrderBy(a => a.LNAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.LNAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Loan Amount",
                    Val1 = (decimal?)a.LoanAmount
                }).ToList();
            }

            ViewBag.Accounts = accounts;
            return View();
        }

        // Close account - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CloseAccount(string accountType, string accountId)
        {
            if (string.IsNullOrEmpty(accountType) || string.IsNullOrEmpty(accountId))
            {
                ModelState.AddModelError("", "Account type and Account ID are required.");
                // Reload search data on error
                ViewBag.AccountTypeSel = accountType?.Trim().ToUpperInvariant() ?? "";
                ViewBag.Accounts = new List<AccountListItemViewModel>();
                return View();
            }

            accountType = accountType.Trim().ToUpper();
            accountId = accountId.Trim();

            try
            {
                if (accountType == "SAVINGS")
                {
                    var acc = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Savings account not found.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    if ((acc.Status ?? "").Trim().ToLower() == "closed")
                    {
                        ModelState.AddModelError("", $"Savings account {accountId} is already closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    var rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.SavingsAccount SET Status = @status WHERE SBAccountID = @id",
                        new SqlParameter("@status", "Closed"),
                        new SqlParameter("@id", accountId));
                    if (rows <= 0)
                    {
                        ModelState.AddModelError("", "Savings account could not be closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    ViewBag.SuccessMessage = $"Savings account {accountId} closed.";
                }
                else if (accountType == "FD")
                {
                    var acc = db.FixedDepositAccounts.FirstOrDefault(a => a.FDAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Fixed deposit account not found.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    if ((acc.Status ?? "").Trim().ToLower() == "closed")
                    {
                        ModelState.AddModelError("", $"Fixed deposit account {accountId} is already closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    var rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.FixedDepositAccount SET Status = @status WHERE FDAccountID = @id",
                        new SqlParameter("@status", "Closed"),
                        new SqlParameter("@id", accountId));
                    if (rows <= 0)
                    {
                        ModelState.AddModelError("", "Fixed deposit account could not be closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    ViewBag.SuccessMessage = $"Fixed deposit account {accountId} closed.";
                }
                else if (accountType == "LOAN")
                {
                    var acc = db.LoanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Loan account not found.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    if ((acc.Status ?? "").Trim().ToLower() == "closed")
                    {
                        ModelState.AddModelError("", $"Loan account {accountId} is already closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    var rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.LoanAccount SET Status = @status WHERE LNAccountID = @id",
                        new SqlParameter("@status", "Closed"),
                        new SqlParameter("@id", accountId));
                    if (rows <= 0)
                    {
                        ModelState.AddModelError("", "Loan account could not be closed.");
                        ViewBag.AccountTypeSel = accountType;
                        ViewBag.Accounts = new List<AccountListItemViewModel>();
                        return View();
                    }
                    ViewBag.SuccessMessage = $"Loan account {accountId} closed.";
                }
                else
                {
                    ModelState.AddModelError("", "Unknown account type.");
                    ViewBag.AccountTypeSel = "";
                    ViewBag.Accounts = new List<AccountListItemViewModel>();
                    return View();
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to close account: " + inner);
                ViewBag.AccountTypeSel = accountType;
                ViewBag.Accounts = new List<AccountListItemViewModel>();
                return View();
            }

            // After successful close, reload the accounts list for the same type
            ViewBag.AccountTypeSel = accountType;
            ViewBag.Query = "";
            var accounts = new List<AccountListItemViewModel>();
            if (accountType == "SAVINGS")
            {
                var list = db.SavingsAccounts.OrderBy(a => a.SBAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.SBAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Balance",
                    Val1 = (decimal?)a.Balance
                }).ToList();
            }
            else if (accountType == "FD")
            {
                var list = db.FixedDepositAccounts.OrderBy(a => a.FDAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.FDAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Deposit",
                    Val1 = (decimal?)a.DepositAmount
                }).ToList();
            }
            else if (accountType == "LOAN")
            {
                var list = db.LoanAccounts.OrderBy(a => a.LNAccountID).Take(200).ToList();
                accounts = list.Select(a => new AccountListItemViewModel {
                    Id = a.LNAccountID,
                    CustomerID = a.CustomerID,
                    Status = a.Status,
                    Col1 = "Loan Amount",
                    Val1 = (decimal?)a.LoanAmount
                }).ToList();
            }
            ViewBag.Accounts = accounts;
            return View();
        }

        // Remove employee page - GET (searchable)
        [HttpGet]
        [ActionName("RemoveEmployee")]
        public ActionResult RemoveEmployeeList(string q)
        {
            IQueryable<Employee> query = db.Employees.Where(e => (e.EmpType ?? "").Trim().ToUpper() == "E");

            q = (q ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(q))
            {
                var lowered = q.ToLower();
                query = query.Where(e => e.EmpID.ToLower().Contains(lowered)
                                       || e.EmpName.ToLower().Contains(lowered)
                                       || (e.DeptID ?? "").ToLower().Contains(lowered)
                                       || (e.PAN ?? "").ToLower().Contains(lowered));
                ViewBag.Query = q;
            }

            ViewBag.Employees = query.OrderBy(e => e.EmpName).Take(200).ToList();
            return View();
        }

        // Remove employee - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("RemoveEmployee")]
        public ActionResult RemoveEmployeePost(string empId)
        {
            if (string.IsNullOrEmpty(empId))
            {
                ModelState.AddModelError("", "Select an employee to remove.");
                ViewBag.Employees = db.Employees.Where(e => (e.EmpType ?? "").Trim().ToUpper() == "E").OrderBy(e => e.EmpName).Take(200).ToList();
                return View();
            }

            try
            {
                var emp = db.Employees.FirstOrDefault(e => e.EmpID == empId);
                if (emp == null)
                {
                    ModelState.AddModelError("", "Employee not found.");
                    ViewBag.Employees = db.Employees.Where(e => (e.EmpType ?? "").Trim().ToUpper() == "E").OrderBy(e => e.EmpName).Take(200).ToList();
                    return View();
                }

                // prevent deleting managers or non-employee types
                var type = (emp.EmpType ?? string.Empty).Trim().ToUpper();
                if (type != "E")
                {
                    ModelState.AddModelError("", "Only employee type 'E' can be removed here.");
                    ViewBag.Employees = db.Employees.Where(e => (e.EmpType ?? "").Trim().ToUpper() == "E").OrderBy(e => e.EmpName).Take(200).ToList();
                    return View();
                }

                db.Employees.Remove(emp);
                db.SaveChanges();

                ViewBag.SuccessMessage = $"Employee {emp.EmpID} removed successfully.";
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to remove employee: " + inner);
            }

            ViewBag.Employees = db.Employees.Where(e => (e.EmpType ?? "").Trim().ToUpper() == "E").OrderBy(e => e.EmpName).Take(200).ToList();
            return View();
        }

        // View pending requests (accounts, employees, etc.)
        [HttpGet]
        public ActionResult PendingRequests()
        {
            // gather all Pending from SB/FD/LOAN tables
            var pendingList = new List<PendingAccountRequestViewModel>();

            var savings = (from a in db.SavingsAccounts
                           join c in db.Customers on a.CustomerID equals c.CustID
                           where (a.Status ?? "").ToLower() == "pending"
                           select new PendingAccountRequestViewModel
                           {
                               AccountID = a.SBAccountID,
                               AccountType = "SAVINGS",
                               CustomerID = c.CustID,
                               CustomerName = c.CustName,
                               Amount = a.Balance,
                               StartDate = null
                           }).ToList();

            var fds = (from a in db.FixedDepositAccounts
                       join c in db.Customers on a.CustomerID equals c.CustID
                       where (a.Status ?? "").ToLower() == "pending"
                       select new PendingAccountRequestViewModel
                       {
                           AccountID = a.FDAccountID,
                           AccountType = "FD",
                           CustomerID = c.CustID,
                           CustomerName = c.CustName,
                           Amount = a.DepositAmount,
                           StartDate = a.StartDate
                       }).ToList();

            var loans = (from a in db.LoanAccounts
                         join c in db.Customers on a.CustomerID equals c.CustID
                         where (a.Status ?? "").ToLower() == "pending"
                         select new PendingAccountRequestViewModel
                         {
                             AccountID = a.LNAccountID,
                             AccountType = "LOAN",
                             CustomerID = c.CustID,
                             CustomerName = c.CustName,
                             Amount = a.LoanAmount,
                             StartDate = a.StartDate
                         }).ToList();

            pendingList.AddRange(savings);
            pendingList.AddRange(fds);
            pendingList.AddRange(loans);

            var ordered = pendingList
                .OrderBy(p => p.AccountType)
                .ThenBy(p => p.CustomerName)
                .ToList();

            return View(ordered);
        }

        // Approve/Reject pending account request
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateAccountRequest(string accountType, string accountId, string decision)
        {
            if (string.IsNullOrWhiteSpace(accountType) || string.IsNullOrWhiteSpace(accountId))
            {
                TempData["ErrorMessage"] = "Account Type and ID are required.";
                return RedirectToAction("PendingRequests");
            }

            var nextStatus = (decision ?? "").Trim().ToLower() == "approve" ? "Active" : "Rejected";
            accountType = accountType.Trim().ToUpperInvariant();

            try
            {
                int rows = 0;
                if (accountType == "SAVINGS")
                {
                    rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.SavingsAccount SET Status = @status WHERE SBAccountID = @id",
                        new SqlParameter("@status", nextStatus),
                        new SqlParameter("@id", accountId));
                }
                else if (accountType == "FD")
                {
                    rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.FixedDepositAccount SET Status = @status WHERE FDAccountID = @id",
                        new SqlParameter("@status", nextStatus),
                        new SqlParameter("@id", accountId));
                }
                else if (accountType == "LOAN")
                {
                    rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.LoanAccount SET Status = @status WHERE LNAccountID = @id",
                        new SqlParameter("@status", nextStatus),
                        new SqlParameter("@id", accountId));
                }
                else
                {
                    TempData["ErrorMessage"] = "Unsupported account type.";
                    return RedirectToAction("PendingRequests");
                }

                if (rows <= 0)
                {
                    TempData["ErrorMessage"] = "Account not found or not updated.";
                }
                else
                {
                    TempData["SuccessMessage"] = $"Request {(nextStatus == "Active" ? "approved" : "rejected")} for {accountType} {accountId}.";
                }
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                TempData["ErrorMessage"] = "Failed to update request: " + msg;
            }

            return RedirectToAction("PendingRequests");
        }

        // new: search existing customers by query (id, name or PAN)
        [HttpGet]
        public JsonResult SearchCustomers(string q)
        {
            q = (q ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
            {
                return Json(new { results = new object[0] }, JsonRequestBehavior.AllowGet);
            }

            var lowered = q.ToLower();
            var matches = db.Customers
                            .Where(c => c.CustID.ToLower().Contains(lowered) || c.CustName.ToLower().Contains(lowered) || c.PAN.ToLower().Contains(lowered))
                            .OrderBy(c => c.CustName)
                            .Take(50)
                            .Select(c => new {
                                id = c.CustID,
                                text = c.CustName + " (" + c.CustID + ")",
                                pan = c.PAN,
                                phone = c.PhoneNumber
                            })
                            .ToList();

            return Json(new { results = matches }, JsonRequestBehavior.AllowGet);
        }

        // Remove customer - GET with optional search
        [HttpGet]
        public ActionResult RemoveCustomer(string q)
        {
            q = (q ?? string.Empty).Trim();
            IQueryable<Customer> query = db.Customers;
            if (!string.IsNullOrEmpty(q))
            {
                var lowered = q.ToLower();
                query = query.Where(c => c.CustID.ToLower().Contains(lowered)
                                       || c.CustName.ToLower().Contains(lowered)
                                       || c.PAN.ToLower().Contains(lowered)
                                       || (c.PhoneNumber ?? "").ToLower().Contains(lowered));
                ViewBag.Query = q;
            }

            var customers = query.OrderBy(c => c.CustName).Take(200).ToList();
            ViewBag.Customers = customers;
            return View();
        }

        // Remove customer - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("RemoveCustomer")]
        public ActionResult RemoveCustomerPost(string custId)
        {
            if (string.IsNullOrEmpty(custId))
            {
                ModelState.AddModelError("", "Select a customer to remove.");
                ViewBag.Customers = db.Customers.OrderBy(c => c.CustName).Take(200).ToList();
                return View();
            }

            try
            {
                var customer = db.Customers.FirstOrDefault(c => c.CustID == custId);
                if (customer == null)
                {
                    ModelState.AddModelError("", "Customer not found.");
                    ViewBag.Customers = db.Customers.OrderBy(c => c.CustName).Take(200).ToList();
                    return View();
                }

                // optional: prevent delete when accounts exist
                bool hasAccounts = db.SavingsAccounts.Any(a => a.CustomerID == custId)
                                 || db.FixedDepositAccounts.Any(a => a.CustomerID == custId)
                                 || db.LoanAccounts.Any(a => a.CustomerID == custId);
                if (hasAccounts)
                {
                    ModelState.AddModelError("", "Cannot remove customer with existing accounts. Close accounts first.");
                    ViewBag.Customers = db.Customers.OrderBy(c => c.CustName).Take(200).ToList();
                    return View();
                }

                db.Customers.Remove(customer);
                db.SaveChanges();

                ViewBag.SuccessMessage = $"Customer {custId} removed successfully.";
            }
            catch (SqlException sqlEx)
            {
                // FK violation
                if (sqlEx.Number == 547)
                {
                    ModelState.AddModelError("", "Cannot remove customer due to related records (accounts/transactions). Close accounts first.");
                }
                else
                {
                    ModelState.AddModelError("", "Failed to remove customer: " + (sqlEx.InnerException?.Message ?? sqlEx.Message));
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Failed to remove customer: " + (ex.InnerException?.Message ?? ex.Message));
            }

            ViewBag.Customers = db.Customers.OrderBy(c => c.CustName).Take(200).ToList();
            return View();
        }

        // Edit Customer - GET
        [HttpGet]
        public ActionResult EditCustomer(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("RemoveCustomer");
            var c = db.Customers.FirstOrDefault(x => x.CustID == id);
            if (c == null) return RedirectToAction("RemoveCustomer");
            var vm = new CustomerEditViewModel
            {
                CustID = c.CustID,
                CustName = c.CustName,
                DOB = c.DOB,
                PAN = c.PAN,
                PhoneNumber = c.PhoneNumber,
                Address = c.Address,
                Gender = c.Gender
            };
            return View(vm);
        }

        // Edit Customer - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditCustomer(CustomerEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            // Check for duplicate PAN
            if (db.Customers.Any(x => x.PAN == model.PAN && x.CustID != model.CustID))
            {
                ModelState.AddModelError("PAN", "PAN already exists for another customer.");
                return View(model);
            }

            try
            {
                var rows = db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Customer
                      SET CustName = @CustName,
                          DOB = @DOB,
                          PAN = @PAN,
                          PhoneNumber = @PhoneNumber,
                          Address = @Address,
                          Gender = @Gender
                      WHERE CustID = @CustID",
                    new SqlParameter("@CustName", (object)model.CustName ?? DBNull.Value),
                    new SqlParameter("@DOB", model.DOB),
                    new SqlParameter("@PAN", (object)model.PAN ?? DBNull.Value),
                    new SqlParameter("@PhoneNumber", (object)model.PhoneNumber ?? DBNull.Value),
                    new SqlParameter("@Address", (object)model.Address ?? DBNull.Value),
                    new SqlParameter("@Gender", model.Gender),
                    new SqlParameter("@CustID", model.CustID)
                );

                if (rows <= 0)
                {
                    ModelState.AddModelError("", "Customer not found or not updated.");
                    return View(model);
                }

                ViewBag.SuccessMessage = "Customer updated successfully.";
            }
            catch (SqlException sqlEx)
            {
                var msg = sqlEx.InnerException?.Message ?? sqlEx.Message;
                ModelState.AddModelError("", "Failed to update customer: " + msg);
            }
            catch (Exception ex)
            {
                string errorMsg = ex.Message;
                var inner = ex.InnerException;
                while (inner != null)
                {
                    errorMsg += " --> " + inner.Message;
                    inner = inner.InnerException;
                }
                ModelState.AddModelError("", "Failed to update customer: " + errorMsg);
            }
            return View(model);
        }

        // Edit Employee - GET
        [HttpGet]
        public ActionResult EditEmployee(string id)
        {
            if (string.IsNullOrEmpty(id)) return RedirectToAction("RemoveEmployee");
            var e = db.Employees.FirstOrDefault(x => x.EmpID == id);
            if (e == null) return RedirectToAction("RemoveEmployee");
            var vm = new EmployeeEditViewModel
            {
                EmpID = e.EmpID,
                EmpName = e.EmpName,
                DeptID = e.DeptID,
                PAN = e.PAN
            };
            return View(vm);
        }

        // Edit Employee - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditEmployee(EmployeeEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Optional duplicate PAN check within employees only
            if (!string.IsNullOrEmpty(model.PAN) && db.Employees.Any(x => x.PAN == model.PAN && x.EmpID != model.EmpID))
            {
                ModelState.AddModelError("PAN", "PAN already exists for another employee.");
                return View(model);
            }

            try
            {
                var rows = db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Employee
                      SET EmpName = @EmpName,
                          DeptID = @DeptID,
                          PAN = @PAN
                      WHERE EmpID = @EmpID",
                    new SqlParameter("@EmpName", (object)model.EmpName ?? DBNull.Value),
                    new SqlParameter("@DeptID", (object)model.DeptID ?? DBNull.Value),
                    new SqlParameter("@PAN", (object)model.PAN ?? DBNull.Value),
                    new SqlParameter("@EmpID", model.EmpID)
                );

                if (rows <= 0)
                {
                    ModelState.AddModelError("", "Employee not found or not updated.");
                    return View(model);
                }

                ViewBag.SuccessMessage = "Employee updated successfully.";
            }
            catch (SqlException sqlEx)
            {
                var msg = sqlEx.InnerException?.Message ?? sqlEx.Message;
                ModelState.AddModelError("", "Failed to update employee: " + msg);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to update employee: " + inner);
            }
            return View(model);
        }

        private string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var rnd = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[rnd.Next(s.Length)]).ToArray());
        }
    }
}