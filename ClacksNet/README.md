# Clacks.NET

--- 
Clacks.NET is a .net outbox library. I'd intended this to include an inbox solution as well, but
did not have a use-case for it, so figured I'd just release the outbox part. If
you want to contribute an inbox solution, please do so.

Additionally, this is intended to support multiple databases; however, I am using it
with postgres, so that's the only one I have tested/fleshed out. Again, if you would
like to add this support, I would be happy to accept a PR.

This library does not support a dead letter queue. It will retry messages infinitely, though it will 
retry other messages first so the system doesn't get stuck retrying the same failed message.
I intend to add a dead letter queue in the future (but would welcome if someone else wants to do this).

---
## Using Clacks.NET

### Install the NuGet package(s)
The main package is `SlySoft.Clacks.NET`, which contains the core functionality. If you are using Postgres, you can
also install the `SlySoft.Clacks.NET.Postgres` package, which contains the Postgres-specific functionality.
```bash 
dotnet add package SlySoft.ClacksNet
dotnet add package SlySoft.ClacksNet.Postgres
```

### Configure the Outbox via dependency injection
Call `AddClacksOut` in your `Startup.cs` or `Program.cs` file to register the outbox service. This method allows the
setup of some configuration values, and you MUST configure a lambda expression to create a connection. In the example
below, IDbConnection is registered in the DI container, and the lambda expression returns it. You may resolve the
connection however you like, but it must be configured for the outbox to work.

Note: the connection must have the permission to run DDL commands, as the outbox will create the table if it does not exist.

```csharp
using SlySoft.ClacksNet;

...

services.AddClacksOut(x => 
{
    x.GetConnection = services => services.GetRequiredService<IDbConnection>();
});
```

### Sending messages to the outbox

This will allow you to write to the outbox by injecting `IClacksOut` into your classes. You can call the `Send` method
with a topic and a message. These values will be available when the outbox is processed later on. Note that there is
both a synchronous and asynchronous version of the `Send` method.

```csharp
using SlySoft.ClacksNet;

public class MyService(IClacksOut clacksOut)
{
    public async Task DoSomething()
    {
        var subject = "messages.to-the-world";
        var message = "Hello, World!"
        await clacksOut.SendAsync(subject, message);
    }
}

```

### Processing the outbox

To process the outbox, you must provide an implementation of the `IClacksOutSender` interface. This interface contains
one method, `SendAsync`, which is called when the outbox is processed (by default, polled once per minute, though this
value is configurable). You must return true if the message was sent successfully. Note that `message` contains a unique
ID, which you should use to perform idempotency checks.

```csharp
using SlySoft.ClacksNet;

internal sealed class OutboxSender : IClacksOutSender {
    public Task<bool> SendMessage(ClacksOutMessage message, CancellationToken cancellationToken = default) {
        Console.WriteLine($"Sending message: {message.Topic} - {message.Message}");
        return Task.FromResult(true);
    }
}
```

You do not need to register this class with the DI container, but you DO need to provide it as a configuration value
when calling `AddClacksOut`. See the following example:

```csharp
using SlySoft.ClacksNet;

...

services.AddClacksOut(x => 
{
    x.GetConnection = services => services.GetRequiredService<IDbConnection>();
    x.Sender = typeof(OutboxSender);
    x.PollingInterval = TimeSpan.FromSeconds(10);
});
```

Additionally, if you are using Postgres, you can add a reference to the `SlySoft.ClacksNet.Postgres` package, and add
the `AddPostgresClacksOutListener` method to your DI container. This will create a listener for the outbox table that
triggers on inserts. This will augment the polling and allow for more immediate processing of messages.

```csharp
services.AddClacksOut(x => 
{
    x.GetConnection = services => services.GetRequiredService<IDbConnection>();
    x.Sender = typeof(OutboxSender);
    x.PollingInterval = TimeSpan.FromSeconds(10);
})
.AddPostgresClacksOutListener();
```
