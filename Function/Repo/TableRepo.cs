using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Net;
using System.Collections.Generic;

namespace Walmart
{
    public static class TableRepo
    {
        static CloudTable groupTable;
        static CloudTable settingsTable;

        public static void Init(string connection)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(connection);
            CloudTableClient tableClient = account.CreateCloudTableClient();

            groupTable = tableClient.GetTableReference("Quotes");
            groupTable.CreateIfNotExistsAsync();

            settingsTable = tableClient.GetTableReference("Settings");
            settingsTable.CreateIfNotExistsAsync();
        }

#region Settings

        public static async Task<SettingTableItem[]> GetSettings()
        {
            TableQuery<SettingTableItem> query = new TableQuery<SettingTableItem>();

            var result = await settingsTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            return result.ToArray();
        }

        public static TableResult UpdateSetting(SettingTableItem sett)
        {
            TableOperation insertOperation = TableOperation.Merge(sett);
            return settingsTable.ExecuteAsync(insertOperation).Result;
        }

        public static async Task<SettingTableItem> GetSetting(string name)
        {
            TableQuery<SettingTableItem> query = new TableQuery<SettingTableItem>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, SettingTableItem.GetRowkey(name)));

            var result = await settingsTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            return result.FirstOrDefault();
        }

#endregion 


        public static async Task<GroupTableItem[]> GetGroups(string subid=null)
        {
            TableQuery<GroupTableItem> query = new TableQuery<GroupTableItem>();

            if (subid != null)
                query =  query.Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, subid));

            var result = await groupTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            return result.ToArray();
        }

        public static async Task<GroupTableItem> GetGroup(string GroupName, string SubID)
        {
            TableQuery<GroupTableItem> query = new TableQuery<GroupTableItem>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, GroupTableItem.GetRowkey(GroupName, SubID)));

            var result = await groupTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            return result.FirstOrDefault();
        }


        public static async Task<GroupTableItem> GetGroup(string GroupID)
        {
            TableQuery<GroupTableItem> query = new TableQuery<GroupTableItem>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, GroupID));

            var result = await groupTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            return result.FirstOrDefault();
        }
        public static async Task<Tuple<int, int,bool>> GetGroupQuote(string GroupName, string SubID)
        {
            
            TableQuery<GroupTableItem> query = new TableQuery<GroupTableItem>().Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, GroupTableItem.GetRowkey(GroupName, SubID)));

            var result = await groupTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            var group = result.FirstOrDefault();
            if (group == null)
            {
                //create new group record with no limits

                group = new GroupTableItem(GroupName, SubID);
                CreateNewGroup(group);
            }
            return new Tuple<int, int,bool> (group.QuoteLimit, group.CurrentVCore, group.Enabled);

        }

        public static TableResult CreateNewGroup(GroupTableItem newgroup)
        {
            var defcore = GetSetting("vCoreCount").Result;
            newgroup.QuoteLimit = int.Parse(defcore != null ? defcore.Value : "-1");
            var defenable = GetSetting("Active").Result;
            newgroup.Enabled = bool.Parse(defenable != null ? defenable.Value : "True");

            TableOperation insertOperation = TableOperation.Insert(newgroup);
            return groupTable.ExecuteAsync(insertOperation).Result;
        }

        public static async Task<bool> UpdateCurrentCorreCount(string GroupName, string SubID, int CurCoreCount)
        {
            TableQuery<GroupTableItem> query = new TableQuery<GroupTableItem>().Where(TableQuery.CombineFilters(
                      TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, GroupTableItem.GetRowkey(GroupName, SubID)), TableOperators.And,
                          TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, GroupTableItem.GetPartitionKey(SubID))));

            var result = await groupTable.ExecuteQuerySegmentedAsync(query, new TableContinuationToken());
            var group = result.FirstOrDefault();
            if (group != null)
            {
                group.CurrentVCore = CurCoreCount;
                //create new group record with no limits
                UpdateGroup(group);
            }
            else
            {
                group.CurrentVCore = CurCoreCount;
                group = new GroupTableItem(GroupName, SubID);
                CreateNewGroup(group);
            }
            return true;
        }

        public static async Task<Tuple<bool,string>> DeleteResourceGroupInfo(string SubID,string affectedGroupName)
        {
            var group = await GetGroup(affectedGroupName, SubID);
            if (group == null) return new Tuple<bool, string>(false,"Already deleted"); //already deleted or not found
            TableOperation del = TableOperation.Delete(group); 
            var result = await groupTable.ExecuteAsync(del);
            return result.HttpStatusCode == (int)HttpStatusCode.NoContent ? new Tuple<bool, string>(true,null) : new Tuple<bool, string>(false,$"status code: {result.HttpStatusCode}");
        }

        public static TableResult UpdateGroup(GroupTableItem group)
        {
            TableOperation insertOperation = TableOperation.Merge(group);
            return groupTable.ExecuteAsync(insertOperation).Result;
        }
    }


}