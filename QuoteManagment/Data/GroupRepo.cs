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
            // GroupUpdateFunction.StorageConnection = configuration["StorageConnection"];
            TableRepo.Init(configuration["StorageConnection"]);
        }
        public List<Group> GetGroups(string subid=null)
        {
            var tablegroups = TableRepo.GetGroups(subid).Result;
            return tablegroups.Select(x =>new Group(x.GroupName, x.PartitionKey) { Name = x.GroupName, CurrentcCore = x.CurrentVCore, IsEnabled = x.Enabled, Quote = x.QuoteLimit, ReviewDate = x.ReviewDate }).ToList();
        }


        public bool UpdateGroup(string GroupName, string SubscriptionID, int Quote, bool isEnabled)
        {
            var result = TableRepo.GetGroup(GroupName, SubscriptionID).Result;
            if (result == null) return false;

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

        public bool ForcePolicies(string subid, string GroupName)
        {
            using (var client = new HttpClient())
            {
                var link = _configuration["HttpFunctionWebHook"];
                link = link.Replace("{subid}", subid, StringComparison.InvariantCultureIgnoreCase);
                link = link.Replace("{group}", GroupName);
                var result = client.PostAsync(link, null).Result;
                return result.IsSuccessStatusCode;
            }
        }

        public bool ForcePolicies(string subid)
        {
            using (var client = new HttpClient())
            {
                var link = _configuration["HttpFunctionWebHook"];
                link = link.Replace("{subid}", subid,StringComparison.InvariantCultureIgnoreCase);
                link = link.Replace("/{group}", null);
                var result = client.PostAsync(link, null).Result;
                return result.IsSuccessStatusCode;
            }
        }
    }
}
