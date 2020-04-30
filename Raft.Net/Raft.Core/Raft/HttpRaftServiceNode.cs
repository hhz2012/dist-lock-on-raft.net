using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Raft.Core.Handler;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Raft.Core.Raft
{
    public class HttpRaftServiceNode : RaftServiceNode
    {
        int httpPort=0;
        ChannelHandlerAdapter adapter = null;

        public HttpRaftServiceNode(NodeSettings nodeSettings,
                                   string dbreezePath,
                                   IActionHandler handler,
                                   int port = 4250,
                                   string nodeName = "default",
                                   int httpPort=10000,
                                   ChannelHandlerAdapter adapter=null,
                                   IWarningLog log = null)
            :base(nodeSettings,dbreezePath,handler,port,nodeName,log)
        {
            this.httpPort = httpPort;
            this.adapter = adapter;
        }
        public async Task StartHttp()
        {
            await this.OpenServicePort(this.httpPort, this.adapter);
        }
        public async Task OpenServicePort(int httpPort, ChannelHandlerAdapter adapter)
        {
            try
            {
                var bossGroup = new MultithreadEventLoopGroup(1);
                // 工作线程组，默认为内核数*2的线程数
                var workerGroup = new MultithreadEventLoopGroup();

                var bootstrap = new ServerBootstrap();
                bootstrap
                   .Option(ChannelOption.SoBacklog, 8192)
                   .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                   {
                       IChannelPipeline pipeline = channel.Pipeline;
                       pipeline.AddLast("encoder", new HttpResponseEncoder());
                       pipeline.AddLast("decoder", new HttpRequestDecoder(4096, 8192, 8192, false));
                       pipeline.AddLast("handler", new HelloServerHandler2());
                   }));
               
                IChannel boundChannel = await bootstrap.BindAsync(port);
            }
            catch (Exception ex)
            {
                //if (log != null)
                //    log.Log(new WarningLogEntry() { Exception = ex });
            }
        }
    }
    sealed class HelloServerHandler2 : ChannelHandlerAdapter
    {
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

        static int JsonLen() => Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat()).Length;

        static MessageBody NewMessage() => new MessageBody("Hello, World!");

        public override void ChannelRead(IChannelHandlerContext ctx, object message)
        {

            if (message is IHttpRequest request)
            {
                try
                {
                    this.Process(ctx, request);
                    // new Task(() => Process(ctx, request), TaskCreationOptions.HideScheduler).RunSynchronously();
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
        //void Process(IChannelHandlerContext ctx, IHttpRequest request)
        //{
        //    new Task(() => Process0(ctx, request), TaskCreationOptions.HideScheduler).RunSynchronously();
        //}
        async void Process(IChannelHandlerContext ctx, IHttpRequest request)
        {

            string uri = request.Uri;
            switch (uri)
            {
                case "/plaintext":
                    //var ack = Task.Run(async () => {
                    //    await Task.Delay(10);
                    //    return 1;
                    //    }).GetAwaiter().GetResult();
                    //await Task.Delay(10).ConfigureAwait(false);
                    this.WriteResponse(ctx, PlaintextContentBuffer.Duplicate(), TypePlain, PlaintextClheaderValue);
                    break;
                case "/json":
                    byte[] json = Encoding.UTF8.GetBytes(NewMessage().ToJsonFormat());
                    this.WriteResponse(ctx, Unpooled.WrappedBuffer(json), TypeJson, JsonClheaderValue);
                    break;
                default:
                    var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.NotFound, Unpooled.Empty, false);
                    ctx.WriteAndFlushAsync(response);
                    ctx.CloseAsync();
                    break;
            }
        }

        void WriteResponse(IChannelHandlerContext ctx, IByteBuffer buf, ICharSequence contentType, ICharSequence contentLength)
        {
            // Build the response object.
            var response = new DefaultFullHttpResponse(HttpVersion.Http11, HttpResponseStatus.OK, buf, false);
            HttpHeaders headers = response.Headers;
            headers.Set(ContentTypeEntity, contentType);
            headers.Set(ServerEntity, ServerName);
            headers.Set(DateEntity, this.date);
            headers.Set(ContentLengthEntity, contentLength);

            // Close the non-keep-alive connection after the write operation is done.
            ctx.WriteAsync(response);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception) => context.CloseAsync();

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();
    }
    sealed class MessageBody
    {
        public MessageBody(string message)
        {
            this.Message = message;
        }

        public string Message { get; }

        public string ToJsonFormat() => "{" + $"\"{nameof(MessageBody)}\" :" + "{" + $"\"{nameof(this.Message)}\"" + " :\"" + this.Message + "\"}" + "}";
    }
}
