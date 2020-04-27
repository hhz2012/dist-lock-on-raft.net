using LockQueueLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FASTER.core;

namespace Lock.UnitTest
{
    [TestClass]
    public class FasterTest
    {
        [TestMethod]
        public void TestReadWriteSeq()
        {
            var log = Devices.CreateLogDevice("C:\\Temp\\hlog.log");
            var fht = new FasterKV<long, long, long, long, Empty, Funcs>
              (1L << 20, new Funcs(), new LogSettings { LogDevice = log });
            var session = fht.NewSession();
            long key = 1, value = 1, input = 10, output = 0;
            session.Upsert(ref key, ref value, Empty.Default, 0);
            session.Dispose();
            fht.Dispose();
            log.Close();
        }

    }
    public class Funcs : IFunctions<long, long, long, long, Empty>
    {
        public void SingleReader(ref long key, ref long input, ref long value, ref long dst) => dst = value;
        public void SingleWriter(ref long key, ref long src, ref long dst) => dst = src;
        public void ConcurrentReader(ref long key, ref long input, ref long value, ref long dst) => dst = value;
        public bool ConcurrentWriter(ref long key, ref long src, ref long dst) => true;
        public void InitialUpdater(ref long key, ref long input, ref long value) => value = input;
        public void CopyUpdater(ref long key, ref long input, ref long oldv, ref long newv) => newv = oldv + input;
        public bool InPlaceUpdater(ref long key, ref long input, ref long value) =>   true;
        public void UpsertCompletionCallback(ref long key, ref long value, Empty ctx) { }
        public void ReadCompletionCallback(ref long key, ref long input, ref long output, Empty ctx, Status s) { }
        public void RMWCompletionCallback(ref long key, ref long input, Empty ctx, Status s) { }
        public void CheckpointCompletionCallback(string sessionId, CommitPoint commitPoint) { }

        public void DeleteCompletionCallback(ref long key, Empty ctx) { }
       
    }
}
