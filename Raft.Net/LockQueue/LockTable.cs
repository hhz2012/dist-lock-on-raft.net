using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LockQueueLib
{
    public class LockTable
    {
        HashEntry[] table = null;
        int size = 100;
        public LockTable(int size=100)
        {
            table = new HashEntry[size];
            for (int i=0;i<size;i++)
            {
                table[i] = new HashEntry();
            }
            this.size = size;
        }

        public LockQueue GetQueue(string key)
        {
            int hashCode = key.GetHashCode();
            if (hashCode < 0) hashCode = -hashCode;
            int index = hashCode % size;
            
            HashEntry entry = table[index];
            do
            {
                if (entry.key == key) return entry.queue;
                if (entry.Next==null)
                {
                    HashEntry newEntry = new HashEntry()
                    {
                        key = key
                    };
                    var update=Interlocked.CompareExchange(ref entry.Next,newEntry,null);
                    if (entry.Next == newEntry) return newEntry.queue;
                    else continue;
                }else
                {
                    entry = entry.Next;
                }
                 
            } while (entry.Next != null);

            //should not happen
            return null;

        }
    }
    public class HashEntry
    {
        public string key { get; set; }
       
        public HashEntry Next = null;

        public LockQueue queue = new LockQueue();
    }
}
