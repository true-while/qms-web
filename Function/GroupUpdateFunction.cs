using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.EventGrid.Models;
using Polly;
using System.Web.Http;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Walmart
{

    public static class GroupUpdateFunctions
    {
        #region Environment
        public static string token = null;
        public static String SubscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
        public static String TenantId = Environment.GetEnvironmentVariable("TenantId");
        public static String AppID = Environment.GetEnvironmentVariable("AppID");
        public static String AppKey = Environment.GetEnvironmentVariable("AppKey");
        public static String StorageConnection = Environment.GetEnvironmentVariable("StorageConnection");

        public static String policyNoDeployID = Environment.GetEnvironmentVariable("policyNoDeployID");
        public static String policyLimitedID = Environment.GetEnvironmentVariable("policyLimitedID");

        #endregion Environment

        public static ILogger _log = null;
        public static RMAPIRepo _apiRepo = null;

        static Dictionary<string, List<VMSize>> dicVMSizes = new Dictionary<string, List<VMSize>>();

        

        [FunctionName("EventGridQuotes")]
        public static async void EventGridQuotes([QueueTrigger("grid-msg")]string jsondata, ILogger logger)
        {
            _log = logger;
            _apiRepo = new RMAPIRepo(_log, TenantId, SubscriptionId, AppID, AppKey);

    
            var tmp = new { data = new { subscriptionId = "", tenantId = "", resourceUri = "", operationName = "" } };
            var data = (JsonConvert.DeserializeAnonymousType(jsondata, tmp)).data;

            _log.LogInformation(jsondata);
   
            if (data.operationName.Contains("Microsoft.Compute/virtualMachines",StringComparison.InvariantCultureIgnoreCase))  //include Microsoft.Compute/virtualMachineScaleSets
            {
                var context = GetOperationContext(data.resourceUri);
                if (context.GroupName != null && context.SubscriptionID != null)
                {
                    TableRepo.Init(GroupUpdateFunctions.StorageConnection);
                    var group = await GetGroupByName(context.SubscriptionID, context.GroupName);
                    var result = await ProcessGroup(group);
                    _log.LogInformation($"GroupName: {context.GroupName} was updated {(result == null ? "unsuccessful" : "successful")}");
                }
            }
            else if (data.operationName.Contains("Microsoft.Resources/subscriptions/resourceGroups/delete", StringComparison.InvariantCultureIgnoreCase))
            {
                var context = GetOperationContext(data.resourceUri);
                if (context.GroupName != null && context.SubscriptionID != null)
                {
                    TableRepo.Init(GroupUpdateFunctions.StorageConnection);
                    var result = await TableRepo.DeleteResourceGroupInfo(context.GroupName, context.SubscriptionID);
                    _log.LogInformation($"GroupName: {context.GroupName} was deleted {(result ? "unsuccessful" : "successful")}");
                }
            }else
            {
                _log.LogInformation($"No action for ${data.operationName}");
            }
        }

        private static (string SubscriptionID, string GroupName) GetOperationContext(string resourceUri)
        {
            ///subscriptions/836636e0-2eca-400d-bbe7-b8bf8c12e7ff/resourcegroups/ARM_Deploy_Staging
            var match = Regex.Match(resourceUri, "subscriptions/(?<sb>[^/]*)/resourcegroups/(?<gr>[^/]*)", RegexOptions.IgnoreCase);
             _log.LogInformation($"SbID {match.Groups["sb"].Value}: Gr { match.Groups["gr"].Value}");
            return  (match.Success ? match.Groups["sb"].Value : null, match.Success ? match.Groups["gr"].Value : null);
        }
        
     
        [FunctionName("GroupUpdateFunction")]
        public static async Task<HttpResponseMessage> GroupUpdateFunction(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GroupUpdateFunction/{subID}/{group?}")] HttpRequestMessage req, string subID, string group, ILogger logger)
        {
            if (string.IsNullOrEmpty(subID)) throw new ArgumentException("Subscription id not valid");
            var result = await InternalRun(subID, group, logger);
            return req.CreateResponse(result.Count > 0 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound, result); 
        }


        private static async Task<List<ResouerceGroup>> InternalRun(string SubID, string GroupName, ILogger logger)
        {
            _log = logger;
            _apiRepo = new RMAPIRepo(_log, TenantId, SubscriptionId, AppID, AppKey);
            TableRepo.Init(GroupUpdateFunctions.StorageConnection);
            var output = new List<ResouerceGroup>();
            
            var groups = GroupName ==null ? new List<ResouerceGroup>(await GetGroupList(SubID)) : new List<ResouerceGroup>() { await GetGroupByName(SubID, GroupName) };
            foreach( var g in groups)
            {
                if (!String.IsNullOrEmpty(g.name))
                    output.Add(await ProcessGroup(g));
            }
            return output;
        }

        private static async Task<ResouerceGroup> ProcessGroup(ResouerceGroup group)
        {
            //Calculate QuoteLitmit
            group.vCurrCoreCount = 0;
            group.SubID = ResouerceGroup.GetSubID(group.id);
            Tuple<int, int> CorInfo = await GetQuoteLimitPerGroup(group.SubID, group.name);
            group.QuoteLimit = CorInfo.Item1;
            group.vPrevCoreCount = CorInfo.Item2;

            _log.LogInformation($"Group: {group.name}");

            if (!dicVMSizes.ContainsKey(group.location)) dicVMSizes[group.location] = new List<VMSize>(await GetVMSizePerLocation(group.SubID, group.location));

            //Calculate current vCore count
            List<VM> vms = new List<VM>(await GetVMList(group.SubID, group.name));
            foreach (var vm in vms)
            {
                if (!dicVMSizes.ContainsKey(vm.location)) dicVMSizes[vm.location] = new List<VMSize>(await GetVMSizePerLocation(group.SubID, vm.location));
                var vmsize = vm.properties.hardwareProfile["vmSize"];
                var size = dicVMSizes[vm.location].Where(x => String.Compare(x.name, vmsize, true) == 0).FirstOrDefault();
                if (size == null) throw new Exception($"The VM Size {vmsize} not found in location {group.location}");
                group.vCurrCoreCount += size.numberOfCores;
            }

            await TableRepo.UpdateCurrentCorreCount(group.name, group.SubID, group.vCurrCoreCount);

            if (group.vCurrCoreCount != group.vPrevCoreCount /*Core count changed*/)
            {
                _log.LogWarning($"Group {group.name} has been update from {group.vPrevCoreCount} to {group.vCurrCoreCount} vCores");
            }

            var UpdateResult = false;
            //Depend on result of calculation
            if (group.QuoteLimit == -1 || group.QuoteLimit > 128)
            {
                //remove all assignment
                UpdateResult = await SetRemoveAllPolicies(group);
            }
            else if (group.QuoteLimit <= group.vCurrCoreCount)
            {
                //set no-deploy
                UpdateResult = await SetDenyPerGroup(group);
            }
            else
            {
                //set limited deploy
                UpdateResult =await SetLmitDeployPolicies(group);
            }

            return group;
        }

        /// <summary>
        /// Function return available VM's SKU by location for setting policy
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        private static async Task<VMSize[]> GetVMSizePerLocation(string SubID, string location)
        {
            var jsondata = await _apiRepo.SendGetRequest(SubID, $"providers/Microsoft.Compute/locations/{location}/vmSizes", "2018-06-01");
            var data = JsonConvert.DeserializeObject<VMSizes>(jsondata);
            return data.value;
        }

        /// <summary>
        /// Function return the Resource Group List
        /// </summary>
        /// <returns></returns>
        private static async Task<ResouerceGroup[]> GetGroupList(string SubID)
        {
            var jsondata = await _apiRepo.SendGetRequest(SubID, "resourcegroups", "2018-05-01");
            var data = JsonConvert.DeserializeObject<ResouerceGroups>(jsondata);
            return data.value;
        }

        private static async Task<ResouerceGroup> GetGroupByName(string SubID, string RgName)
        {
            var jsondata = await _apiRepo.SendGetRequest(SubID, $"resourcegroups/{RgName}", "2018-05-01");
            var data = JsonConvert.DeserializeObject<ResouerceGroup>(jsondata);
            return data;
        }

        /// <summary>
        /// Function return list of deployed VMs
        /// </summary>
        /// <param name="groupname"></param>
        /// <returns></returns>
        private static async Task<VM[]> GetVMList(string SubID, string groupname)
        {
            var jsondata = await _apiRepo.SendGetRequest(SubID, $"resourceGroups/{groupname}/providers/Microsoft.Compute/virtualMachines", "2018-06-01");
            VMList data = JsonConvert.DeserializeObject<VMList>(jsondata);
            return data.value;
        }

        /// <summary>
        /// Set deny policy for Resource Group, but before remove Limited if that exists
        /// </summary>
        /// <param name="Groupname"></param>
        /// <returns></returns>
        private static async Task<bool> SetDenyPerGroup(ResouerceGroup group)
        {
            var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
            if (isLimited) await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);
            return await _apiRepo.AssigmentDenyPolicy(group.name, group.SubID, policyNoDeployID);
        }

        /// <summary>
        /// Before setting policy the current policy need to be removed. If the quote does not change keep the assignment
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        private static async Task<bool> SetLmitDeployPolicies(ResouerceGroup group)
        {
            var isDeny = await _apiRepo.IsDenyPolicy(group.name, group.SubID,policyNoDeployID);
            if (isDeny) await _apiRepo.RemoveDenyPolicy(group.name, group.SubID, policyNoDeployID);

            var vCore = group.QuoteLimit - group.vCurrCoreCount;
            var maxCore = dicVMSizes[group.location].Max(x => x.numberOfCores);
            if (vCore > maxCore)
            {
                var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
                if (isLimited) await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);
                return false;
            }
            var CurrentLimitVCore = await _apiRepo.GetLimitedAccessPolicyQuote(group.name, group.SubID, policyLimitedID);
            if (CurrentLimitVCore == vCore) return true;

            var availableSizes= dicVMSizes[group.location].Where(x => x.numberOfCores <= vCore).Select(x => x.name).ToList();
            return await _apiRepo.AssigmentLimitedPolicy(group.name, vCore, availableSizes, group.SubID, policyLimitedID);
        }

        /// <summary>
        /// Get current quote from existed policy assignment
        /// </summary>
        /// <param name="Groupname">Group name</param>
        /// <returns></returns>
        private static async Task<Tuple<int, int>> GetQuoteLimitPerGroup(string SubID, string Groupname)
        {
            return await TableRepo.GetGroupQuote(Groupname, SubID);
        }

        /// <summary>
        /// Remove all existed policies from Resource Group
        /// </summary>
        /// <param name="name">Group Name</param>
        /// <returns></returns>
        private static async Task<bool> SetRemoveAllPolicies(ResouerceGroup group)
        {
            var isDeny = await _apiRepo.IsDenyPolicy(group.name, group.SubID, policyNoDeployID);
            var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
            if (isDeny) await _apiRepo.RemoveDenyPolicy(group.name, group.SubID, policyNoDeployID);
            if (isLimited) await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);

            return true;
        }

    }
}
