using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Walmart
{
    public class GroupTableItem : TableEntity
    {
        public GroupTableItem(){}
        public GroupTableItem(string Name,string Subscriptionid)
        {
            this.RowKey = GetRowkey(Name, Subscriptionid);
            this.PartitionKey = GetPartitionKey(Subscriptionid);
        }
        public int QuoteLimit {get;set;}
        public int CurrentVCore { get; set; }
        public string GroupName {get;set;}
        public DateTime ReviewDate {get;set;}
        public bool Enabled {get;set;}

        internal static string GetRowkey(string groupName, string subscriptionId)
        {
            return $"{groupName}-{subscriptionId}";
        }

        internal static string GetPartitionKey(string subscriptionId)
        {
            return subscriptionId;
        }
    }
}