using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuoteManagement.Models;
using QuoteManagment.Data;
using QuoteManagment.Models;

namespace QuoteManagement.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly GroupRepo _grRepo;
        private IConfiguration _configuration;
        private Dictionary<string, string> _sublist;
        public SubsRepo _sbRepo;
        public SettingsRepo _stRepo;

        public HomeController(GroupRepo grRepo, SubsRepo sbRepo, SettingsRepo stRepo, IConfiguration configuration)
        {
            _grRepo = grRepo;
            _grRepo.GroupRepoInit();
            _configuration = configuration;
            _sbRepo = sbRepo;
            sbRepo.SubRepoInit();
            _stRepo = stRepo;
            stRepo.SettRepoInit();
        }
        public ActionResult UserInfo()
        {
            return View();
        }
        // GET: Groups
        public ActionResult Index()
        {
            if (_sublist == null) _sublist = _sbRepo.GetSubscriptiuonList();
            ViewData["subscription"] = _sublist;
            ViewData["WebHookTemplate"] = _configuration["HttpFunctionWebHook"];

            string cookie = HttpContext.Request.Cookies["sb"];
            if (string.IsNullOrEmpty(cookie) || cookie == "All")
            {
                ViewData["selected_subscription_name"] = "All";
                ViewData["selected_subscription"] = "All";
            }
            else
            {
                string subName;
                if (((Dictionary<string, string>)ViewData["subscription"]).TryGetValue(cookie, out subName) && ValidateSubID(cookie))
                {
                    ViewData["selected_subscription_name"] = subName;
                    ViewData["selected_subscription"] = cookie;
                }
                else
                {   //Subid must be invalid. - reset cookie
                    HttpContext.Response.Cookies.Delete("sb");
                }
            }


            var defcore = _stRepo.GetSetting("vCoreCount");
            ViewData["quote_limit"] = int.Parse(defcore != null ? defcore.Value : "-1");
            var defenable = bool.Parse(_stRepo.GetSetting("Active").Value);
            ViewData["quote_enabled"] = defenable;

            return View();
        }

        // GET: Groups/Edit/5
        [RoleAuth(Roles = "QMSManager")]
        public ActionResult Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var group = _grRepo.GetGroup(id);
            if (group == null)
            {
                return NotFound();
            }
            return View(group);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleAuth(Roles = "QMSManager")]
        public ActionResult Edit(string id, Group group)
        {
            if (id != group.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var result = _grRepo.UpdateGroup(group.Name, group.SubscriptionID, group.Quote, group.IsEnabled);
                return RedirectToAction(nameof(Index));
            }
            return View(@group);
        }

        [RoleAuth(Roles = "QMSManager")]
        public JsonResult ForcePolicies(string id)
        {
            List<string> subList = new List<string>();
            if (id == null) return Json(new { result = 0, message = "SubscriptionId need to be provided" });

            if (_sublist == null) _sublist = _sbRepo.GetSubscriptiuonList();
            if (id == "All")
                subList = _sublist.Keys.ToList();
            else
                subList.Add(id);

            foreach (var sId in subList)
            {
                var subUpd = _grRepo.ForcePolicies(sId);
                if (!subUpd.Item1)
                {
                    return Json(new { result = 0, message = $"Forcing policy for subscription {sId} fail", reason = subUpd.Item2 });
                }
            }
            return Json(new { result = 1, message = "Policy for subscriptions successfully forced" });

        }

        private bool ValidateSubID(string subid)
        {
            if (_sublist == null) _sublist = _sbRepo.GetSubscriptiuonList();
            return subid == null ? false : _sublist.Any(x => string.Compare(x.Key, subid, true) == 0);
        }

        public JsonResult Getdata(string id)
        {
            var isManager = User.IsInRole("QMSManager");

            if (_sublist == null) _sublist = _sbRepo.GetSubscriptiuonList();
            var byversion = _grRepo.GetGroups(id == "All" ? null : ValidateSubID(id) ? id : null);

            var items = byversion.Select(x => new Group
            {
                SubscriptionID = _sublist.FirstOrDefault(s => s.Key == x.SubscriptionID).Value,
                Name = x.Name,//String.Format("<a href='/Home/Edit?id={0}'>{1}</a>", x.ID,x.Name),
                CurrentcCore = x.CurrentcCore,
                ID = x.ID,
                Quote = x.Quote,
                IsEnabled = x.IsEnabled,
                ReviewDate = x.ReviewDate,
                Actions = isManager ? String.Format("<a class='btn btn-default' style='padding-top:0px;padding-bottom:0px' href='/Home/Edit?id={0}'>Edit</a>", x.ID) : "",
            }).OrderByDescending(x => x.Name).ToList();

            var gridParams = GridHelper.ParseGridParams(HttpUtility.ParseQueryString(HttpContext.Request.QueryString.Value));
            if (String.IsNullOrEmpty(gridParams.SortName)) gridParams.SortName = "Name";
            string[] intColumnsName = new string[] { "vCore", "Quote" };

            var rows = items.Select(b => new FlexGridRow() { id = b.ID, cell = b.ConvertToKeyValue() }).ToList();

            if (!string.IsNullOrEmpty(gridParams.Query))
            {
                rows = rows.Where(x => x.cell[gridParams.QueryField].Contains(gridParams.Query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            }
            if (intColumnsName.Contains(gridParams.SortName))
            {
                rows =
                (gridParams.IsSortOrderDesc
                    ? rows.OrderByDescending(x => int.Parse(x.cell[gridParams.SortName]))
                    : rows.OrderBy(x => int.Parse(x.cell[gridParams.SortName]))).ToList();
            }
            else
            {
                rows =
                (gridParams.IsSortOrderDesc
                    ? rows.OrderByDescending(x => x.cell[gridParams.SortName])
                    : rows.OrderBy(x => x.cell[gridParams.SortName])).ToList();
            }

            var rowspage = rows.Skip(gridParams.RowsRequested * (gridParams.Page - 1)).Take(gridParams.RowsRequested).ToList();

            var model = new FlexGridDataSource() { page = gridParams.Page, total = rows.Count(), rows = rowspage.ToArray() };
            return Json(model);
        }

        [HttpPost]
        [RoleAuth(Roles = "QMSManager")]
        public JsonResult BulkUpdate(BulkUpdate update)
        {
            if (update == null || update.Groups == null || update.Groups.Count == 0)
                return Json(new { result = 0, message = "No groups provided for update" });

            foreach (var groupid in update.Groups)
            {
                var group = _grRepo.GetGroup(groupid);
                if (update.chCore) group.Quote = update.vCore;
                if (update.chEnbl) group.IsEnabled = update.Enbl == 1;

                var result = _grRepo.UpdateGroup(group.Name, group.SubscriptionID, group.Quote, group.IsEnabled);
                if (!result.Item1)
                    return Json(new { result = 0, message = $"Group '{group.Name}' Subscription '{group.SubscriptionID}' update fail!", reason = result.Item2 });

            }
            return Json(new { result = 1, message = "Groups has been update successfully!" });

        }

    }
}
