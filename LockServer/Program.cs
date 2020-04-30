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


            //cluster.OpenRpc();
            Console.WriteLine("open port on 9090");
            Console.ReadLine();


        }
    }
}
