using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze.Utils;

namespace Raft
{    
    public class StateLogEntryRedirectResponse
    {
        public enum eResponseType
        {
            NOT_A_LEADER,
            CACHED,
            COMMITED,
            ERROR
        }

        public StateLogEntryRedirectResponse()
        {

        }

        /// <summary>
        /// 
        /// </summary>        
        public eResponseType ResponseType { get; set; } = eResponseType.CACHED;
        
      

    }
}
