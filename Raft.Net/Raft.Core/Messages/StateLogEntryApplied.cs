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

    public class StateLogEntryApplied 
    {
        public StateLogEntryApplied()
        {
        }

        /// <summary>
        /// 
        /// </summary>        
        public ulong StateLogEntryId { get; set; }

        /// <summary>
        /// 
        /// </summary>        
        public ulong StateLogEntryTerm { get; set; }
        

     
    }
}
