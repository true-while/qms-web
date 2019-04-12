using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Polly;
using System.Web.Http;
using Function.Models;

namespace Walmart
{

    public class RMAPIRepo
    {
        private string _TenantId;
        private string _AppID;
        private string _AppKey;
        private string _AppSubscriptionID;
        private string _Token;
        private ILogger _log;

        public RMAPIRepo(ILogger log, string tenantId, string appSubID, string appId, string appKey)
        {
            _TenantId = tenantId;
            _AppID = appId;
            _AppKey = appKey;
            _AppSubscriptionID = appSubID;
            _log = log;
        }

        public Policy retry = Policy.Handle<HttpResponseException>().
            WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(2));
        
        public async Task<IEnumerable<Subscription>> GetSubscription()
        {

            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    client.BaseAddress = new Uri($"https://management.azure.com/subscriptions?api-version=2016-06-01");
                    var result = await client.GetAsync("");

                    var json = Encoding.UTF8.GetString(await result.Content.ReadAsByteArrayAsync());

                    var tmp = new { value = new[] { new { displayName = "", subscriptionId = "" } } };
                    var data = JsonConvert.DeserializeAnonymousType(json, tmp);

                    return data.value.Select(x => new Subscription() { ID = x.subscriptionId, Name = x.displayName }).AsEnumerable<Subscription>();

                }
            });
            
        }

        public async Task<string> GetS2SAccessToken(string TenantId, string AppID, string AppKey)
        {
            if (_Token == null)
            {
                string authContextURL = "https://login.windows.net/" + TenantId;

                var authenticationContext = new AuthenticationContext(authContextURL);

                var credential = new ClientCredential(AppID, AppKey);
                var result = await authenticationContext.AcquireTokenAsync(resource: "https://management.azure.com/", clientCredential: credential);

                if (result == null)
                {
                    throw new InvalidOperationException("Failed to obtain the token");
                }

                _Token = result.AccessToken;
            }

            return _Token;
        }

        public async Task<string> SendGetRequest(string subid, string servicename, string version)
        {
            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    client.BaseAddress = new Uri($"https://management.azure.com/subscriptions/{subid}/{servicename}?api-version={version}");
                    var result = await client.GetAsync("");
                    return await result.Content.ReadAsStringAsync();                    
                }
            });
        }

        public async Task<Tuple<bool,string>> AssigmentDenyPolicy(string GroupName, string SubscriptionID, string policyNoDeployID)
        {   
            var policyAssiment = new PolicyAssigment();
            policyAssiment.properties = new Properties()
            {
                description = "This policy assignment prevent of deployment new VM or changed existed in Resource Group",
                displayName = "Quote monitoring - temporary deny policy",
                parameters = null,
                policyDefinitionId = $"/subscriptions/{SubscriptionID}/providers/Microsoft.Authorization/policyDefinitions/{policyNoDeployID}",
                scope = $"/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}"
            };

            string NoDeploymentPolicyAssigment = JsonConvert.SerializeObject(policyAssiment);
            
            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);
            var NoDeploymentContent = new StringContent(NoDeploymentPolicyAssigment, Encoding.UTF8, "application/json");

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var putUrl = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyNoDeployID}?api-version=2017-06-01-preview");
                    var result = await client.PutAsync(putUrl, NoDeploymentContent);
                    _log.LogInformation($"Deny Policy assigned for gr:{GroupName}");
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });
        }

        public async Task<Tuple<bool,string>> AssigmentLimitedPolicy(string GroupName, int MaxSizevCore , List<string> AvailableSizes, string SubscriptionID, string policyLimitedID)
        {
            var policyAssiment = new PolicyAssigment();
            policyAssiment.properties = new Properties()
            {
                description = $"Temporary limitation from deploying VM more then {MaxSizevCore} vCore",
                displayName = $"Quote limited policy by /{MaxSizevCore}/ vCore VMs sizes",
                parameters = new Parameters() { allowedVMsize = new AllowedVMsize() { value = AvailableSizes } }, 
                policyDefinitionId = $"/subscriptions/{SubscriptionID}/providers/Microsoft.Authorization/policyDefinitions/{policyLimitedID}",
                scope = $"/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}"
            };

            string LimitedDeploymentPolicyAssigment = JsonConvert.SerializeObject(policyAssiment);

            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);
            var LimitedDeploymentContent = new StringContent(LimitedDeploymentPolicyAssigment, Encoding.UTF8, "application/json");

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var putUrl = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyLimitedID}?api-version=2017-06-01-preview");
                    var result = await client.PutAsync(putUrl, LimitedDeploymentContent);
                    _log.LogInformation($"LimitedPolicy assigned for gr:{GroupName} available core count to deploy: {MaxSizevCore}");
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });
        }

        public async Task<Tuple<bool, string>> IsDenyPolicy(string GroupName, string SubscriptionID, string policyNoDeployID)
        {
            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var url = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyNoDeployID}?api-version=2017-06-01-preview");
                    var result = await client.GetAsync(url);
                    _log.LogInformation($"DenyPolicy {(result.StatusCode == System.Net.HttpStatusCode.NotFound ? "not" : "")} found for gr:{GroupName}");
                    if (result.StatusCode == System.Net.HttpStatusCode.NotFound) return new Tuple<bool, string>(false, null);
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });

        }

        public async Task<int> GetLimitedAccessPolicyQuote(string GroupName, string SubscriptionID, string policyLimitedID)
        {
            var vCoreCount = 0;

            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var url = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyLimitedID}?api-version=2017-06-01-preview");
                    var result = await client.GetAsync(url);
                    if (result.IsSuccessStatusCode)
                    {
                        var json = Encoding.UTF8.GetString(await result.Content.ReadAsByteArrayAsync());
                        var match = Regex.Match(json, @"/(?<vCore>\d)/");
                        if (match.Success)
                        {
                            if (int.TryParse(match.Groups["vCore"].Value, out vCoreCount))
                            {
                                _log.LogInformation($"LimitedAccessPolicyQuote /{vCoreCount}/ retrieved for removed for gr:{GroupName}");
                                return vCoreCount;
                            }
                        }
                    }
                }
                return 0;
            });
        }

        public async Task<Tuple<bool, string>> IsLimitedPolicy(string GroupName, string SubscriptionID, string policyLimitedID)
        {
            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var url = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyLimitedID}?api-version=2017-06-01-preview");
                    var result = await client.GetAsync(url);
                    _log.LogInformation($"LimitedPolicy {(result.StatusCode == System.Net.HttpStatusCode.NotFound ? "not" : "")} found for gr:{GroupName}");
                    if (result.StatusCode == System.Net.HttpStatusCode.NotFound) return new Tuple<bool, string>(false, null);
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });
        }

        public async Task<Tuple<bool, string>> RemoveDenyPolicy(string GroupName, string SubscriptionID, string policyNoDeployID)
        {
            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var url = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyNoDeployID}?api-version=2017-06-01-preview");
                    var result = await client.DeleteAsync(url);
                    _log.LogInformation($"DenyPolicy removed for gr:{GroupName}");
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });
        }

        public async Task<Tuple<bool, string>> RemoveLimitedPolicy(string GroupName, string SubscriptionID, string policyLimitedID)
        {

            var queryToken = await GetS2SAccessToken(_TenantId, _AppID, _AppKey);

            return await retry.ExecuteAsync(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", queryToken);

                    var url = new Uri($"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{GroupName}/providers/Microsoft.Authorization/policyAssignments/{policyLimitedID}?api-version=2017-06-01-preview");
                    var result = await client.DeleteAsync(url);
                    _log.LogInformation($"LimitedPolicy removed for gr:{GroupName}");
                    return new Tuple<bool, string>(result.IsSuccessStatusCode, result.Content.ReadAsStringAsync().Result);
                }
            });
        }
    }
}