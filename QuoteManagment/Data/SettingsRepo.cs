using Microsoft.Extensions.Configuration;
using QuoteManagement.Models;
using QuoteManagment.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Walmart;

namespace QuoteManagment.Data
{
    public class SettingsRepo
    {
        private IConfiguration _configuration;

        public SettingsRepo(IConfiguration configuration)
        {
            _configuration = configuration;
            TableRepo.Init(configuration["StorageConnection"]);
        }

        public List<Setting> GetSettings()
        {
            var tablesettings = TableRepo.GetSettings().Result;
            return tablesettings.Select(x => new Setting(){ Source = (SettingSource)Enum.Parse(typeof(SettingSource), x.PartitionKey), Name = x.RowKey, Value =x.Value, Description = x.Description }).ToList();
        }

        public bool UpdateSettings(string Name, string Value, string Source)
        {
            var result = TableRepo.GetSetting(Name).Result;
            if (result == null) return false;

            result.Value = Value;
            TableRepo.UpdateSetting(result);
            return true;
        }

        public Setting GetSetting(string Name)
        {
            var result = TableRepo.GetSetting(Name).Result;
            return result != null ? new Setting() { Source = (SettingSource)Enum.Parse(typeof(SettingSource),result.PartitionKey), Name = result.RowKey, Value = result.Value, Description = result.Description } : null;
        }
    }
}
