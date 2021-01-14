// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using HttpMethod = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class Http1ConnectionBenchmark
    {
        private const int InnerLoopCount = 512;

        private readonly HttpParser<Adapter> _parser = new HttpParser<Adapter>();

        private ReadOnlySequence<byte> _buffer;

        internal Http1Connection Connection { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var memoryPool = SlabMemoryPoolFactory.Create();
            var options = new PipeOptions(memoryPool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);

            var serviceContext = TestContextFactory.CreateServiceContext(
                serverOptions: new KestrelServerOptions(),
                httpParser: new HttpParser<Http1ParsingHandler>());

            var connectionContext = TestContextFactory.CreateHttpConnectionContext(
                serviceContext: serviceContext,
                connectionContext: null,
                transport: pair.Transport,
                timeoutControl: new TimeoutControl(timeoutHandler: null),
                memoryPool: memoryPool,
                connectionFeatures: new FeatureCollection());

            var http1Connection = new Http1Connection(connectionContext);

            http1Connection.Reset();

            Connection = http1Connection;
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void PlaintextTechEmpower()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void LiveAspNet()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.LiveaspnetRequest);
                ParseData();
            }
        }

        private void InsertData(byte[] data)
        {
            _buffer = new ReadOnlySequence<byte>(data);
        }

        private void ParseData()
        {
            var reader = new SequenceReader<byte>(_buffer);
            if (!_parser.ParseRequestLine(new Adapter(this), ref reader))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }

            if (!_parser.ParseHeaders(new Adapter(this), ref reader))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }

            Connection.EnsureHostHeaderExists();

            Connection.Reset();
        }

        private struct Adapter : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            public Http1ConnectionBenchmark RequestHandler;

            public Adapter(Http1ConnectionBenchmark requestHandler)
            {
                RequestHandler = requestHandler;
            }

            public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
                => RequestHandler.Connection.OnHeader(name, value);

            public void OnHeadersComplete(bool endStream)
                => RequestHandler.Connection.OnHeadersComplete();

            public void OnStartLine(HttpVersionAndMethod versionAndMethod, TargetOffsetPathLength targetPath, Span<byte> startLine)
                => RequestHandler.Connection.OnStartLine(versionAndMethod, targetPath, startLine);

            public void OnStaticIndexedHeader(int index)
            {
                throw new NotImplementedException();
            }

            public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
            {
                throw new NotImplementedException();
            }
        }
    }
}
