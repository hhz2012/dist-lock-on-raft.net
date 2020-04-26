using Raft.Core.Handler;
using Raft.RaftEmulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class ClusterHandler : ActionHandlerBase
    {
        public ClusterHandler():base()
        {

        }
        List<ShardEmulator> Shards = new List<ShardEmulator>();
        public override bool DoAction(string entityName, ulong index, byte[] data)
        {
            Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}");
            //handle shards operation
            try
            {
                string str = System.Text.Encoding.Default.GetString(data);
                if (str.StartsWith("shards:"))
                {
                    string json = str.Substring(7);
                    if (json == this.raftNode.NodeName)
                    {
                        //start a shard
                        var shard = new ShardEmulator();
                        shard.StartEmulateTcpNodes(3);
                        this.Shards.Add(shard);
                    }

                }

                // Console.WriteLine(str+" is received");
            }
            catch (Exception ex)
            {

            }
            return true;
        }
    }
}
