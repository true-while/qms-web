using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteManagment.Models
{
    public class FlexGridDataSource
    {
        public int page { get; set; }
        public int total { get; set; }
        public FlexGridRow[] rows { get; set; }
    }

    public class FlexGridRow
    {
        public string id { get; set; }
        public Dictionary<string, string> cell { get; set; }
    }
}
