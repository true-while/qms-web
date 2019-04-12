using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteManagment.Models
{
    public class BulkUpdate
    {
        public List<string> Groups { get; set; }
        public bool chCore { get; set; }
        public int vCore { get; set; }
        public bool chEnbl { get; set; }
        public int Enbl { get; set; }
    }

}
