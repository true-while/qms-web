using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Walmart;

namespace QuoteManagement.Models
{
    public class Group 
    {
        public static string GetID(string Name, string SubscriptionID)
        {
            return $"{Name}-{SubscriptionID}";
        }
        public Group()
        { }
        public Group(string name, string subscriptionID)
        { Name = name; SubscriptionID = subscriptionID; }
        private string id;
        public string ID { get { return id ?? GetID(Name, SubscriptionID); } set { id = value; } }
        public string Name { get; set; }
        public string SubscriptionID { get; set; }
        public int Quote { get; set; }
        public int CurrentcCore { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime ReviewDate { get; set; }

        public Dictionary<string, string> ConvertToKeyValue()
        {
            var prop = typeof(Group).GetProperties();
            return prop.ToDictionary(x => x.Name, x =>
            {
                var val = x.GetValue(this);
                return val?.ToString() ?? "";
            });
        }
    }
}
