﻿using System;
using System.Text.Json;
using Paramore.Brighter.Extensions;

namespace Paramore.Brighter.Core.Tests.MessageSerialisation.Test_Doubles;

public class MyVanillaCommandMessageMapper : IAmAMessageMapper<MyTransformableCommand>
{
    public IRequestContext Context { get; set; }

    public Message MapToMessage(MyTransformableCommand request, Publication publication)
    {
        return new Message(
            new MessageHeader(request.Id, publication.Topic, request.RequestToMessageType(), timeStamp: DateTime.UtcNow),
            new MessageBody(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.General)))
            );
    }

    public MyTransformableCommand MapToRequest(Message message)
    {
        return JsonSerializer.Deserialize<MyTransformableCommand>(message.Body.Value);
    }
}
