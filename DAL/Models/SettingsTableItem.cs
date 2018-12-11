using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace Walmart
{
    public class SettingTableItem: TableEntity
    {
        public SettingTableItem() { }
        public SettingTableItem(string name, string value, string source)
        {
            this.RowKey = GetRowkey(name);
            this.PartitionKey = GetPartitionKey(source);
        }
        public string Value { get; set; }
        public string Description { get; set; }

        internal static string GetRowkey(string settingName)
        {
            return $"{settingName}";
        }

        internal static string GetPartitionKey(string source)
        {
            return $"{source}";
        }
    }
}
