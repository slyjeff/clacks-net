# Clacks.NET Postgres

--- 
Clacks.NET postgres augments the Clacks.NET library by adding handling outbox messages on a trigger in addition to
polling.

---
## Using Clacks.NET

### Install the NuGet package
```bash 
dotnet add package SlySoft.ClackNet.Postgres
```

### Configure the Outbox via dependency injection
Add a call to `AddPostgresClacksOutListener` in your `Startup.cs` or `Program.cs` file to register the outbox service.

```csharp
services.AddClacksOut(x => 
{
    x.GetConnection = services => services.GetRequiredService<IDbConnection>();
    x.Sender = typeof(OutboxSender);
    x.PollingInterval = TimeSpan.FromSeconds(10);
})
.AddPostgresClacksOutListener();
```
