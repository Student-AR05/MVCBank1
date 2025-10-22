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

                    var existing = db.SavingsAccounts.FirstOrDefault(a => a.CustomerID == userId);
                    if (existing != null)
                    {
                        TempData["Message"] = "You already have a savings account.";
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
                        Status = "Pending"
                    };

                    db.FixedDepositAccounts.Add(fd);
                    db.SaveChanges();

                    TempData["Message"] = "Fixed deposit created and pending activation.";
                    return RedirectToAction("Dashboard");
                }

                if (accountType == "LOAN")
                {
                    decimal amount = 0; int months = 0; decimal roi = 0;
                    decimal.TryParse(form["loanAmount"], out amount);
                    int.TryParse(form["loanTenure"], out months);
                    decimal.TryParse(form["loanROI"], out roi);

                    if (amount < 10000)
                    {
                        TempData["Message"] = "Minimum loan amount is 10000.";
                        return RedirectToAction("OpenAccount");
                    }

                    var loan = new LoanAccount
                    {
                        CustomerID = userId,
                        LoanAmount = amount,
                        StartDate = DateTime.Now.Date,
                        TenureMonths = months,
                        LNROI = roi,
                        Status = "Pending",
                        EMIAmount = Math.Round((amount * roi / 100) / (months == 0 ? 1 : months), 2)
                    };

                    db.LoanAccounts.Add(loan);
                    db.SaveChanges();

                    TempData["Message"] = "Loan application created and pending review.";
                    return RedirectToAction("Dashboard");
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
            var cust = db.Customers.FirstOrDefault(c => c.CustID == userId);
            if (cust == null)
            {
                ModelState.AddModelError("", "Customer not found.");
                return View();
            }

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

            // generate unique FDAccountID
            string fdId;
            var rnd = new Random();
            do
            {
                fdId = "FD" + rnd.Next(0, 99999).ToString("D5");
            } while (db.FixedDepositAccounts.Any(f => f.FDAccountID == fdId));

            try
            {
                var fd = new FixedDepositAccount
                {
                    CustomerID = userId,
                    StartDate = DateTime.Now.Date,
                    EndDate = DateTime.Now.Date.AddMonths(months),
                    DepositAmount = amount,
                    FDROI = finalRoi,
                    Status = "Pending"
                    // do not set FDAccountID because it's computed in the database
                };

                // Insert without FDAccountID (DB computes it)
                var insertSql = @"INSERT INTO dbo.FixedDepositAccount (CustomerID, StartDate, EndDate, DepositAmount, FDROI, Status)
                                  VALUES (@CustomerID, @StartDate, @EndDate, @DepositAmount, @FDROI, @Status)";

                var insertParams = new[] {
                    new SqlParameter("@CustomerID", fd.CustomerID),
                    new SqlParameter("@StartDate", fd.StartDate),
                    new SqlParameter("@EndDate", fd.EndDate),
                    new SqlParameter("@DepositAmount", fd.DepositAmount),
                    new SqlParameter("@FDROI", fd.FDROI),
                    new SqlParameter("@Status", fd.Status)
                };

                db.Database.ExecuteSqlCommand(insertSql, insertParams);

                // retrieve the newly created FD record (latest by FDNum for this customer and start date)
                var created = db.FixedDepositAccounts
                                .Where(f => f.CustomerID == userId && System.Data.Entity.DbFunctions.TruncateTime(f.StartDate) == fd.StartDate.Date && f.DepositAmount == amount)
                                .OrderByDescending(f => f.FDNum)
                                .FirstOrDefault();

                if (created != null)
                {
                    ViewBag.FDAccountID = created.FDAccountID;
                }
                else
                {
                    ViewBag.FDAccountID = null; // not found
                }

                ViewBag.SuccessMessage = "Fixed deposit created and pending activation.";
                ViewBag.MaturityAmount = maturityAmount;
                ViewBag.ROI = finalRoi;
                ViewBag.DurationMonths = months;
                ViewBag.DepositAmount = amount;
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
                return View();
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", "Failed to create FD: " + inner);
                return View();
            }

            return View();
        }

        [HttpGet]
        public ActionResult ApplyLoan()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Transactions()
        {
            return View();
        }

        // ... other existing actions remain unchanged ...
    }
}