using System;
using System.Collections.Generic;
using System.Text;

namespace LockService
{
    public class ClusterCommand
    {
        public string Command { get; set; }

        public string Target { get; set; }

        public List<string> Targets { get; set; } = new List<string>();
        public List<EndPoint> IpAddress { get; set; } = new List<EndPoint>();
    }
    public class EndPoint
    {
        public string ipAddress { get; set; }
        public int port { get; set; }
    }
}
