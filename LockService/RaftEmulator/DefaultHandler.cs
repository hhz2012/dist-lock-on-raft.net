using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class DefaultHandler : ActionHandlerBase
    {
        public override bool DoAction(string entityName, ulong index, byte[] data)
        {
            //Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}"); 
            return true;
        }
    }
}
