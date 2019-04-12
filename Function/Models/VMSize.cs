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

 public class VMSizes
        {
            public VMSize[] value { get; set; }
        }

        public class VMSize
        {
            public string name { get; set; }
            public int numberOfCores { get; set; }
            public long osDiskSizeInMB { get; set; }
            public int resourceDiskSizeInMB { get; set; }
            public int memoryInMB { get; set; }
            public int maxDataDiskCount { get; set; }
        }
}