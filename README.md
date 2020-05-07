# dist-lock-on-raft.net
distributed readwrite lock in.net core

this project use raft.net and dotnetty
https://github.com/hhblaze/Raft.Net

and I made the following changes:
1.use dotnetty to replace network transport 
2.use leveldb to replace storage part
3.change structure to make it easy to extend

distributed lock still in working.
