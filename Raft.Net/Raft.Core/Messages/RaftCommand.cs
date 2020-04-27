using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core
{
    public class RaftCommand
    {
        public int Code { get; set; }
        public object Message { get; set; }
    }
}
