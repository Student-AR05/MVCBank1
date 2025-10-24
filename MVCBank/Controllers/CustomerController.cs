using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using MVCBank.Models;
using MVCBank.Models.ViewModels;
using System.Data.SqlClient;
using System.Data;
using MVCBank.Filters;
using System.Data.Entity;

namespace MVCBank.Controllers
{
    [SessionAuthorize(RolesCsv = "CUSTOMER")]
    public class CustomerController : Controller
    {
        private BankDbEntities db = new BankDbEntities();

        // GET: Customer
        public ActionResult Index()
        {
            return View();
        }

        // Dashboard action used after login
        public ActionResult Dashboard()
        {
            var userId = Session["UserID"] as string;

            // Do not redirect customers without a savings account — show dashboard with options
            var account = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId);

            // prepare view model with account and recent transactions
            var vm = new CustomerDashboardViewModel
            {
                SavingsAccount = account,
                Transactions = account != null
                    ? db.SavingsTransactions
                        .Where(t => t.SBAccountID == account.SBAccountID)
                        .OrderByDescending(t => t.TransactionDate)
                        .Take(20)
                        .ToList()
                    : new List<SavingsTransaction>()
            };

            return View(vm);
        }

        // Settings page for customer
        [HttpGet]
        public ActionResult Settings()
        {
            var userId = Session["UserID"] as string;
            var customer = db.Customers.FirstOrDefault(c => c.CustID == userId);
            return View(customer);
        }

        // Settings POST - change password
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Settings(FormCollection form)
        {
            var userId = Session["UserID"] as string;

            var current = form["currentPassword"] ?? string.Empty;
            var newPwd = form["newPassword"] ?? string.Empty;
            var confirm = form["confirmPassword"] ?? string.Empty;

            var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
            if (cust == null)
            {
                ModelState.AddModelError("", "Customer not found.");
                return View();
            }

            // Validate current password
            var currentBytes = Encoding.UTF8.GetBytes(current);
            var stored = cust.CustPassword ?? new byte[0];
            if (!stored.SequenceEqual(currentBytes))
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View(cust);
            }

            if (string.IsNullOrEmpty(newPwd) || newPwd.Length < 6)
            {
                ModelState.AddModelError("", "New password must be at least 6 characters.");
                return View(cust);
            }

            if (newPwd != confirm)
            {
                ModelState.AddModelError("", "New password and confirmation do not match.");
                return View(cust);
            }

            try
            {
                var newBytes = Encoding.UTF8.GetBytes(newPwd);
                var sql = "UPDATE dbo.Customer SET CustPassword = @pwd WHERE CustID = @id";
                var parameters = new[] {
                    new SqlParameter("@pwd", System.Data.SqlDbType.VarBinary) { Value = (object)newBytes ?? DBNull.Value },
                    new SqlParameter("@id", userId)
                };

                db.Database.ExecuteSqlCommand(sql, parameters);

                ViewBag.SuccessMessage = "Password updated successfully.";

                // Refresh local entity if needed
                var refreshed = db.Customers.FirstOrDefault(c => c.CustID == userId);
                return View(refreshed);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to update password: " + inner);
                return View(cust);
            }
        }

        // GET: show open account form
        [HttpGet]
        public ActionResult OpenAccount()
        {
            return View();
        }

        // POST: create savings account or other accounts
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OpenAccount(FormCollection form)
        {
            var userId = Session["UserID"] as string;

            string accountType = form["accountTypeSelected"] ?? "SAVINGS";

            try
            {
                if (accountType == "SAVINGS")
                {
                    decimal initial = 0;
                    decimal.TryParse(form["initialDeposit"], out initial);
                    if (initial < 1000)
                    {
                        TempData["Message"] = "Minimum initial deposit for savings is 1000.";
                        return RedirectToAction("OpenAccount");
                    }

                    var existing = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId && a.Status=="Active");
                    if (existing != null)
                    {
                        TempData["Message"] = "You already have a savings account (active or pending).";
                        return RedirectToAction("Dashboard");
                    }

                    var account = new SavingsAccount
                    {
                        CustomerID = userId,
                        Balance = initial,
                        Status = "Pending",
                        CreatedAt = DateTime.Now
                    };

                    db.SavingsAccounts.Add(account);
                    db.SaveChanges();

                    TempData["Message"] = "Savings account created and pending activation.";
                    return RedirectToAction("Dashboard");
                }

                if (accountType == "FD")
                {
                    decimal amount = 0; int months = 0; decimal roi = 0;
                    decimal.TryParse(form["fdAmount"], out amount);
                    int.TryParse(form["fdDuration"], out months);
                    decimal.TryParse(form["fdROI"], out roi);

                    if (amount < 10000)
                    {
                        TempData["Message"] = "Minimum FD deposit is 10000.";
                        return RedirectToAction("OpenAccount");
                    }

                    var fd = new FixedDepositAccount
                    {
                        CustomerID = userId,
                        StartDate = DateTime.Now.Date,
                        EndDate = DateTime.Now.Date.AddMonths(months),
                        DepositAmount = amount,
                        FDROI = roi,
                        Status = "Active" // create FD as Active by default (no manager approval)
                    };

                    db.FixedDepositAccounts.Add(fd);
                    db.SaveChanges();

                    TempData["Message"] = "Fixed deposit created and activated.";
                    return RedirectToAction("Dashboard");
                }

                if (accountType == "LOAN")
                {
                    // Enforce: customer must have an active savings account before opening a loan
                    var hasActiveSavings = db.SavingsAccounts.Any(a => a.CustomerID == userId && ((a.Status ?? "").ToLower() == "active"));
                    if (!hasActiveSavings)
                    {
                        TempData["Message"] = "You must have an active savings account before opening a loan.";
                        return RedirectToAction("OpenAccount");
                    }

                    decimal amount = 0; int months = 0; decimal monthlyTakeHome = 0; decimal roi = 0;
                    decimal.TryParse(form["loanAmount"], out amount);
                    int.TryParse(form["loanTenure"], out months);
                    decimal.TryParse(form["monthlyTakeHome"], out monthlyTakeHome);

                    if (amount < 10000)
                    {
                        TempData["Message"] = "Minimum loan amount is 10000.";
                        return RedirectToAction("OpenAccount");
                    }
                    if (months <= 0)
                    {
                        TempData["Message"] = "Loan tenure (months) is required.";
                        return RedirectToAction("OpenAccount");
                    }
                    if (monthlyTakeHome <= 0)
                    {
                        TempData["Message"] = "Monthly take home is required.";
                        return RedirectToAction("OpenAccount");
                    }

                    var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
                    if (cust == null)
                    {
                        TempData["Message"] = "Customer not found.";
                        return RedirectToAction("OpenAccount");
                    }

                    // Age and senior rules
                    var today = DateTime.Today;
                    int age = today.Year - cust.DOB.Year;
                    if (cust.DOB > today.AddYears(-age)) age--;
                    bool isSenior = age >= 60;
                    if (isSenior)
                    {
                        if (amount > 100000)
                        {
                            TempData["Message"] = "Senior Citizens cannot be sanctioned a loan of greater than 1 lakh.";
                            return RedirectToAction("OpenAccount");
                        }
                        roi = 9.5m;
                    }
                    else
                    {
                        if (amount >= 1000000) roi = 9.0m;
                        else if (amount >= 500000) roi = 9.5m;
                        else roi = 10.0m;
                    }

                    // EMI calculation using amortized formula
                    decimal monthlyRate = roi / (12 * 100);
                    double pow = Math.Pow(1 + (double)monthlyRate, months);
                    decimal emi = (monthlyRate == 0) ? (amount / months) : (amount * monthlyRate * (decimal)pow) / ((decimal)pow - 1);
                    emi = Math.Round(emi, 2);

                    if (emi > 0.6m * monthlyTakeHome)
                    {
                        TempData["Message"] = $"EMI ({emi:C2}) cannot exceed 60% of monthly take home ({monthlyTakeHome:C2}).";
                        return RedirectToAction("OpenAccount");
                    }

                    var start = DateTime.Now.Date;

                    // Insert LoanAccount and fetch generated LNAccountID
                    var sqlAcc = @"INSERT INTO dbo.LoanAccount (CustomerID, LoanAmount, StartDate, TenureMonths, LNROI, EMIAmount, Status)
                                   VALUES (@CustomerID, @LoanAmount, @StartDate, @TenureMonths, @LNROI, @EMIAmount, @Status);
                                   SELECT LNAccountID FROM dbo.LoanAccount WHERE LNNum = SCOPE_IDENTITY();";

                    var accParams = new[] {
                        new SqlParameter("@CustomerID", userId),
                        new SqlParameter("@LoanAmount", amount),
                        new SqlParameter("@StartDate", start),
                        new SqlParameter("@TenureMonths", months),
                        new SqlParameter("@LNROI", roi),
                        new SqlParameter("@EMIAmount", emi),
                        new SqlParameter("@Status", "Pending")
                    };

                    string createdAccountId = db.Database.SqlQuery<string>(sqlAcc, accParams).FirstOrDefault();
                    if (string.IsNullOrEmpty(createdAccountId))
                    {
                        TempData["Message"] = "Failed to submit loan request.";
                        return RedirectToAction("OpenAccount");
                    }

                    ViewBag.SuccessMessage = $"Loan application submitted. Account ID: {createdAccountId}. EMI: {emi:C2} (ROI: {roi}% )";
                    ViewBag.CreatedAccountID = createdAccountId;
                    ViewBag.LoanEMI = emi;
                    ViewBag.LoanROI = roi;
                    return View();
                }
            }
            catch (Exception ex)
            {
                TempData["Message"] = "Failed to create account: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction("OpenAccount");
            }

            TempData["Message"] = "Unknown account type.";
            return RedirectToAction("OpenAccount");
        }

        // New placeholder pages for customer quick actions
        [HttpGet]
        public ActionResult OpenSavings()
        {
            return View();
        }

        // POST: create a savings account request from customer (marked Pending)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OpenSavings(FormCollection form)
        {
            var userId = Session["UserID"] as string;

            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["Message"] = "User not logged in.";
                return RedirectToAction("OpenSavings");
            }

            decimal initial = 0m;
            decimal.TryParse(form["initialDeposit"], out initial);

            if (initial < 1000m)
            {
                ModelState.AddModelError("", "Minimum initial deposit for savings is Rs. 1,000.");
                ViewBag.ErrorMessage = "Minimum initial deposit for savings is Rs. 1,000.";
                return View();
            }

            try
            {
                var existing = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId);
                if (existing != null)
                {
                    ModelState.AddModelError("", "You already have a savings account.");
                    ViewBag.ErrorMessage = "You already have a savings account.";
                    return View();
                }

                var account = new SavingsAccount
                {
                    CustomerID = userId,
                    Balance = initial,
                    Status = "Pending",
                    CreatedAt = DateTime.Now
                };

                db.SavingsAccounts.Add(account);
                db.SaveChanges();

                TempData["Message"] = "Savings account request submitted and pending approval.";
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to submit request: " + inner);
                ViewBag.ErrorMessage = "Failed to submit request: " + inner;
                return View();
            }
        }

        [HttpGet]
        public ActionResult OpenFD()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OpenFD(FormCollection form)
        {
            var userId = Session["UserID"] as string;

            decimal amount = 0;
            int months = 0;
            decimal.TryParse(form["fdAmount"], out amount);
            int.TryParse(form["fdDurationMonths"], out months);

            if (amount < 10000)
            {
                ModelState.AddModelError("", "Minimum Deposit is Rs. 10,000");
                return View();
            }

            if (months <= 0)
            {
                ModelState.AddModelError("", "Duration must be greater than 0 months.");
                return View();
            }

            // Ensure customer exists
            var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
            if (cust == null)
            {
                ModelState.AddModelError("", "Customer not found.");
                return View();
            }

            // Check for an active savings account and sufficient balance for FD deposit
            var savings = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId && ((a.Status ?? "").ToLower() == "active"));
            if (savings == null)
            {
                var msg = "No active savings account found. Please open and activate a savings account before creating an FD.";
                ModelState.AddModelError("", msg);
                ViewBag.ErrorMessage = msg;
                return View();
            }

            if (savings.Balance < amount)
            {
                var msg = $"Insufficient balance in savings account. Available balance: Rs. {savings.Balance:0.00}";
                ModelState.AddModelError("", msg);
                ViewBag.ErrorMessage = msg;
                return View();
            }

            // determine base ROI based on duration in years
            decimal years = Math.Round((decimal)months / 12m, 4);
            decimal baseRoi;
            if (years <= 1.0m)
                baseRoi = 6.0m;
            else if (years > 1.0m && years <= 2.0m)
                baseRoi = 7.0m;
            else
                baseRoi = 8.0m; // for durations >2 years

            // senior citizen check: age >= 60 -> +0.5%
            int age = 0;
            try
            {
                var dob = cust.DOB;
                var today = DateTime.Today;
                age = today.Year - dob.Year;
                if (dob > today.AddYears(-age)) age--;
            }
            catch
            {
                age = 0;
            }

            decimal finalRoi = baseRoi;
            if (age >= 60)
            {
                finalRoi += 0.5m;
            }

            // compute maturity amount using annual compounding
            double p = (double)amount;
            double r = (double)finalRoi / 100.0;
            double t = (double)months / 12.0;
            double maturity = p * Math.Pow(1.0 + r, t);
            decimal maturityAmount = Math.Round((decimal)maturity, 2);

            try
            {
                // Use a DB transaction: deduct savings balance, create FD (Active), and add transactions
                using (var tran = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Deduct amount from savings balance
                        var rows = db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.SavingsAccount SET Balance = Balance - @p0 WHERE SBAccountID = @p1",
                            amount, savings.SBAccountID);
                        if (rows == 0)
                        {
                            tran.Rollback();
                            ModelState.AddModelError("", "Failed to deduct amount from savings account.");
                            return View();
                        }

                        // refresh savings entity from DB to get updated balance
                        savings = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == savings.SBAccountID);
                        if (savings != null)
                        {
                            ViewBag.SavingsBalance = savings.Balance;
                        }

                        // Create FD as Active (no manager approval required)
                        var fdStart = DateTime.Now.Date;
                        var fdEnd = DateTime.Now.Date.AddMonths(months);

                        // Insert FD and return generated FDAccountID
                        var insertFdSql = @"INSERT INTO dbo.FixedDepositAccount (CustomerID, StartDate, EndDate, DepositAmount, FDROI, Status)
                                             VALUES (@CustomerID, @StartDate, @EndDate, @DepositAmount, @FDROI, @Status);
                                             SELECT FDAccountID FROM dbo.FixedDepositAccount WHERE FDNum = SCOPE_IDENTITY();";

                        var createdFdId = db.Database.SqlQuery<string>(
                            insertFdSql,
                            new SqlParameter("@CustomerID", userId),
                            new SqlParameter("@StartDate", fdStart),
                            new SqlParameter("@EndDate", fdEnd),
                            new SqlParameter("@DepositAmount", amount),
                            new SqlParameter("@FDROI", finalRoi),
                            new SqlParameter("@Status", "Active")
                        ).FirstOrDefault();

                        FixedDepositAccount created = null;
                        if (!string.IsNullOrEmpty(createdFdId))
                        {
                            created = db.FixedDepositAccounts.FirstOrDefault(f => f.FDAccountID == createdFdId);
                        }
                        else
                        {
                            // Fallback: try to locate by attributes
                            created = db.FixedDepositAccounts
                                        .Where(f => f.CustomerID == userId && f.StartDate == fdStart && f.DepositAmount == amount)
                                        .OrderByDescending(f => f.FDNum)
                                        .FirstOrDefault();
                        }

                        // populate viewbag with FD dates/status for display
                        if (created != null)
                        {
                            ViewBag.StartDate = created.StartDate;
                            ViewBag.EndDate = created.EndDate;
                            ViewBag.Status = created.Status;
                            ViewBag.FDAccountID = created.FDAccountID;
                        }
                        else
                        {
                            ViewBag.FDAccountID = null;
                        }

                        // Create a savings transaction recording the debit (use 'W' to satisfy CHECK constraint)
                        var insertSavSql = @"INSERT INTO dbo.SavingsTransaction (SBAccountID, TransactionDate, TransactionType, Amount)
                                             VALUES (@SBAccountID, @TransactionDate, @TransactionType, @Amount)";
                        db.Database.ExecuteSqlCommand(insertSavSql,
                            new SqlParameter("@SBAccountID", savings.SBAccountID),
                            new SqlParameter("@TransactionDate", DateTime.Now),
                            new SqlParameter("@TransactionType", "W"),
                            new SqlParameter("@Amount", amount)
                        );

                        // Try to fetch the created transaction (TransactionID is computed by DB)
                        var createdSavTx = db.SavingsTransactions
                                            .Where(tx => tx.SBAccountID == savings.SBAccountID && DbFunctions.TruncateTime(tx.TransactionDate) == DbFunctions.TruncateTime(DateTime.Now) && tx.Amount == amount)
                                            .OrderByDescending(tx => tx.TransactionNum)
                                            .FirstOrDefault();
                        if (createdSavTx != null)
                        {
                            ViewBag.CreatedSavingsTransactionID = createdSavTx.TransactionID;
                        }

                        // Optionally create an FDTransaction record
                        if (created != null)
                        {
                            var insertFdTxSql = @"INSERT INTO dbo.FDTransaction (FDAccountID, TransactionDate, FDType, Amount)
                                                  VALUES (@FDAccountID, @TransactionDate, @FDType, @Amount)";
                            db.Database.ExecuteSqlCommand(insertFdTxSql,
                                new SqlParameter("@FDAccountID", created.FDAccountID),
                                new SqlParameter("@TransactionDate", DateTime.Now),
                                new SqlParameter("@FDType", "C"),
                                new SqlParameter("@Amount", amount)
                            );
                        }

                        tran.Commit();

                        ViewBag.SuccessMessage = "Fixed deposit created and activated. Amount deducted from savings.";
                        ViewBag.MaturityAmount = maturityAmount;
                        ViewBag.ROI = finalRoi;
                        ViewBag.DurationMonths = months;
                        ViewBag.DepositAmount = amount;
                    }
                    catch
                    {
                        tran.Rollback();
                        throw;
                    }
                }
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException valEx)
            {
                var msg = string.Join("; ", valEx.EntityValidationErrors.SelectMany(e => e.ValidationErrors).Select(e => e.ErrorMessage));
                ModelState.AddModelError("", "Validation failed: " + msg);
                return View();
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException dbEx)
            {
                var inner = dbEx.InnerException?.Message ?? dbEx.Message;
                ModelState.AddModelError("", "DB update failed: " + inner);
                var msg = GetInnerExceptionMessages(dbEx);
                ModelState.AddModelError("", "DB update failed: " + msg);
                return View();
            }
            catch (Exception ex)
            {
                var innerMsg = GetInnerExceptionMessages(ex);
                ModelState.AddModelError("", "Failed to create FD: " + innerMsg);
                return View();
            }

            return View();
        }

        [HttpGet]
        public ActionResult ApplyLoan()
        {
            // Auto-fill applicant details from logged-in customer's profile
            var userId = Session["UserID"] as string;
            var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
            if (cust != null)
            {
                ViewBag.ApplicantName = cust.CustName;
                ViewBag.DOB = cust.DOB.ToString("yyyy-MM-dd");
                ViewBag.PAN = cust.PAN;
                ViewBag.Mobile = cust.PhoneNumber;
                ViewBag.Address = cust.Address;
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ApplyLoan(FormCollection form)
        {
            var userId = Session["UserID"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                ModelState.AddModelError("", "User not logged in.");
                return View();
            }

            try
            {
                // Enforce: customer must have an active savings account before applying for a loan
                var hasActiveSavings = db.SavingsAccounts.Any(a => a.CustomerID == userId && ((a.Status ?? "").ToLower() == "active"));
                if (!hasActiveSavings)
                {
                    ModelState.AddModelError("", "You must have an active savings account before applying for a loan.");
                    return View();
                }

                decimal amount = 0; int months = 0; decimal roi = 0;
                decimal.TryParse(form["loanAmount"], out amount);
                int.TryParse(form["loanTenure"], out months);
                decimal monthlyTakeHome = 0;
                // Fix: read netMonthlyIncome from the form
                decimal.TryParse(form["netMonthlyIncome"], out monthlyTakeHome);

                // Minimum loan amount
                if (amount < 10000)
                {
                    ModelState.AddModelError("", "Minimum loan amount is Rs. 10,000.");
                    return View();
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

                var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
                if (cust == null)
                {
                    ModelState.AddModelError("", "Customer not found.");
                    return View();
                }

                // Age calculation for senior citizen
                var today = DateTime.Today;
                int age = today.Year - cust.DOB.Year;
                if (cust.DOB > today.AddYears(-age)) age--;
                bool isSenior = age >= 60;

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
                    if (amount >= 1000000) roi = 9.0m;
                    else if (amount >= 500000) roi = 9.5m;
                    else roi = 10.0m;
                }

                // EMI calculation
                decimal monthlyRate = roi / (12 * 100);
                double pow = Math.Pow(1 + (double)monthlyRate, months);
                decimal emi = (monthlyRate == 0) ? (amount / months) : (amount * monthlyRate * (decimal)pow) / ((decimal)pow - 1);
                emi = Math.Round(emi, 2);

                if (emi > 0.6m * monthlyTakeHome)
                {
                    ModelState.AddModelError("", $"EMI ({emi:C2}) cannot exceed 60% of monthly take home ({monthlyTakeHome:C2}).");
                    return View();
                }

                // Insert LoanAccount (let DB generate LNAccountID) and return inserted LNAccountID
                var start = DateTime.Now.Date;
                var sqlAcc = @"INSERT INTO dbo.LoanAccount (CustomerID, LoanAmount, StartDate, TenureMonths, LNROI, EMIAmount, Status)
                               VALUES (@CustomerID, @LoanAmount, @StartDate, @TenureMonths, @LNROI, @EMIAmount, @Status);
                               SELECT LNAccountID FROM dbo.LoanAccount WHERE LNNum = SCOPE_IDENTITY();";

                var accParams = new[] {
                    new SqlParameter("@CustomerID", userId),
                    new SqlParameter("@LoanAmount", amount),
                    new SqlParameter("@StartDate", start),
                    new SqlParameter("@TenureMonths", months),
                    new SqlParameter("@LNROI", roi),
                    new SqlParameter("@EMIAmount", emi),
                    new SqlParameter("@Status", "Pending")
                };

                string createdAccountId = db.Database.SqlQuery<string>(sqlAcc, accParams).FirstOrDefault();
                if (string.IsNullOrEmpty(createdAccountId))
                {
                    ModelState.AddModelError("", "Failed to submit loan application.");
                    return View();
                }

                ViewBag.SuccessMessage = $"Loan application submitted. Account ID: {createdAccountId}. EMI: {emi:C2} (ROI: {roi}% )";
                ViewBag.CreatedAccountID = createdAccountId;
                ViewBag.LoanEMI = emi;
                ViewBag.LoanROI = roi;
                return View();
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to apply for loan: " + inner);
                return View();
            }
        }

        [HttpGet]
        public ActionResult Transactions(string accountType = null, string accountId = null)
        {
            var userId = Session["UserID"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                ModelState.AddModelError("", "User not logged in.");
                return View();
            }

            // Load the customer's accounts for selection
            var savingsAccounts = db.SavingsAccounts.Where(a => a.CustomerID == userId).ToList();
            var fdAccounts = db.FixedDepositAccounts.Where(a => a.CustomerID == userId).ToList();
            var loanAccounts = db.LoanAccounts.Where(a => a.CustomerID == userId).ToList();

            ViewBag.SavingsAccounts = savingsAccounts;
            ViewBag.FDAccounts = fdAccounts;
            ViewBag.LoanAccounts = loanAccounts;

            if (string.IsNullOrEmpty(accountType) || string.IsNullOrEmpty(accountId))
            {
                // No specific account requested - show selection UI
                return View();
            }

            accountType = (accountType ?? "").Trim().ToUpper();
            accountId = (accountId ?? "").Trim();

            try
            {
                if (accountType == "SAVINGS")
                {
                    var acc = savingsAccounts.FirstOrDefault(a => a.SBAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Savings account not found or does not belong to you.");
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
                    var acc = fdAccounts.FirstOrDefault(a => a.FDAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Fixed deposit account not found or does not belong to you.");
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

                if (accountType == "LOAN")
                {
                    var acc = loanAccounts.FirstOrDefault(a => a.LNAccountID == accountId);
                    if (acc == null)
                    {
                        ModelState.AddModelError("", "Loan account not found or does not belong to you.");
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

                ModelState.AddModelError("", "Unsupported account type. Use SAVINGS, FD or LOAN.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to load transactions: " + inner);
            }

            return View();
        }

        // Transfer money between savings accounts (customer)
        [HttpGet]
        public ActionResult Transfer()
        {
            var userId = Session["UserID"] as string;
            var from = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId && (a.Status ?? "").ToLower() == "active");
            if (from == null)
            {
                ViewBag.ErrorMessage = "You need an active savings account to transfer money.";
            }
            else
            {
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Transfer(FormCollection form)
        {
            var userId = Session["UserID"] as string;
            var targetId = (form["toAccountId"] ?? string.Empty).Trim();
            decimal amount = 0m;
            decimal.TryParse(form["amount"], out amount);

            if (amount <= 100m)
            {
                ModelState.AddModelError("", "Transfer amount must be greater than Rs. 100.");
            }
            if (string.IsNullOrEmpty(targetId))
            {
                ModelState.AddModelError("", "Destination savings account ID is required.");
            }

            var from = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId && (a.Status ?? "").ToLower() == "active");
            if (from == null)
            {
                ModelState.AddModelError("", "You need an active savings account to transfer money.");
            }

            if (!ModelState.IsValid)
            {
                if (from != null)
                {
                    ViewBag.FromAccount = from.SBAccountID;
                    ViewBag.FromBalance = from.Balance;
                }
                return View();
            }

            if (from.SBAccountID.Equals(targetId, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("", "Cannot transfer to the same account.");
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
                return View();
            }

            // Check balance sufficiency
            if (from.Balance < amount)
            {
                ModelState.AddModelError("", $"Insufficient balance. Available: Rs. {from.Balance:0.00}");
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
                return View();
            }

            // Validate destination account exists and active
            var to = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == targetId);
            if (to == null)
            {
                ModelState.AddModelError("", "Destination savings account not found.");
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
                return View();
            }
            if ((to.Status ?? string.Empty).Trim().ToLower() != "active")
            {
                ModelState.AddModelError("", "Destination savings account is not active.");
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
                return View();
            }

            try
            {
                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Deduct from source with atomic balance check
                        var rowsFrom = db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.SavingsAccount SET Balance = Balance - @p0 WHERE SBAccountID = @p1 AND Balance >= @p0",
                            amount, from.SBAccountID);
                        if (rowsFrom == 0)
                        {
                            tx.Rollback();
                            ModelState.AddModelError("", "Failed to debit your savings account or insufficient funds.");
                            ViewBag.FromAccount = from.SBAccountID;
                            ViewBag.FromBalance = from.Balance;
                            return View();
                        }

                        // Credit destination
                        var rowsTo = db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.SavingsAccount SET Balance = Balance + @p0 WHERE SBAccountID = @p1",
                            amount, to.SBAccountID);
                        if (rowsTo == 0)
                        {
                            tx.Rollback();
                            ModelState.AddModelError("", "Failed to credit destination account.");
                            ViewBag.FromAccount = from.SBAccountID;
                            ViewBag.FromBalance = from.Balance;
                            return View();
                        }

                        // Insert transactions
                        var insertTxn = @"INSERT INTO dbo.SavingsTransaction (SBAccountID, TransactionDate, TransactionType, Amount)
                                          VALUES (@p0, @p1, @p2, @p3)";
                        // Source: withdrawal
                        db.Database.ExecuteSqlCommand(insertTxn, from.SBAccountID, DateTime.Now, "W", amount);
                        // Destination: deposit
                        db.Database.ExecuteSqlCommand(insertTxn, to.SBAccountID, DateTime.Now, "D", amount);

                        tx.Commit();

                        // Refresh balance
                        var refreshed = db.SavingsAccounts.FirstOrDefault(a => a.SBAccountID == from.SBAccountID);
                        ViewBag.FromAccount = refreshed?.SBAccountID ?? from.SBAccountID;
                        ViewBag.FromBalance = refreshed?.Balance ?? (from.Balance - amount);
                        ViewBag.SuccessMessage = $"Transferred Rs. {amount:0.00} to {to.SBAccountID} successfully.";
                        return View();
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        var msg = ex.InnerException?.Message ?? ex.Message;
                        ModelState.AddModelError("", "Failed to transfer: " + msg);
                        ViewBag.FromAccount = from.SBAccountID;
                        ViewBag.FromBalance = from.Balance;
                        return View();
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to transfer: " + msg);
                ViewBag.FromAccount = from.SBAccountID;
                ViewBag.FromBalance = from.Balance;
                return View();
            }
        }

        // Helper to extract full inner exception messages (useful for DB errors)
        private string GetInnerExceptionMessages(Exception ex)
        {
            if (ex == null) return string.Empty;
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