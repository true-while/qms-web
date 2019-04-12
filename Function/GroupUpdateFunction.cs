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

        public static ILogger _log = null;
        public static RMAPIRepo _apiRepo = null;

        static Dictionary<string, List<VMSize>> dicVMSizes = new Dictionary<string, List<VMSize>>();

        #endregion Environment


        //Queue Triggered function
        [FunctionName("EventGridQuotes")]
        public static async Task EventGridQuotes([QueueTrigger("grid-msg")]string jsondata, ILogger logger)
        {
            _log = logger;
            _apiRepo = new RMAPIRepo(_log, TenantId, SubscriptionId, AppID, AppKey);

    
            var tmp = new { data = new { subscriptionId = "", tenantId = "", resourceUri = "", operationName = "" } };
            var data = (JsonConvert.DeserializeAnonymousType(jsondata, tmp)).data;

            _log.LogInformation(jsondata);

            if (data.operationName.Contains("Microsoft.Compute/virtualMachines", StringComparison.InvariantCultureIgnoreCase))  //include Microsoft.Compute/virtualMachineScaleSets
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
                    var result = await TableRepo.DeleteResourceGroupInfo( context.SubscriptionID, context.GroupName);
                    _log.LogInformation($"GroupName: {context.GroupName} was deleted {(result.Item1 ? $"unsuccessful {result.Item2}" : "successful")}");
                }
            }
            else if (data.operationName.Contains("Microsoft.Resources/subscriptions/resourceGroups/write", StringComparison.InvariantCultureIgnoreCase))
            {
                var context = GetOperationContext(data.resourceUri);
                if (context.GroupName != null && context.SubscriptionID != null)
                {
                    TableRepo.Init(GroupUpdateFunctions.StorageConnection);
                    var group = await TableRepo.GetGroup(context.GroupName, context.SubscriptionID);
                    if (group==null)
                    {
                        TableRepo.CreateNewGroup(new GroupTableItem(context.GroupName, context.SubscriptionID));
                        group = await TableRepo.GetGroup(context.GroupName, context.SubscriptionID);
                        _log.LogInformation($"GroupName: {context.GroupName} was created successful");
                        if (group.Enabled)
                        {
                            //set limits
                            var RgGroup = await GetGroupByName(context.SubscriptionID, context.GroupName);
                            var result = await ProcessGroup(RgGroup);
                            _log.LogInformation($"GroupName: {context.GroupName} was processed successful");
                        }
                    }
                    else
                        _log.LogInformation($"GroupName: {context.GroupName} was checked");
                 }
            }
            else
            {
                _log.LogInformation($"No action for '{data.operationName}'");
            }
        }
     
        //WebHook triggered function
        [FunctionName("GroupUpdateFunction")]
        public static async Task<HttpResponseMessage> GroupUpdateFunction(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GroupUpdateFunction/{subID}/{group?}")] HttpRequestMessage req, string subID, string group, ILogger logger)
        {
            _log = logger;
            _log.LogInformation(string.Format("Call params: SubID {0}  GroupID {1}", subID, group));


            if (string.IsNullOrEmpty(subID)) throw new ArgumentException("Subscription id not valid");
            var result = await InternalRun(subID, group);
            if (result.Item1.Count == 0 && result.Item2.Count == 0)
            {
                _log.LogError("Groups are disabled or not found");
                return req.CreateResponse(System.Net.HttpStatusCode.NoContent, "Groups are disabled or not found");
            }
            else if (result.Item2.Count > 0)
            {
                result.Item2.ForEach(x => _log.LogError(x));
                return req.CreateResponse(System.Net.HttpStatusCode.InternalServerError, result.Item2.ToArray());
            }
            else
            {
                result.Item1.ForEach(x => _log.LogInformation(String.Format("Group {0} has been checked",x.name)));
            }

            return req.CreateResponse(System.Net.HttpStatusCode.OK,result.Item1); 
        }

        private static (string SubscriptionID, string GroupName) GetOperationContext(string resourceUri)
        {
            ///subscriptions/836636e0-2eca-400d-bbe7-b8bf8c12e7ff/resourcegroups/ARM_Deploy_Staging
            var match = Regex.Match(resourceUri, "subscriptions/(?<sb>[^/]*)/resourcegroups/(?<gr>[^/]*)", RegexOptions.IgnoreCase);
            _log.LogInformation($"SbID {match.Groups["sb"].Value}: Gr { match.Groups["gr"].Value}");
            return (match.Success ? match.Groups["sb"].Value : null, match.Success ? match.Groups["gr"].Value : null);
        }

        private static async Task<Tuple<List<ResouerceGroup>,List<string>>> InternalRun(string SubID, string GroupName)
        {
            _log.LogInformation("Call for '" + SubID + "' and Group Name '" + GroupName + "'");

            var errors = new List<string>();
            _apiRepo = new RMAPIRepo(_log, TenantId, SubscriptionId, AppID, AppKey);
            TableRepo.Init(GroupUpdateFunctions.StorageConnection);
            var output = new List<ResouerceGroup>();
            
            var groups = GroupName ==null ? new List<ResouerceGroup>(await GetGroupList(SubID)) : new List<ResouerceGroup>() { await GetGroupByName(SubID, GroupName) };

            _log.LogInformation("Groups found count: {0}", groups.Count);
            foreach ( var g in groups)
            {
                if (!String.IsNullOrEmpty(g.name))
                {
                    var result = await ProcessGroup(g);
                    if (result.Item2)
                        output.Add(result.Item1); //add to output successfully processed groups
                    else
                        errors.Add(result.Item3); 
                       
                }
            }
            return new Tuple<List<ResouerceGroup>, List<string>>(output,errors);
        }

        private static async Task<Tuple<ResouerceGroup, bool, string>> ProcessGroup(ResouerceGroup group)
        {
            try
            {
                //Calculate QuoteLitmit
                group.vCurrCoreCount = 0;
                group.SubID = ResouerceGroup.GetSubID(group.id);
                Tuple<int, int, bool> CorInfo = await GetQuoteLimitPerGroup(group.SubID, group.name);
                group.QuoteLimit = CorInfo.Item1; //Limit
                group.vPrevCoreCount = CorInfo.Item2;   //Current
                group.Enabled = CorInfo.Item3;

                _log.LogInformation($"Group: {group.name}");

                if (!group.Enabled)
                {
                    var reuslt = await SetRemoveAllPolicies(group);
                    return new Tuple<ResouerceGroup, bool, string>(group, reuslt.Item1, reuslt.Item2);
                }

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

                var UpdateResult = new Tuple<bool, string>(false, null);
                //Depend on result of calculation
                if (group.QuoteLimit == -1 || (group.QuoteLimit - group.vPrevCoreCount) > 128)
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
                    UpdateResult = await SetLmitDeployPolicies(group);
                }

                return new Tuple<ResouerceGroup, bool, string>(group, UpdateResult.Item1, UpdateResult.Item2);
            }
            catch (Exception ex)
            {
                _log.LogError(ex.ToString());
                return new Tuple<ResouerceGroup, bool, string>(group, false, ex.Message);
            }
            
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
            //_log.LogInformation(string.Format("GetGroupList return:{0}", jsondata));
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
        private static async Task<Tuple<bool,string>> SetDenyPerGroup(ResouerceGroup group)
        {
            var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
            if (!isLimited.Item1 && !String.IsNullOrEmpty(isLimited.Item2))
                return isLimited; //must be error
            else if (isLimited.Item1)
            {
                var rmResult = await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);
                if (!rmResult.Item1) return rmResult;
            }
            return await _apiRepo.AssigmentDenyPolicy(group.name, group.SubID, policyNoDeployID);
        }

        /// <summary>
        /// Before setting policy the current policy need to be removed. If the quote does not change keep the assignment
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        private static async Task<Tuple<bool,string>> SetLmitDeployPolicies(ResouerceGroup group)
        {
            var isDeny = await _apiRepo.IsDenyPolicy(group.name, group.SubID,policyNoDeployID);
            if (!isDeny.Item1 && !String.IsNullOrEmpty(isDeny.Item2))
                return isDeny; //must be error
            else if (isDeny.Item1)
            {
                var rmDeny = await _apiRepo.RemoveDenyPolicy(group.name, group.SubID, policyNoDeployID);
                if (!rmDeny.Item1) return rmDeny;
            }

            var vCore = group.QuoteLimit - group.vCurrCoreCount;
            var maxCore = dicVMSizes[group.location].Max(x => x.numberOfCores);
            if (vCore < maxCore)
            {
                var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
                if (!isLimited.Item1 && !String.IsNullOrEmpty(isLimited.Item2))
                    return isLimited;
                else if (isLimited.Item1)
                    await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);

            }
            var CurrentLimitVCore = await _apiRepo.GetLimitedAccessPolicyQuote(group.name, group.SubID, policyLimitedID);
            if (CurrentLimitVCore == vCore) return new Tuple<bool, string>(true,null);

            var availableSizes= dicVMSizes[group.location].Where(x => x.numberOfCores <= vCore).Select(x => x.name).ToList();
            return await _apiRepo.AssigmentLimitedPolicy(group.name, vCore, availableSizes, group.SubID, policyLimitedID);
        }

        /// <summary>
        /// Get current quote from existed policy assignment
        /// </summary>
        /// <param name="Groupname">Group name</param>
        /// <returns></returns>
        private static async Task<Tuple<int, int,bool>> GetQuoteLimitPerGroup(string SubID, string Groupname)
        {
            return await TableRepo.GetGroupQuote(Groupname, SubID);
        }

        /// <summary>
        /// Remove all existed policies from Resource Group
        /// </summary>
        /// <param name="name">Group Name</param>
        /// <returns></returns>
        private static async Task<Tuple<bool,string>> SetRemoveAllPolicies(ResouerceGroup group)
        {
            var isDeny = await _apiRepo.IsDenyPolicy(group.name, group.SubID, policyNoDeployID);
            if (!isDeny.Item1 && !String.IsNullOrEmpty(isDeny.Item2))
                return isDeny; //must be error
            else if (isDeny.Item1)
            {
                var rmDeny = await _apiRepo.RemoveDenyPolicy(group.name, group.SubID, policyNoDeployID);
                if (!rmDeny.Item1) return rmDeny;
            }
            var isLimited = await _apiRepo.IsLimitedPolicy(group.name, group.SubID, policyLimitedID);
            if (!isLimited.Item1 && !String.IsNullOrEmpty(isLimited.Item2))
                return isLimited; //must be error
            else if (isLimited.Item1)
            {
                var rmLim = await _apiRepo.RemoveLimitedPolicy(group.name, group.SubID, policyLimitedID);
                if (!rmLim.Item1) return rmLim;
                
            }
            return new Tuple<bool, string>(true,null);
        }

    }
}
