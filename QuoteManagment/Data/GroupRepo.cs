using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Walmart;

namespace QuoteManagement.Models
{
    public class GroupRepo
    {
        private IConfiguration _configuration;

        public GroupRepo(IConfiguration configuration, ILogger logger)
        {
            _configuration = configuration;
            
        }
        public void GroupRepoInit()
        {
            TableRepo.Init(_configuration["StorageConnection"]);
        }

        public List<Group> GetGroups(string subid=null)
        {
            var tablegroups = TableRepo.GetGroups(subid).Result;
            return tablegroups.Select(x =>new Group(x.GroupName, x.PartitionKey) { Name = x.GroupName, CurrentcCore = x.CurrentVCore, IsEnabled = x.Enabled, Quote = x.QuoteLimit, ReviewDate = x.ReviewDate }).ToList();
        }

        public Tuple<bool,string> UpdateGroupForBulk(string GroupID, int? vCore, int? Enabled)
        {
            var result = TableRepo.GetGroup(GroupID).Result;
            if (result == null) return new Tuple<bool, string>(false, $"The Group with ID '{GroupID}' not found");

            if (vCore.HasValue) result.QuoteLimit = vCore.Value;
            if (Enabled.HasValue) result.Enabled = Enabled.Value==1;
            TableRepo.UpdateGroup(result);
            return ForcePolicies(GroupID);
        }

        public Tuple<bool, string> UpdateGroup(string GroupName, string SubscriptionID, int Quote, bool isEnabled)
        {
            var result = TableRepo.GetGroup(GroupName, SubscriptionID).Result;
            if (result == null) return new Tuple<bool, string>(false,$"The Group with ID  '{GroupName}' not found");

            result.QuoteLimit = Quote;
            result.Enabled = isEnabled;
            TableRepo.UpdateGroup(result);
            return ForcePolicies(SubscriptionID, GroupName);
        }

        public Group GetGroup(string GroupID)
        {
            var result= TableRepo.GetGroup(GroupID).Result;
            return result != null ? new Group(result.GroupName, result.PartitionKey) { Name = result.GroupName, CurrentcCore = result.CurrentVCore, IsEnabled = result.Enabled, Quote = result.QuoteLimit, ReviewDate = result.ReviewDate } : null;
        }

        public Tuple<bool, string> ForcePolicies(string subid, string GroupName)
        {
            using (var client = new HttpClient())
            {
                var link = _configuration["HttpFunctionWebHook"];
                link = link.Replace("{subid}", subid, StringComparison.InvariantCultureIgnoreCase);
                link = link.Replace("{group?}", GroupName);
                var result = client.PostAsync(link, null).Result;
                if (result.IsSuccessStatusCode)
                    return new Tuple<bool, string>(true, null);
                else
                    return new Tuple<bool, string>(false, result.Content.ReadAsStringAsync().Result);
            }
        }

        public Tuple<bool,string> ForcePolicies(string subid)
        {
            using (var client = new HttpClient() { Timeout = TimeSpan.FromHours(1) })
            {
                var link = _configuration["HttpFunctionWebHook"];
                link = link.Replace("{subid}", subid, StringComparison.InvariantCultureIgnoreCase);
                link = link.Replace("/{group?}", null);
                var result = client.PostAsync(link, null).Result;
                if (result.IsSuccessStatusCode)
                    return new Tuple<bool, string>(true, null);
                else
                    return new Tuple<bool, string>(false, result.StatusCode  + ":" + result.Content.ReadAsStringAsync().Result);
            }
        }
    }
}
