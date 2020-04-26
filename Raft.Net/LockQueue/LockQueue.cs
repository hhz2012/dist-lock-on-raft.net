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
        public bool Unlock(string sessionId)
        {
            var item = header;
            do
            {
                if (item.sessionId==sessionId)
                {
                    item.sessionId = string.Empty;
                    if (item==header.Next) //release from header,delete nodes
                    {
                        RemoveFromHead();
                    }
                }

            } while (item.Next != null);
            return true;
        }
        public bool RemoveFromHead()
        {
            var item = header;
            while (item.Next!=null)
            {
                var emptyNode = item.Next;
                if (emptyNode.sessionId==string.Empty) //check passed
                {
                    //start to remove this node
                    var nextnext = emptyNode.Next;
                    var updated=Interlocked.CompareExchange(ref emptyNode.Next, nextnext, null);
                    if (updated==null) //update success
                    {
                        var remove=Interlocked.CompareExchange(ref item.Next, emptyNode, nextnext);
                        if (remove==nextnext) //
                        {
                            if (nextnext == null) return true;
                            else continue;
                        }else
                        {
                            //unexpected error happened
                        }
                    }
                }
            }
            return true;
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
