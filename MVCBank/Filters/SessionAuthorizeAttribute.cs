using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace MVCBank.Filters
{
    // Session-based authorization attribute to guard actions/controllers
    // Usage: [SessionAuthorize] or [SessionAuthorize(RolesCsv="MANAGER")] or [SessionAuthorize(RolesCsv="CUSTOMER,EMPLOYEE")]
    public class SessionAuthorizeAttribute : AuthorizeAttribute
    {
        // Comma-separated list of allowed roles (e.g., "MANAGER", "EMPLOYEE", "CUSTOMER"). Optional.
        public string RolesCsv { get; set; }

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            if (httpContext == null)
                return false;

            var session = httpContext.Session;
            var userId = session?["UserID"] as string;
            var role = session?["Role"] as string;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(role))
                return false;

            if (string.IsNullOrWhiteSpace(RolesCsv))
                return true; // any logged-in user is allowed

            var allowed = RolesCsv
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim().ToUpperInvariant())
                .ToArray();

            return allowed.Contains(role.Trim().ToUpperInvariant());
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Redirect to login page when unauthorized
            var urlHelper = new UrlHelper(filterContext.RequestContext);
            var loginUrl = urlHelper.Action("Login", "Auth");

            // If it's an AJAX request, return 401 to let client handle it
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.Result = new HttpStatusCodeResult(401, "Unauthorized");
                return;
            }

            filterContext.Result = new RedirectResult(loginUrl);
        }
    }
}
