using System;
using DBreeze;
using DBreeze.Utils;
using LockService;
using Raft;
using Raft.Core.RaftEmulator;
using Raft.Transport;

namespace NodeTest.Core
{
    class Program
    {
        static IWarningLog log = null;
      

        static Raft.RaftEmulator.ClusterManagerEmulator cluster = null;
        static byte val = 0;

        static void Main(string[] args)
        {
            var cluster = new LockClusterManager();
            cluster.StartControlNodes(5);
            Console.ReadLine();
            cluster.TestSendData("hello");

            Console.ReadLine();
        }



    }//eoc
}
