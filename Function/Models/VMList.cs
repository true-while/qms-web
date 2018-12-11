using System;
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
        public class VMList
        {
            public VM[] value;
        }

        public class VM
        {
            public VMProp properties { get; set; }
            public string name { get; set; }
            public string location { get; set; }
        }
        public class VMProp
        {
            public Dictionary<string, string> hardwareProfile { get; set; }
        }
}