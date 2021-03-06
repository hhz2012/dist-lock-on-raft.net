﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LockServer
{
    using System.Text;
    using DotNetty.Buffers;
    using DotNetty.Codecs.Http;
    using DotNetty.Common.Utilities;
    using DotNetty.Transport.Channels;
    using System;
    using DotNetty.Common;
    using LockService;
    using System.Threading.Tasks;
    using Raft.Core.Business;
    using Raft.Transport;

    public  class HelloServerHandler : ServiceChannelAdapter
    {
        public HelloServerHandler()
        {
            
        }
        override public void SetNode(RaftServiceNode node)
        {
            this.node = node;
        }
        RaftServiceNode node = null;
        static readonly ThreadLocalCache Cache = new ThreadLocalCache();

        sealed class ThreadLocalCache : FastThreadLocal<AsciiString>
        {
            protected override AsciiString GetInitialValue()
            {
                DateTime dateTime = DateTime.UtcNow;
                return AsciiString.Cached($"{dateTime.DayOfWeek}, {dateTime:dd MMM yyyy HH:mm:ss z}");
            }
        }

        static readonly byte[] StaticPlaintext = Encoding.UTF8.GetBytes("Hello, World!");
        static readonly int StaticPlaintextLen = StaticPlaintext.Length;
        static readonly IByteBuffer PlaintextContentBuffer = Unpooled.UnreleasableBuffer(Unpooled.DirectBuffer().WriteBytes(StaticPlaintext));
        static readonly AsciiString PlaintextClheaderValue = AsciiString.Cached($"{StaticPlaintextLen}");
        static readonly AsciiString JsonClheaderValue = AsciiString.Cached($"{JsonLen()}");

        static readonly AsciiString TypePlain = AsciiString.Cached("text/plain");
        static readonly AsciiString TypeJson = AsciiString.Cached("application/json");
        static readonly AsciiString ServerName = AsciiString.Cached("Netty");
        static readonly AsciiString ContentTypeEntity = HttpHeaderNames.ContentType;
        static readonly AsciiString DateEntity = HttpHeaderNames.Date;
        static readonly AsciiString ContentLengthEntity = HttpHeaderNames.ContentLength;
        static readonly AsciiString ServerEntity = HttpHeaderNames.Server;

        volatile ICharSequence date = Cache.Value;
        public override bool IsSharable => true;

        static int JsonLen() => Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat()).Length;

        static MessageBody NewMessage() => new MessageBody("Hello, World!");

        static MessageBody NewMessage2() => new MessageBody("Hello, World!");

        public override async void ChannelRead(IChannelHandlerContext ctx, object message)
        {
            if (message is IHttpRequest request)
            {
                try
                {
                    await this.Process(ctx, request);
                }
                catch (Exception ex)
                {

                }
                finally
                {
                    ReferenceCountUtil.Release(message);
                }
            }
            else
            {
                ctx.FireChannelRead(message);
            }
        }
        public async Task<object> BeginRequest(LockOper command)
        {
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
            var node = this.node;
            //Console.WriteLine("start lock oper" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            var result = await Task.Run(async () =>
            {
                var result = await this.node.AddLogEntryRequestAsync(System.Text.Encoding.UTF8.GetBytes(data)).ConfigureAwait(false);
                return result;
            }
            );
            // Console.WriteLine("await finished" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            return result;
        }
        async Task Process(IChannelHandlerContext ctx, IHttpRequest request)
        {
            string uri = request.Uri;
            switch (uri)
            {
                case "/lock":
                    LockOper op2 = new LockOper()
                    {
                        Key = "key",
                        Oper = "lock",
                        Session = "session1"
                    };
                    var ack1 = Task.Run(async () =>
                    {
                        return await this.BeginRequest(op2);
                    }).GetAwaiter().GetResult();

                    //byte[] json2 = Encoding.UTF8.GetBytes(NewMessage2().ToJsonFormat());
                    //this.WriteResponse(ctx, Unpooled.WrappedBuffer(json2), TypeJson, JsonClheaderValue);
                    string str= "null";
                    if (ack1 != null)
                    {
                         str = Newtonsoft.Json.JsonConvert.SerializeObject(ack1);

                    }
                    byte[] json2 = Encoding.UTF8.GetBytes(new MessageBody(str).ToJsonFormat());
                    var length = json2.Length;
                    this.WriteResponse(ctx, Unpooled.WrappedBuffer(json2), TypeJson, AsciiString.Cached($"{length}"));
                    break;
                case "/plaintext":
                    LockOper op = new LockOper()
                    {
                        Key = "key",
                        Oper = "lock",
                        Session = "session1"
                    };
                    var ack = Task.Run(async () =>
                    {
                        return await this.BeginRequest(op);
                    }).GetAwaiter().GetResult();

                    this.WriteResponse(ctx, PlaintextContentBuffer.Duplicate(), TypePlain, PlaintextClheaderValue);
                    break;
                case "/json":
                    byte[] json = Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat());
                    this.WriteResponse(ctx, Unpooled.WrappedBuffer(json), TypeJson, JsonClheaderValue);
                    break;
                default:
                    var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.NotFound, Unpooled.Empty, false);
                     await ctx.WriteAndFlushAsync(response);
                     await ctx.CloseAsync();
                    break;
            }
        }

        async Task WriteResponse(IChannelHandlerContext ctx, IByteBuffer buf, ICharSequence contentType, ICharSequence contentLength)
        {
            // Build the response object.
            var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.OK, buf, false);
            HttpHeaders headers = response.Headers;
            headers.Set(ContentTypeEntity, contentType);
            headers.Set(ServerEntity, ServerName);
            headers.Set(DateEntity, this.date);
            headers.Set(ContentLengthEntity, contentLength);

            // Close the non-keep-alive connection after the write operation is done.
            await ctx.WriteAsync(response);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception) => context.CloseAsync();

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();
    }
}
