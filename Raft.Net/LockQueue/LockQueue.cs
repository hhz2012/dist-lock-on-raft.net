using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LockQueue
{
    public class LockQueue
    {
        LockEntry header = new LockEntry();
        public bool LockNoWait(string sessionId, LockType type)
        {
            var item = header;
            while (header.Type==type&&type!=LockType.Write)
            {
                if (header.Next==null)
                {
                    //find tail 
                    LockEntry newEntry = new LockEntry()
                    {
                        sessionId = sessionId,
                        Type = type
                    };
                    var update=Interlocked.CompareExchange(ref header.Next, null, newEntry);
                    if (update == newEntry) return true;
                    else continue; //other body take this position
                }
            };
            return false;
        }
    }
    public class LockEntry
    {
        public string sessionId { get; set; }
        public LockType Type { get; set; }
        public LockEntry Next  = null;
    }
    public enum LockType
    {
        Read=0,
        Write=1
    }
}
