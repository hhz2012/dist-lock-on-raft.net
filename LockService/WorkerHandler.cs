using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class WorkerHandler : ActionHandlerBase
    {
        public override bool DoAction(string entityName, ulong index, byte[] data)
        {
            Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}");
            //handle shards operation
            try
            {
                string str = System.Text.Encoding.Default.GetString(data);
                Console.WriteLine(str + " is received");
            }
            catch (Exception ex)
            {

            }
            return true;
        }
    }
}
