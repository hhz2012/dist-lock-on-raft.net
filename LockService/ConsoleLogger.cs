using Raft;
using System;
using System.Collections.Generic;
using System.Text;

namespace LockService
{
    public class Logger : IWarningLog
    {
        public void Log(WarningLogEntry logEntry)
        {
            Console.WriteLine(logEntry.ToString());
        }
    }
}
