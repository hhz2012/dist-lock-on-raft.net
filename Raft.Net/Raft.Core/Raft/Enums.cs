using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Raft
{
    public enum eNodeState
    {
        Leader,
        Follower,
        Candidate
    }

    enum eTermComparationResult
    {
        CurrentTermIsSmaller,
        TermsAreEqual,
        CurrentTermIsHigher
    }
}
