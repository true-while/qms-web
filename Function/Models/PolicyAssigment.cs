using System;
using System.Collections.Generic;
using System.Text;

namespace Walmart
{
    public class AllowedVMsize
    {
        public List<string> value { get; set; }
    }

    public class Parameters
    {
        public AllowedVMsize allowedVMsize { get; set; }
    }

    public class RootObject
    {
        public Parameters parameters { get; set; }
    }

    public class Properties
    {
        public string description { get; set; }
        public string displayName { get; set; }
        public Parameters parameters { get; set; }
        public string policyDefinitionId { get; set; }
        public string scope { get; set; }
    }

    public class PolicyAssigment
    {
        public Properties properties { get; set; }
    }
}
