using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteManagment.Models
{
    public enum SettingSource
    {
        Web =0,
        Default =1
    }
    public class Setting
    {
        [Key]
        public string Name { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public SettingSource Source { get; set; }
    }
}
