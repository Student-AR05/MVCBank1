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
using MVCBank.Filters;

namespace MVCBank.Controllers
{
    [SessionAuthorize(RolesCsv = "EMPLOYEE")]
    public class LoanEmployeeController : Controller
    {
        private BankDbEntities db = new BankDbEntities();

        // Dashboard for loan employee
        public ActionResult Dashboard()
        {
            ViewBag.ActiveLoanAccounts = db.LoanAccounts.Count(a => (a.Status ?? "").ToLower() != "closed");
            ViewBag.ActiveFDAccounts = db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() != "closed");
            ViewBag.TotalLoanPrincipal = db.LoanAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.LoanAmount).Sum() ?? 0m;
            ViewBag.TotalFDDeposits = db.FixedDepositAccounts.Where(a => (a.Status ?? "").ToLower() != "closed").Select(a => (decimal?)a.DepositAmount).Sum() ?? 0m;
            ViewBag.PendingAccounts = db.LoanAccounts.Count(a => (a.Status ?? "").ToLower() == "pending")
                                      + db.FixedDepositAccounts.Count(a => (a.Status ?? "").ToLower() == "pending");
            return View("Dashboard");
        }

        // GET: LoanEmployee
        public ActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // Add loan account (GET)
        [HttpGet]
        public ActionResult AddAccount()
        {
            ViewBag.Customers = db.Customers.ToList();
            return View();
        }

        // Add loan account (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddAccount(FormCollection form)
        {
            ViewBag.Customers = db.Customers.ToList();
            var customerOption = (form["customerOption"] ?? string.Empty).Trim().ToUpper(); // EXISTING or NEW
            string custId = null;

            try
            {
                Customer customer = null;
                if (customerOption == "EXISTING")
                {
                    custId = form["ExistingCustomerID"];
                    if (string.IsNullOrEmpty(custId))
                    {
                        ModelState.AddModelError("", "Select an existing customer.");
                        return View();
                    }
                    customer = db.Customers.FirstOrDefault(x => x.CustID == custId);
                    if (customer == null)
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

                    customer = db.Customers.FirstOrDefault(x => x.PAN == pan);
                    if (customer == null)
                    {
                        ModelState.AddModelError("", "Failed to create new customer.");
                        return View();
                    }

                    custId = customer.CustID;
                    ViewBag.NewCustomerPassword = pwd;
                }

                // --- Custom Validations ---
                decimal amount = 0; int months = 0; decimal roi = 0;
                decimal.TryParse(form["loanAmount"], out amount);
                int.TryParse(form["loanTenure"], out months);
                decimal monthlyTakeHome = 0;
                decimal.TryParse(form["monthlyTakeHome"], out monthlyTakeHome);

                if (amount < 10000)
                {
                    ModelState.AddModelError("", "Minimum loan amount is Rs. 10,000.");
                    return View();
                }

                // Age calculation
                var today = DateTime.Today;
                int age = today.Year - customer.DOB.Year;
                if (customer.DOB > today.AddYears(-age)) age--;
                bool isSenior = age >= 60;

                // Senior citizen rules
                if (isSenior)
                {
                    if (amount > 100000)
                    {
                        ModelState.AddModelError("", "Senior Citizens cannot be sanctioned a loan of greater than 1 lakh.");
                        return View();
                    }
                    roi = 9.5m;
                }
                else
                {
                    if (amount <= 500000) roi = 10m;
                    else if (amount <= 1000000) roi = 9.5m;
                    else roi = 9m;
                }

                if (months <= 0)
                {
                    ModelState.AddModelError("", "Loan tenure (months) is required.");
                    return View();
                }

                if (monthlyTakeHome <= 0)
                {
                    ModelState.AddModelError("", "Monthly take home is required.");
                    return View();
                }

                // EMI calculation (standard formula for reducing balance loans is more complex, but for simplicity, use flat interest)
                // EMI = [P x R x (1+R)^N] / [(1+R)^N-1] where R = roi/(12*100), N = months
                decimal monthlyRate = roi / (12 * 100);
                double pow = Math.Pow(1 + (double)monthlyRate, months);
                decimal emi = (monthlyRate == 0) ? (amount / months) : (amount * monthlyRate * (decimal)pow) / ((decimal)pow - 1);
                emi = Math.Round(emi, 2);

                if (emi > 0.6m * monthlyTakeHome)
                {
                    ModelState.AddModelError("", $"EMI ({emi:C2}) cannot exceed 60% of monthly take home ({monthlyTakeHome:C2}).");
                    return View();
                }

                // Generate unique account number (LNAccountID)
                string newAccountId = GenerateLoanAccountId();
                while (db.LoanAccounts.Any(a => a.LNAccountID == newAccountId))
                {
                    newAccountId = GenerateLoanAccountId();
                }

                var start = DateTime.Now.Date;
                var sqlAcc = @"INSERT INTO dbo.LoanAccount (LNAccountID, CustomerID, LoanAmount, StartDate, TenureMonths, LNROI, EMIAmount, Status) VALUES (@LNAccountID, @CustomerID, @LoanAmount, @StartDate, @TenureMonths, @LNROI, @EMIAmount, @Status)";
                var accParams = new[] {
                    new SqlParameter("@LNAccountID", newAccountId),
                    new SqlParameter("@CustomerID", custId),
                    new SqlParameter("@LoanAmount", amount),
                    new SqlParameter("@StartDate", start),
                    new SqlParameter("@TenureMonths", months),
                    new SqlParameter("@LNROI", roi),
                    new SqlParameter("@EMIAmount", emi),
                    new SqlParameter("@Status", "Pending")
                };

                db.Database.ExecuteSqlCommand(sqlAcc, accParams);

                ViewBag.SuccessMessage = $"Loan account created successfully. Account ID: {newAccountId}. EMI: {emi:C2} (ROI: {roi}%)";
                ViewBag.CreatedAccountID = newAccountId;
                ViewBag.LoanEMI = emi;
                ViewBag.LoanROI = roi;
                return View();
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to add loan account: " + inner);
            }

            return View();
        }

        private string GenerateLoanAccountId()
        {
            // Example: LN + 5 digit random number
            var rnd = new Random();
            return "LN" + rnd.Next(0, 99999).ToString("D5");
        }

        // Close loan account (GET)
        [HttpGet]
        public ActionResult CloseAccount()
        {
            return View();
        }

        // Close loan account - POST
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
                if (accountType == "LOAN")
                {
                    // check existence without attempting to update entity with computed PK
                    var exists = db.LoanAccounts.Any(a => a.LNAccountID == accountId);
                    if (!exists)
                    {
                        ModelState.AddModelError("", "Loan account not found.");
                        return View();
                    }

                    // perform direct SQL update to avoid EF trying to update a computed primary key
                    var rows = db.Database.ExecuteSqlCommand(
                        "UPDATE dbo.LoanAccount SET Status = @status WHERE LNAccountID = @id",
                        new SqlParameter("@status", "Closed"),
                        new SqlParameter("@id", accountId));

                    if (rows <= 0)
                    {
                        ModelState.AddModelError("", "Failed to close loan account (no rows affected).");
                        return View();
                    }

                    ViewBag.SuccessMessage = $"Loan account {accountId} closed successfully.";
                    return View();
                }

                ModelState.AddModelError("", "Not authorized to close accounts of this type.");
            }
            catch (Exception ex)
            {
                // capture full exception chain for debugging - remove after root cause found
                var detailed = ex.ToString();
                if (ex.InnerException != null) detailed += "\nInner: " + ex.InnerException.ToString();
                if (ex.InnerException?.InnerException != null) detailed += "\nInnerInner: " + ex.InnerException.InnerException.ToString();
                var msg = ex.InnerException?.InnerException?.Message ?? ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to close loan account: " + msg);
                ViewBag.DebugException = detailed;
            }

            return View();
        }

        // View loan transactions (GET)
        [HttpGet]
        public ActionResult ViewTransactions()
        {
            return View();
        }

        // View loan transactions (POST)
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
                if (accountType == "LOAN")
                {
                    var acc = db.LoanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Loan account not found.");
                        return View();
                    }

                    var txns = db.LoanTransactions.Where(t => t.LNAccountID == accountId)
                                                   .OrderByDescending(t => t.EMIDate_Actual)
                                                   .ToList();

                    ViewBag.AccountType = "LOAN";
                    ViewBag.Account = acc;
                    ViewBag.Transactions = txns;
                    return View();
                }

                ModelState.AddModelError("", "Unsupported account type. Use LOAN.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to load transactions: " + inner);
            }

            return View();
        }

        // Deposit (GET) - show part payment form
        [HttpGet]
        public ActionResult Deposit()
        {
            return View();
        }

        // PART PAYMENT (Deposit) - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Deposit(FormCollection form)
        {
            var accountId = (form["accountId"] ?? string.Empty).Trim();
            decimal amount = 0;
            decimal.TryParse(form["amount"], out amount);

            if (string.IsNullOrEmpty(accountId) || amount <= 0)
            {
                ModelState.AddModelError("", "Valid Loan Account ID and amount are required.");
                return View();
            }

            try
            {
                var acc = db.LoanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                if (acc == null)
                {
                    ModelState.AddModelError("", "Loan account not found.");
                    return View();
                }

                // determine current outstanding
                var lastTxn = db.LoanTransactions.Where(t => t.LNAccountID == accountId)
                                                  .OrderByDescending(t => t.EMIDate_Actual)
                                                  .FirstOrDefault();
                decimal currentOutstanding = lastTxn != null ? lastTxn.Outstanding : acc.LoanAmount;

                var newOutstanding = currentOutstanding - amount;
                if (newOutstanding < 0) newOutstanding = 0;

                var txn = new LoanTransaction
                {
                    LoanTransactionID = GenerateLoanTransactionId(),
                    LNAccountID = accountId,
                    EMIDate_Actual = DateTime.Now,
                    EMIDate_Paid = DateTime.Now,
                    LatePenalty = null,
                    Amount = amount,
                    Outstanding = newOutstanding
                };

                db.LoanTransactions.Add(txn);
                db.SaveChanges();

                ViewBag.SuccessMessage = $"Recorded payment of {amount:C2} for loan {accountId}. Outstanding: {newOutstanding:C2}";
            }
            catch (DbEntityValidationException valEx)
            {
                var msg = string.Join("; ", valEx.EntityValidationErrors.SelectMany(e => e.ValidationErrors).Select(e => e.ErrorMessage));
                ModelState.AddModelError("", "Failed to record payment: " + msg);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to record payment: " + inner);
            }

            return View();
        }

        // Withdraw (GET) - show foreclosure form
        [HttpGet]
        public ActionResult Withdraw()
        {
            return View();
        }

        // FORECLOSE (Withdraw) - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Withdraw(FormCollection form)
        {
            var accountId = (form["accountId"] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(accountId))
            {
                ModelState.AddModelError("", "Loan Account ID is required for foreclosure.");
                return View();
            }

            try
            {
                var acc = db.LoanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                if (acc == null)
                {
                    ModelState.AddModelError("", "Loan account not found.");
                    return View();
                }

                var lastTxn = db.LoanTransactions.Where(t => t.LNAccountID == accountId)
                                                  .OrderByDescending(t => t.EMIDate_Actual)
                                                  .FirstOrDefault();
                decimal currentOutstanding = lastTxn != null ? lastTxn.Outstanding : acc.LoanAmount;

                if (currentOutstanding <= 0)
                {
                    ModelState.AddModelError("", "No outstanding amount for this loan.");
                    return View();
                }

                // create final transaction and close loan
                var txn = new LoanTransaction
                {
                    LoanTransactionID = GenerateLoanTransactionId(),
                    LNAccountID = accountId,
                    EMIDate_Actual = DateTime.Now,
                    EMIDate_Paid = DateTime.Now,
                    LatePenalty = null,
                    Amount = currentOutstanding,
                    Outstanding = 0m
                };

                // use explicit DB transaction so both insertion of txn and status update succeed or fail together
                using (var trans = db.Database.BeginTransaction())
                {
                    try
                    {
                        db.LoanTransactions.Add(txn);
                        db.SaveChanges();

                        var rows = db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.LoanAccount SET Status = @status WHERE LNAccountID = @id",
                            new SqlParameter("@status", "Closed"),
                            new SqlParameter("@id", accountId));

                        if (rows <= 0)
                        {
                            trans.Rollback();
                            ModelState.AddModelError("", "Failed to close loan account (no rows affected).");
                            return View();
                        }

                        trans.Commit();

                        ViewBag.SuccessMessage = $"Loan {accountId} foreclosed. Final payment: {currentOutstanding:C2}";
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }
            catch (DbEntityValidationException valEx)
            {
                var msg = string.Join("; ", valEx.EntityValidationErrors.SelectMany(e => e.ValidationErrors).Select(e => e.ErrorMessage));
                ModelState.AddModelError("", "Failed to foreclose loan: " + msg);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to foreclose loan: " + inner);
            }

            return View();
        }

        private string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var rnd = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[rnd.Next(s.Length)]).ToArray());
        }

        private string GenerateLoanTransactionId()
        {
            var rnd = new Random();
            return "LT" + rnd.Next(0, 99999).ToString("D5");
        }
    }
}