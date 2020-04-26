using LockQueueLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lock.UnitTest
{
    [TestClass]
    public class QueueTest
    {
        [TestMethod]
        public void TestReadWriteSeq()
        {
            LockQueue queue = new LockQueue();
            queue.LockNoWait("lock1", LockType.Read);
            Assert.AreEqual(queue.Length, 1);

            queue.LockNoWait("lock2", LockType.Read);
            Assert.AreEqual(queue.Length, 2);

            queue.LockNoWait("lock3", LockType.Write);
            Assert.AreEqual(queue.Length, 2);
        }
        [TestMethod]
        public void TestWriteRead()
        {
            LockQueue queue = new LockQueue();
            queue.LockNoWait("lock1", LockType.Write);
            Assert.AreEqual(queue.Length, 1);
            queue.LockNoWait("lock2", LockType.Read);
            Assert.AreEqual(queue.Length, 1);
        }
        [TestMethod]
        public void TestAddAndRemove()
        {
            LockQueue queue = new LockQueue();
            queue.LockNoWait("lock1", LockType.Read);
            Assert.AreEqual(queue.Length, 1);

            queue.LockNoWait("lock2", LockType.Read);
            Assert.AreEqual(queue.Length, 2);

            queue.Unlock("lock2");
            Assert.AreEqual(queue.Length, 2);

            queue.Unlock("lock1");
            Assert.AreEqual(queue.Length, 0);
        }
        [TestMethod]
        public void TableTest()
        {
            LockTable table = new LockTable(10);
            table.GetQueue("queue1");
            table.GetQueue("queue2");
            table.GetQueue("queue3");
            Assert.IsTrue(true);
        }
    }
}
