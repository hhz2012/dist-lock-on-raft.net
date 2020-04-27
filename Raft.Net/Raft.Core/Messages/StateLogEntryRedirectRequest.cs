/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze.Utils;

namespace Raft
{   
    public class StateLogEntryRedirectRequest 
    {
        public StateLogEntryRedirectRequest()
        {

        }

        /// <summary>
        /// 
        /// </summary>   
        public byte[] Data { get; set; }

        /// <summary>
        /// Used for determining cancellation of AppendLogEntryAsync by the non-leader node
        /// </summary>
        public byte[] ExternalID { get; set; }


       
    }
}
