using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MVCBank.Models;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Data.Entity.Validation;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using MVCBank.Filters;

namespace MVCBank.Controllers
{
    [SessionAuthorize(RolesCsv = "EMPLOYEE")]
    public class SavingEmployeeController : Controller
    {
        private BankDbEntities db = new BankDbEntities();

        // GET: SavingEmployee
        public ActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // Dashboard for savings employee
        public ActionResult Dashboard()
        {
            ViewBag.ActiveSavingsAccounts = db.SavingsAccounts.Count(a => (a.Status ?? "").ToLower() != "closed");
            ViewBag.ActiveFDAccounts = db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() != "closed");
            ViewBag.TotalSavingsBalance = db.SavingsAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.Balance).Sum() ?? 0m;
            ViewBag.TotalFDDeposits = db.FixedDepositAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.DepositAmount).Sum() ?? 0m;
            ViewBag.PendingAccounts = db.SavingsAccounts.Count(a => (a.Status ?? "").ToLower() == "pending")
                                      + db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() == "pending");
            return View("Dashboard");
        }

        // Add account (GET)
        [HttpGet]
        public ActionResult AddAccount()
        {
            ViewBag.Customers = db.Customers.ToList();
            return View();
        }

        // Add account (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddAccount(FormCollection form)
        {
            ViewBag.Customers = db.Customers.ToList();

            var customerOption = (form["customerOption"] ?? string.Empty).Trim().ToUpper(); // EXISTING or NEW
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

                // New validation: no customer can have more than 1 savings account
                if (!string.IsNullOrEmpty(custId))
                {
                    var existingAccount = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == custId);
                    if (existingAccount != null)
                    {
                        ModelState.AddModelError("", "Customer already has a savings account.");
                        return View();
                    }
                }

                // Create savings account
                decimal initial = 0;
                decimal.TryParse(form["initialDeposit"], out initial);
                if (initial < 1000)
                {
                    ModelState.AddModelError("", "Minimum initial deposit for savings is 1000.");
                    return View();
                }

                var sqlAcc = @"INSERT INTO dbo.SavingsAccount (CustomerID, Balance, Status) VALUES (@CustomerID, @Balance, @Status)";
                var accParams = new[] {
                    new SqlParameter("@CustomerID", custId),
                    new SqlParameter("@Balance", initial),
                    new SqlParameter("@Status", "Active")
                };

                db.Database.ExecuteSqlCommand(sqlAcc, accParams);

                var acc = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == custId && a.Balance == initial);
                ViewBag.SuccessMessage = "Savings account added successfully.";
                ViewBag.CreatedAccountID = acc?.SBAccountID;
                return View();
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to add savings account: " + inner);
            }

            return View();
        }

        // Close account (GET)
        [HttpGet]
        public ActionResult CloseAccount()
        {
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
                        return View();
                    }

                    acc.Status = "Closed";
                    db.SaveChanges();

                    ViewBag.SuccessMessage = $"Savings account {accountId} closed successfully.";
                    return View();
                }

                ModelState.AddModelError("", "Not authorized to close accounts of this type.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to close account: " + inner);
            }

            return View();
        }

        // Deposit (GET)
        [HttpGet]
        public ActionResult Deposit()
        {
            return View();
        }

        // Deposit (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Deposit(FormCollection form)
        {
            var accountId = form["accountId"];
            decimal amount = 0;
            decimal.TryParse(form["amount"], out amount);

            if (string.IsNullOrEmpty(accountId) || amount <= 0)
            {
                ModelState.AddModelError("", "Valid Account ID and amount must be provided for deposit.");
                return View();
            }

            try
            {
                // verify account exists (read-only EF is fine)
                var acc = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == accountId);
                if (acc == null)
                {
                    ModelState.AddModelError("", "Savings account not found.");
                    return View();
                }

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // update balance using raw SQL to avoid EF trying to manage computed PK on related inserts
                        var updateSql = "UPDATE dbo.SavingsAccount SET Balance = Balance + @p0 WHERE SBAccountID = @p1";
                        var rows = db.Database.ExecuteSqlCommand(updateSql, amount, accountId);
                        if (rows == 0)
                        {
                            tx.Rollback();
                            ModelState.AddModelError("", "Failed to update account balance.");
                            return View();
                        }

                        // insert transaction without specifying TransactionID (DB will compute it)
                        var insertSql = @"INSERT INTO dbo.SavingsTransaction (SBAccountID, TransactionDate, TransactionType, Amount)
                                          VALUES (@p0, @p1, @p2, @p3)";
                        db.Database.ExecuteSqlCommand(insertSql, accountId, DateTime.Now, "D", amount);

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        var inner = GetInnerExceptionMessages(ex);
                        ModelState.AddModelError("", "Failed to deposit: " + inner);
                        return View();
                    }
                }

                ViewBag.SuccessMessage = $"Deposited {amount:C2} to account {accountId} successfully.";
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to deposit: " + inner);
            }

            return View();
        }

        // Withdraw (GET)
        [HttpGet]
        public ActionResult Withdraw()
        {
            return View();
        }

        // Withdraw (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Withdraw(FormCollection form)
        {
            var accountId = form["accountId"];
            decimal amount = 0;
            decimal.TryParse(form["amount"], out amount);

            if (string.IsNullOrEmpty(accountId) || amount <= 0)
            {
                ModelState.AddModelError("", "Valid Account ID and amount must be provided for withdrawal.");
                return View();
            }

            try
            {
                // verify account exists
                var acc = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == accountId);
                if (acc == null)
                {
                    ModelState.AddModelError("", "Savings account not found.");
                    return View();
                }

                if (acc.Balance < amount)
                {
                    ModelState.AddModelError("", "Insufficient balance for withdrawal.");
                    return View();
                }

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        var updateSql = "UPDATE dbo.SavingsAccount SET Balance = Balance - @p0 WHERE SBAccountID = @p1";
                        var rows = db.Database.ExecuteSqlCommand(updateSql, amount, accountId);
                        if (rows == 0)
                        {
                            tx.Rollback();
                            ModelState.AddModelError("", "Failed to update account balance.");
                            return View();
                        }

                        var insertSql = @"INSERT INTO dbo.SavingsTransaction (SBAccountID, TransactionDate, TransactionType, Amount)
                                          VALUES (@p0, @p1, @p2, @p3)";
                        db.Database.ExecuteSqlCommand(insertSql, accountId, DateTime.Now, "W", amount);

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        var inner = GetInnerExceptionMessages(ex);
                        ModelState.AddModelError("", "Failed to withdraw: " + inner);
                        return View();
                    }
                }

                ViewBag.SuccessMessage = $"Withdrew {amount:C2} from account {accountId} successfully.";
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to withdraw: " + inner);
            }

            return View();
        }

        // View transactions (GET)
        [HttpGet]
        public ActionResult ViewTransactions()
        {
            return View();
        }

        // View transactions (POST) - show transactions for a savings or FD account
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ViewTransactions(string accountType, string accountId)
        {
            if (string.IsNullOrEmpty(accountType) || string.IsNullOrEmpty(accountId))
            {
                ModelState.AddModelError("", "Account type and Account ID are required.");
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
                        return View();
                    }

                    var txns = db.SavingsTransactions.Where(t => t.SBAccountID == accountId)
                                                     .OrderByDescending(t => t.TransactionDate)
                                                     .ToList();

                    ViewBag.AccountType = "SAVINGS";
                    ViewBag.Account = acc;
                    ViewBag.Transactions = txns;
                    return View();
                }

                if (accountType == "FD")
                {
                    var acc = db.FixedDepositAccounts.FirstOrDefault(a => a.FDAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Fixed deposit account not found.");
                        return View();
                    }

                    var txns = db.FDTransactions.Where(t => t.FDAccountID == accountId)
                                                 .OrderByDescending(t => t.TransactionDate)
                                                 .ToList();

                    ViewBag.AccountType = "FD";
                    ViewBag.Account = acc;
                    ViewBag.Transactions = txns;
                    return View();
                }

                ModelState.AddModelError("", "Unsupported account type. Use SAVINGS or FD.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to load transactions: " + inner);
            }

            return View();
        }

        // Open account page (GET)
        [HttpGet]
        public ActionResult OpenAccount()
        {
            ViewBag.Customers = db.Customers.ToList();
            return View();
        }

        // Open account page (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OpenAccount(FormCollection form)
        {
            ViewBag.Customers = db.Customers.ToList();

            var customerOption = (form["customerOption"] ?? string.Empty).Trim().ToUpper(); // EXISTING or NEW
            var accountType = (form["accountType"] ?? string.Empty).Trim().ToUpper(); // SAVINGS, FD

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

                ModelState.AddModelError("", "Unknown or unauthorized account type.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to add account: " + inner);
            }

            return View();
        }

        // View pending requests (accounts)
        [HttpGet]
        public ActionResult PendingRequests()
        {
            var pendingList = new List<MVCBank.Models.ViewModels.PendingAccountRequestViewModel>();

            var savings = (from a in db.SavingsAccounts
                           join c in db.Customers on a.CustomerID equals c.CustID
                           where (a.Status ?? "").ToLower() == "pending"
                           select new MVCBank.Models.ViewModels.PendingAccountRequestViewModel
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
                       select new MVCBank.Models.ViewModels.PendingAccountRequestViewModel
                       {
                           AccountID = a.FDAccountID,
                           AccountType = "FD",
                           CustomerID = c.CustID,
                           CustomerName = c.CustName,
                           Amount = a.DepositAmount,
                           StartDate = a.StartDate
                       }).ToList();

            pendingList.AddRange(savings);
            pendingList.AddRange(fds);

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

        private string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var rnd = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[rnd.Next(s.Length)]).ToArray());
        }

        private string GenerateTransactionId()
        {
            // TransactionID must be max length 7 (e.g. ST12345)
            var rnd = new Random();
            return "ST" + rnd.Next(0, 99999).ToString("D5");
        }

        private string GetInnerExceptionMessages(Exception ex)
        {
            var messages = new List<string>();
            var cur = ex;
            while (cur != null)
            {
                messages.Add(cur.Message);
                cur = cur.InnerException;
            }
            return string.Join(" --> ", messages);
        }
    }
}