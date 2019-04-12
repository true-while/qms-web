
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

namespace Walmart
{
    public class ResouerceGroups
    {
        public ResouerceGroup[] value;
    }

    public class ResouerceGroup
    {
        public String id { get; set; }
        public String location { get; set; }
        public String name { get; set; }
        public String SubID { get; set; }
        public int QuoteLimit { get; set; }
        public int vPrevCoreCount { get; set; }
        public int vCurrCoreCount { get; set; }
        public Dictionary<string, string> properties { get; set; }
        public bool Enabled { get;  set; }

        internal static string GetSubID(string id)
        {
            Regex r = new Regex("/subscriptions/(?<id>[^/]*)/", RegexOptions.IgnoreCase);
            var mach = r.Match(id);
            if (mach.Success)
            {
                return mach.Groups["id"].Value;
            }
            return null;
        }
    }
}