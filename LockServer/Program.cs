using LockService;
using System;
using System.Threading.Tasks;

namespace LockServer
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("start lock server");
            var cluster = new LockClusterManager();
            cluster.StartControlNodes(3).ConfigureAwait(false).GetAwaiter().GetResult();

            //wait for system to ready 
            while(!cluster.isReady())
            {
                Task.Delay(2000);
            }
            Console.WriteLine("node boot ok, start lock service");

            cluster.BootWorkerNode();

            while (!cluster.isWorkerReady())
            {
                Task.Delay(2000);
            }
            Console.WriteLine("worker node boot ok, start lock service");
            //open port and listen to request 

            Console.ReadLine();


        }
    }
}
