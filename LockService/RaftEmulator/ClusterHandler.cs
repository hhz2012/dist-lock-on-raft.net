using LockService;
using Raft.Core.Handler;
using Raft.RaftEmulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class ClusterManagerHandler : ActionHandlerBase
    {
        public ClusterManagerHandler():base()
        {

        }
        
        public override bool DoAction(string entityName, ulong index, byte[] data)
        {
            //Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}");
            //handle shards operation
            try
            {
                string str = System.Text.Encoding.Default.GetString(data);
                string json = str;
                ClusterCommand command = Newtonsoft.Json.JsonConvert.DeserializeObject<ClusterCommand>(json);
                Console.WriteLine($"command received:{command.Command},{command.Target}");
                if (command.Command== "CreateShard")
                {

                }
            }
            catch (Exception ex)
            {

            }
            return true;
        }
    }
}
