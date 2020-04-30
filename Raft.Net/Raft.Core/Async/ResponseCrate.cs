/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Raft
{
    internal class ResponseCrate
    {
        /// <summary>
        /// Not SLIM version must be used (it works faster for longer delay which RPCs are)
        /// </summary>
        public byte[] res = null;
        public Action<Tuple<bool, byte[]>> callBack = null;
        public bool IsRespOk = false;

        public ManualResetEventSlim amre = null;

        public DateTime created = DateTime.UtcNow;
        public int TimeoutsMs = 30000;

    
        /// <summary>
        /// Works faster with timer than WaitOneAsync
        /// </summary>
        public void Init_AMRE()
        {
            amre = new ManualResetEventSlim();
        }

        public void Set_MRE()
        {
            try
            {
                if (amre != null)
                {
                    amre.Set();
                }
            }
            catch (Exception ex)
            {

            }

        }
        long IsDisposed = 0;
        public void Dispose_MRE()
        {
            if (System.Threading.Interlocked.CompareExchange(ref IsDisposed, 1, 0) != 0)
                return;
            if (amre != null)
            {
                amre.Set();
                amre = null;
            }
        }

    }
}
