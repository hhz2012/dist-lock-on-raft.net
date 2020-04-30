using System;
using System.Collections.Generic;
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

        static byte val = 0;

        static void Main(string[] args)
        {
            var cluster = new LockClusterManager();
            cluster.StartControlNodes(3).ConfigureAwait(false).GetAwaiter().GetResult();

           // while (true)
            {
                Console.ReadLine();
            }


          

            while (true)
            {
                Console.WriteLine("before lock operation,press enter");
                Console.ReadLine();

                LockOper op = new LockOper()
                {
                    Key = "key",
                    Oper = "lock",
                    Session = "session1"
                };
                cluster.TestWorkOperation(op);

            }
            Console.ReadLine();
        }



    }//eoc
}
