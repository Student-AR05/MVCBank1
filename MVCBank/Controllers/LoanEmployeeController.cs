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
using MVCBank.Models.ViewModels;

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
            // For loan employees show only loan account requests
            ViewBag.PendingLoanRequests = db.LoanAccounts.Count(a => (a.Status ?? "").ToLower() == "pending");
            // keep overall pending accounts if needed elsewhere
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

                    // Name regex enforcement
                    var namePattern = new Regex("^[A-Za-z][A-Za-z ]{1,}$");
                    name = (name ?? string.Empty).Trim();
                    if (!namePattern.IsMatch(name))
                    {
                        ModelState.AddModelError("", "Name must contain only letters and spaces, min 2 characters.");
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

                // Minimum loan amount validation
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
                    // ROI slabs:
                    // 10% for loans below 5,00,000
                    // 9.5% for loans from 5,00,000 to 10,00,000 (inclusive)
                    // 9% for loans above 10,00,000
                    if (amount >= 1000000)
                    {
                        roi = 9.0m;
                    }
                    else if (amount >= 500000)
                    {
                        roi = 9.5m;
                    }
                    else
                    {
                        roi = 10.0m;
                    }
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

                // EMI calculation (amortizing loan formula)
                // EMI = [P x R x (1+R)^N] / [(1+R)^N-1] where R = roi/(12*100), N = months
                decimal monthlyRate = roi / (12 * 100);
                double pow = Math.Pow(1 + (double)monthlyRate, months);
                decimal emi = (monthlyRate == 0) ? (amount / months) : (amount * monthlyRate * (decimal)pow) / ((decimal)pow - 1);
                emi = Math.Round(emi, 2);

                // EMI should not exceed 60% of monthly take home
                if (emi > 0.6m * monthlyTakeHome)
                {
                    ModelState.AddModelError("", $"EMI ({emi:C2}) cannot exceed 60% of monthly take home ({monthlyTakeHome:C2}).");
                    return View();
                }

                // Insert LoanAccount without LNAccountID (computed column) and retrieve generated LNAccountID
                var start = DateTime.Now.Date;
                var sqlAcc = @"INSERT INTO dbo.LoanAccount (CustomerID, LoanAmount, StartDate, TenureMonths, LNROI, EMIAmount, Status)
                               VALUES (@CustomerID, @LoanAmount, @StartDate, @TenureMonths, @LNROI, @EMIAmount, @Status);
                               SELECT LNAccountID FROM dbo.LoanAccount WHERE LNNum = SCOPE_IDENTITY();";

                var accParams = new[] {
                    new SqlParameter("@CustomerID", custId),
                    new SqlParameter("@LoanAmount", amount),
                    new SqlParameter("@StartDate", start),
                    new SqlParameter("@TenureMonths", months),
                    new SqlParameter("@LNROI", roi),
                    new SqlParameter("@EMIAmount", emi),
                    new SqlParameter("@Status", "Pending")
                };

                // Execute insert and get generated LNAccountID
                string createdAccountId = db.Database.SqlQuery<string>(sqlAcc, accParams).FirstOrDefault();

                if (string.IsNullOrEmpty(createdAccountId))
                {
                    ModelState.AddModelError("", "Failed to create loan account.");
                    return View();
                }

                ViewBag.SuccessMessage = $"Loan account created successfully. Account ID: {createdAccountId}. EMI: {emi:C2} (ROI: {roi}% )";
                ViewBag.CreatedAccountID = createdAccountId;
                ViewBag.LoanEMI = emi;
                ViewBag.LoanROI = roi;
                return View();
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
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
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to load transactions: " + inner);
            }

            return View();
        }

        // Deposit (GET) - show part payment form
        [HttpGet]
        public ActionResult Deposit(string accountId = null)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return View();
            }

            accountId = accountId.Trim();
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

                ViewBag.Account = acc;
                ViewBag.Outstanding = currentOutstanding;
                ViewBag.LoanEMI = acc.EMIAmount;
                ViewBag.LoanROI = acc.LNROI;
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to load loan details: " + inner);
            }

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
            amount = Math.Round(amount, 2);

            if (string.IsNullOrEmpty(accountId) || amount <= 0)
            {
                ModelState.AddModelError("", "Valid Loan Account ID and amount are required.");
                // If account id provided, try to reload details for the view
                if (!string.IsNullOrEmpty(accountId)) return RedirectToAction("Deposit", new { accountId = accountId });
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
                newOutstanding = Math.Round(newOutstanding, 2);

                // Insert payment transaction without LoanTransactionID (computed in DB)
                var insertSql = @"INSERT INTO dbo.LoanTransaction (LNAccountID, EMIDate_Actual, EMIDate_Paid, LatePenalty, Amount, Outstanding)
                                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5)";
                db.Database.ExecuteSqlCommand(
                    insertSql,
                    accountId,            // @p0 LNAccountID
                    DateTime.Now,         // @p1 EMIDate_Actual
                    DateTime.Now,         // @p2 EMIDate_Paid
                    (object)DBNull.Value, // @p3 LatePenalty
                    amount,               // @p4 Amount
                    newOutstanding        // @p5 Outstanding
                );

                ViewBag.SuccessMessage = $"Recorded payment of {amount:C2} for loan {accountId}. Outstanding: {newOutstanding:C2}";

                // Reload account details to show updated outstanding and info
                var refreshedAcc = db.LoanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                ViewBag.Account = refreshedAcc;
                ViewBag.Outstanding = newOutstanding;
                ViewBag.LoanEMI = refreshedAcc?.EMIAmount;
                ViewBag.LoanROI = refreshedAcc?.LNROI;
            }
            catch (DbEntityValidationException valEx)
            {
                var msg = string.Join("; ", valEx.EntityValidationErrors.SelectMany(e => e.ValidationErrors).Select(e => e.ErrorMessage));
                ModelState.AddModelError("", "Failed to record payment: " + msg);
            }
            catch (DbUpdateException dbuEx)
            {
                var msg = GetInnerExceptionMessages(dbuEx);
                ModelState.AddModelError("", "Failed to record payment: " + msg);
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
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

        // Add alias routes for foreclosure to support /LoanEmployee/ForecloseLoan
        [HttpGet]
        public ActionResult ForecloseLoan()
        {
            // Reuse the same view as Withdraw (foreclosure)
            return View("Withdraw");
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
                currentOutstanding = Math.Round(currentOutstanding, 2);

                if (currentOutstanding <= 0)
                {
                    ModelState.AddModelError("", "No outstanding amount for this loan.");
                    return View();
                }

                // use explicit DB transaction so both insertion of txn and status update succeed or fail together
                using (var trans = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Insert final transaction without LoanTransactionID (computed in DB)
                        var insertSql = @"INSERT INTO dbo.LoanTransaction (LNAccountID, EMIDate_Actual, EMIDate_Paid, LatePenalty, Amount, Outstanding)
                                          VALUES (@p0, @p1, @p2, @p3, @p4, @p5)";
                        db.Database.ExecuteSqlCommand(
                            insertSql,
                            accountId,            // @p0 LNAccountID
                            DateTime.Now,         // @p1 EMIDate_Actual
                            DateTime.Now,         // @p2 EMIDate_Paid
                            (object)DBNull.Value, // @p3 LatePenalty
                            currentOutstanding,   // @p4 Amount (final payment)
                            0m                    // @p5 Outstanding
                        );

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
                    catch (DbUpdateException dbuEx)
                    {
                        trans.Rollback();
                        var msg = GetInnerExceptionMessages(dbuEx);
                        ModelState.AddModelError("", "Failed to foreclose loan: " + msg);
                        return View();
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
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to foreclose loan: " + inner);
            }

            return View();
        }

        // Alias POST action for foreclosure to support /LoanEmployee/ForecloseLoan POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ForecloseLoan(FormCollection form)
        {
            // Delegate to Withdraw(FormCollection) implementation
            return Withdraw(form);
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

        private string GenerateUniqueLoanTransactionId()
        {
            // Ensure uniqueness within reasonable attempts to avoid unique key violations
            var rnd = new Random();
            for (int i = 0; i < 25; i++)
            {
                var id = "LT" + rnd.Next(0, 99999).ToString("D5");
                bool exists = db.LoanTransactions.Any(t => t.LoanTransactionID == id);
                if (!exists) return id;
            }
            // Fallback using time-based suffix
            var fallback = (DateTime.UtcNow.Ticks % 100000).ToString("D5");
            var altId = "LT" + fallback;
            if (!db.LoanTransactions.Any(t => t.LoanTransactionID == altId)) return altId;
            // As a last resort, increment until unique (very unlikely loop)
            int num = int.Parse(fallback);
            for (int j = 0; j < 100000; j++)
            {
                num = (num + 1) % 100000;
                var tryId = "LT" + num.ToString("D5");
                if (!db.LoanTransactions.Any(t => t.LoanTransactionID == tryId)) return tryId;
            }
            // If everything fails, return a random id and let DB enforce uniqueness (should not happen)
            return "LT" + new Random().Next(0, 99999).ToString("D5");
        }

        private string GetInnerExceptionMessages(Exception ex)
        {
            var messages = new System.Text.StringBuilder();
            Exception cur = ex;
            while (cur != null)
            {
                if (messages.Length > 0) messages.Append(" --> ");
                messages.Append(cur.Message);
                cur = cur.InnerException;
            }
            return messages.ToString();
        }

        // View pending loan requests (GET)
        [HttpGet]
        public ActionResult PendingRequests()
        {
            try
            {
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

                var ordered = loans.OrderBy(p => p.CustomerName).ThenBy(p => p.AccountID).ToList();
                return View("PendingRequests", ordered);
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to load pending loan requests: " + inner);
                return View("PendingRequests", new List<PendingAccountRequestViewModel>());
            }
        }

        // Approve/Reject a pending loan request (GET) - support links from view
        [HttpGet]
        public ActionResult UpdateLoanRequest(string accountId, string decision)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                TempData["ErrorMessage"] = "Account ID is required.";
                return RedirectToAction("PendingRequests");
            }

            string message;
            try
            {
                var ok = TrySetLoanStatus(accountId, decision, out message);
                if (!ok)
                {
                    TempData["ErrorMessage"] = message;
                }
                else
                {
                    TempData["SuccessMessage"] = message;
                }
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                TempData["ErrorMessage"] = "Failed to update request: " + inner;
            }

            return RedirectToAction("PendingRequests");
        }

        // Approve/Reject a pending loan request (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UpdateLoanRequest_Post(string accountId, string decision)
        {
            // kept for clients/forms that post; reuse same implementation
            if (string.IsNullOrWhiteSpace(accountId))
            {
                TempData["ErrorMessage"] = "Account ID is required.";
                return RedirectToAction("PendingRequests");
            }

            string message;
            try
            {
                var ok = TrySetLoanStatus(accountId, decision, out message);
                if (!ok) TempData["ErrorMessage"] = message;
                else TempData["SuccessMessage"] = message;
            }
            catch (Exception ex)
            {
                var inner = GetInnerExceptionMessages(ex);
                TempData["ErrorMessage"] = "Failed to update request: " + inner;
            }

            return RedirectToAction("PendingRequests");
        }

        // helper to change loan status
        private bool TrySetLoanStatus(string accountId, string decision, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                message = "Account ID is required.";
                return false;
            }

            var nextStatus = (decision ?? string.Empty).Trim().ToLower() == "approve" ? "Active" : "Rejected";

            try
            {
                var rows = db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.LoanAccount SET Status = @status WHERE LNAccountID = @id",
                    new SqlParameter("@status", nextStatus),
                    new SqlParameter("@id", accountId));

                if (rows <= 0)
                {
                    message = "Loan request not found or could not be updated.";
                    return false;
                }

                message = $"Request {(nextStatus == "Active" ? "approved" : "rejected")} for LOAN {accountId}.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Failed to update request: " + (ex.InnerException?.Message ?? ex.Message);
                return false;
            }
        }
    }
}