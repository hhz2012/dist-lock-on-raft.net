using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raft;
using Raft.Transport;

namespace rafttest
{

    
    class Program
    {
        static IWarningLog log = null;
        static void Main(string[] args)
        {
            Test();
        }
        static async void Test()
        { 
            log = new Logger();
            var configPath = @"D:\study\Raft.Net\rafttest\config.json";
            TcpRaftNode rn1 = TcpRaftNode.GetFromConfig(System.IO.File.ReadAllText(configPath),
               @"D:\Temp\DBreeze\Node1", 4250, log,
               (entityName, index, data) => { Console.WriteLine($"Committed {entityName}/{index}"); return true; });

            TcpRaftNode rn2 = TcpRaftNode.GetFromConfig(System.IO.File.ReadAllText(configPath),
                             @"D:\Temp\DBreeze\Node2", 4251, log,
                           (entityName, index, data) => { Console.WriteLine($"Committed {entityName}/{index}"); return true; });

            TcpRaftNode rn3 = TcpRaftNode.GetFromConfig(System.IO.File.ReadAllText(configPath),
                           @"D:\Temp\DBreeze\Node3", 4252, log,
                           (entityName, index, data) => { Console.WriteLine($"Committed {entityName}/{index}"); return true; });

            rn1.Start();
            rn2.Start();
            rn3.Start();



            rn1.AddLogEntry(new byte[] { 23 });
            await rn1.AddLogEntryAsync(new byte[] { 27 }, entityName: "inMemory2");



        }
    }
    



    public class Logger : IWarningLog
    {
        public void Log(WarningLogEntry logEntry)
        {
            Console.WriteLine(logEntry.ToString());
        }
    }


    
}
