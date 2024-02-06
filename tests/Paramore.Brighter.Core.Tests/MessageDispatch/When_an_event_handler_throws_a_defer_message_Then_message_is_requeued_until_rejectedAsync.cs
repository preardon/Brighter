﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Core.Tests.MessageDispatch.TestDoubles;
using Xunit;
using Paramore.Brighter.ServiceActivator;
using Paramore.Brighter.ServiceActivator.TestHelpers;

namespace Paramore.Brighter.Core.Tests.MessageDispatch
{
    public class MessagePumpEventProcessingDeferMessageActionTestsAsync
    {
        private readonly IAmAMessagePump _messagePump;
        private readonly FakeChannel _channel;
        private readonly SpyRequeueCommandProcessor _commandProcessor;
        private readonly int _requeueCount = 5;

        public MessagePumpEventProcessingDeferMessageActionTestsAsync()
        {
            _commandProcessor = new SpyRequeueCommandProcessor();
            var commandProcessorProvider = new CommandProcessorProvider(_commandProcessor);
            _channel = new FakeChannel();
            var messageMapperRegistry = new MessageMapperRegistry(
                null,
                new SimpleMessageMapperFactoryAsync(_ => new MyEventMessageMapperAsync()));
            messageMapperRegistry.RegisterAsync<MyEvent, MyEventMessageMapperAsync>();
             
            _messagePump = new MessagePumpAsync<MyEvent>(commandProcessorProvider, messageMapperRegistry, null) 
                { Channel = _channel, TimeoutInMilliseconds = 5000, RequeueCount = _requeueCount };

            var msg = new TransformPipelineBuilderAsync(messageMapperRegistry, null)
                .BuildWrapPipeline<MyEvent>()
                .WrapAsync(new MyEvent())
                .Result;
            _channel.Enqueue(msg);
        }

        [Fact]
        public async Task When_an_event_handler_throws_a_defer_message_Then_message_is_requeued_until_rejectedAsync()
        {
            var task = Task.Factory.StartNew(() => _messagePump.Run(), TaskCreationOptions.LongRunning);
            await Task.Delay(1000);

            var quitMessage = new Message(new MessageHeader(Guid.Empty, "", MessageType.MT_QUIT), new MessageBody(""));
            _channel.Enqueue(quitMessage);

            await Task.WhenAll(task);

            _channel.RequeueCount.Should().Be(_requeueCount-1);
            _channel.RejectCount.Should().Be(1);
        }
    }
}
