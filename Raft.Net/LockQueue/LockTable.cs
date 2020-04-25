using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace LockQueue
{
    public class LockTable
    {
        public ConcurrentDictionary<string, LockQueue> Queues = new ConcurrentDictionary<string, LockQueue>();

        public LockQueue GetQueue(string key)
        {
            if (!Queues.ContainsKey(key))
            {
                var queue = new LockQueue();
                bool success=Queues.TryAdd(key, queue);
                if (success) return queue;
                else return null;
            }else
            {
                return Queues[key];
            }
        }
    }
}
