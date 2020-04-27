using LockQueueLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FASTER.core;
using LiteDB;

namespace Lock.UnitTest
{
    [TestClass]
    public class litedbtest
    {
        [TestMethod]
        public void TestReadWriteSeq()
        {
            using (var db = new LiteDatabase(@"C:\Temp\MyData.db"))
            {
                // Get a collection (or create, if doesn't exist)
                var col = db.GetCollection<Customer>("customers");

                // Create your new customer instance
                var customer = new Customer
                {
                    Name = "John Doe",
                    Phones = new string[] { "8000-0000", "9000-0000" },
                    IsActive = true
                };
                // Insert new customer document (Id will be auto-incremented)
                col.Insert(customer);
            }
        }

    }
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string[] Phones { get; set; }
        public bool IsActive { get; set; }
    }
}
