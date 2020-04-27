using LockService;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class WorkerHandler : ActionHandlerBase
    {
        LockSeriveControlNode node = null;
        public WorkerHandler(LockSeriveControlNode node) : base()
        {
            this.node = node;
        }
        public override bool DoAction(string entityName, ulong index, byte[] data)
        {
           // Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}");
            //handle shards operation
            try
            {
                string str = System.Text.Encoding.Default.GetString(data);
                string json = str;
                LockOper command = Newtonsoft.Json.JsonConvert.DeserializeObject<LockOper>(json);
                //Console.WriteLine($"lock oper received:{command.Oper},{command.Key}");

                var result=this.node.DoWork(command).ConfigureAwait(false).GetAwaiter().GetResult();
                //Console.WriteLine(" lock result:" + result);
                //if (command.Command == "CreateShard")
                //{
                //    this.node.JoinShard(command);
                //}
            }
            catch (Exception ex)
            {

            }
            return true;
        }
    }
}
