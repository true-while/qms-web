using Function.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Walmart;

namespace QuoteManagment.Data
{
    public class SubsRepo
    {
        private IConfiguration _configuration;
        private RMAPIRepo _apiRepo;
        private ILogger _logger;

        public SubsRepo(IConfiguration configuration, ILogger logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Dictionary<string,string> GetSubscriptiuonList()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            var subs = _apiRepo.GetSubscription().Result;
            foreach(var s in subs)
            {
                result.Add(s.ID, s.Name);
            }
            return result;
        }

        public void SubRepoInit()
        {
            _apiRepo = new RMAPIRepo(_logger, _configuration["TenantId"], _configuration["SubscriptionId"], _configuration["AppID"], _configuration["AppKey"]);
        }
    }
}
