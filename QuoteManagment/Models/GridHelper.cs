using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteManagment.Models
{
    public static class GridHelper
    {
        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source != null && toCheck != null && source.IndexOf(toCheck, comp) >= 0;
        }

        public class GridParams
        {
            public int Page { get; set; }
            public int RowsRequested { get; set; }
            public string SortName { get; set; }
            public bool IsSortOrderDesc { get; set; }
            public string Query { get; set; }
            public string QueryField { get; set; }
        }


        public static GridParams ParseGridParams(NameValueCollection context)
        {
            return new GridParams()
            {
                Page = context["page"] != null ? int.Parse(context["page"]) : 1,
                RowsRequested = context["rp"] != null ? int.Parse(context["rp"]) : 25,
                SortName = context["sortname"],
                IsSortOrderDesc = context["sortorder"] != null && context["sortorder"] == "desc",
                Query = context["query"],
                QueryField = context["qtype"]
            };

        }

    }
}
