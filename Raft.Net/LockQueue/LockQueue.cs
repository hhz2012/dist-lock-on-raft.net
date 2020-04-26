using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LockQueueLib
{
    public class LockQueue
    {
        LockEntry header = new LockEntry();
        public bool LockNoWait(string sessionId, LockType type)
        {
            var item = header;
            while (item.Type==type&&type!=LockType.Write)
            {
                if (item.Next==null)
                {
                    //find tail 
                    LockEntry newEntry = new LockEntry()
                    {
                        sessionId = sessionId,
                        Type = type
                    };
                    var oldvalue=Interlocked.CompareExchange(ref item.Next,  newEntry,null);
                    if (item.Next == newEntry) return true;
                    else continue; //other body take this position
                }else
                {
                    item = item.Next;
                }
            };
            return false;
        }
        public int Length
        {
            get
            {
                var item = header.Next;
                int length = 0;
                while (item != null) 
                {
                    length++;
                    item = item.Next;
                    if (item == null) break;
                } 
                return length;
            }
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
                }else
                {
                    item = item.Next;
                }

            } while (item!= null);
            return true;
        }
        public bool RemoveFromHead()
        {
            var item = header;
            while (item.Next!=null)
            {
                var emptyNode = item.Next;
                if (emptyNode.sessionId == string.Empty) //check passed
                {
                    //start to remove this node
                    var nextnext = emptyNode.Next;
                    var updated = Interlocked.CompareExchange(ref emptyNode.Next, null, nextnext);
                    if (emptyNode.Next == null) //update success
                    {
                        var remove = Interlocked.CompareExchange(ref item.Next, nextnext, emptyNode);
                        if (item.Next == nextnext) //
                        {
                            if (nextnext == null) return true;
                            else continue;
                        }
                        else
                        {
                            //unexpected error happened
                        }
                    }
                }
                else break;
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
