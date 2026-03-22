# Logger Usage Comparison

> Generated: 2026-03-22 21:43

Code examples and real output for each logger across the benchmark scenarios.
Both text and JSON output shown where applicable.

---

## No Fields

Message only, no structured fields attached.

### Clip

```csharp
clip.Info("Request handled");

clipZero.Info("Request handled");
```

```
2026-03-22 20:43:51.898 INFO Request handled
```

```json
{
  "ts": "2026-03-22T20:43:51.900Z",
  "level": "info",
  "msg": "Request handled"
}
```

### Serilog

```csharp
serilog.Information("Request handled");
```

```
2026-03-22 21:43:51.906 INFO Request handled {}
```

```json
{
  "Timestamp": "2026-03-22T21:43:51.9172370+01:00",
  "Level": "Information",
  "MessageTemplate": "Request handled"
}
```

### NLog

```csharp
nlog.Info("Request handled");
```

```
2026-03-22 21:43:51.935 INFO Request handled
```

```json
{
  "ts": "2026-03-22 21:43:51.9382",
  "level": "Info",
  "msg": "Request handled"
}
```

### MEL

```csharp
mel.LogInformation("Request handled");
```

```
2026-03-22 21:43:51.958 info: Demo[0]
      Request handled
```

```json
{
  "Timestamp": "2026-03-22T21:43:51.960Z",
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "Demo",
  "Message": "Request handled",
  "State": {
    "{OriginalFormat}": "Request handled"
  },
  "Scopes": []
}
```

### ZLogger

```csharp
zlogger.ZLogInformation($"Request handled");
```

```
2026-03-22 21:43:51.971 INF Request handled
```

```json
{
  "Timestamp": "2026-03-22T21:43:51.973404+01:00",
  "LogLevel": "Information",
  "Category": "comparison",
  "Message": "Request handled"
}
```

### log4net

```csharp
log4net.Info("Request handled");
```

```
2026-03-22 21:43:51.975 INFO  Request handled
```

```json
{
  "ts": "2026-03-22 20:43:51,976",
  "level": "INFO",
  "msg": "Request handled"
}
```

### ZeroLog

```csharp
zerolog.Info("Request handled");
```

```
2026-03-22 20:43:51.988 INFO  Request handled
```

---

## Five Fields

Message with five structured fields: string, int, double, Guid, and decimal.

### Clip

```csharp
clip.Info("Request handled",
    new { Method, Status, Elapsed, RequestId = ReqId, Amount });

clipZero.Info("Request handled",
    new Field("Method", Method),
    new Field("Status", Status),
    new Field("Elapsed", Elapsed),
    new Field("RequestId", ReqId),
    new Field("Amount", Amount));
```

```
2026-03-22 20:43:51.994 INFO Request handled                           Amount=49.95 Elapsed=1.234 Method=GET RequestId=550e8400-e29b-41d4-a716-446655440000 Status=200
```

```json
{
  "ts": "2026-03-22T20:43:51.998Z",
  "level": "info",
  "msg": "Request handled",
  "fields": {
    "Method": "GET",
    "Status": 200,
    "Elapsed": 1.234,
    "RequestId": "550e8400-e29b-41d4-a716-446655440000",
    "Amount": 49.95
  }
}
```

### Serilog

```csharp
serilog.Information("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);
```

```
2026-03-22 21:43:51.999 INFO Request handled GET 200 1.234 "550e8400-e29b-41d4-a716-446655440000" 49.95 {}
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.0000300+01:00",
  "Level": "Information",
  "MessageTemplate": "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}",
  "Properties": {
    "Method": "GET",
    "Status": 200,
    "Elapsed": 1.234,
    "RequestId": "550e8400-e29b-41d4-a716-446655440000",
    "Amount": 49.95
  }
}
```

### NLog

```csharp
nlog.Info("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);
```

```
2026-03-22 21:43:52.001 INFO Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95 Method=GET Status=200 Elapsed=1.234 RequestId=550e8400-e29b-41d4-a716-446655440000 Amount=49.95
```

```json
{
  "ts": "2026-03-22 21:43:52.0017",
  "level": "Info",
  "msg": "Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95",
  "Method": "GET",
  "Status": 200,
  "Elapsed": 1.234,
  "RequestId": "550e8400-e29b-41d4-a716-446655440000",
  "Amount": 49.95
}
```

### MEL

```csharp
mel.LogInformation("Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}", Method, Status, Elapsed, ReqId, Amount);
```

```
2026-03-22 21:43:52.004 info: Demo[0]
      Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.005Z",
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "Demo",
  "Message": "Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95",
  "State": {
    "Method": "GET",
    "Status": 200,
    "Elapsed": 1.234,
    "RequestId": "550e8400-e29b-41d4-a716-446655440000",
    "Amount": 49.95,
    "{OriginalFormat}": "Request handled {Method} {Status} {Elapsed} {RequestId} {Amount}"
  },
  "Scopes": []
}
```

### ZLogger

```csharp
zlogger.ZLogInformation($"Request handled {Method} {Status} {Elapsed} {ReqId} {Amount}");
```

```
2026-03-22 21:43:52.006 INF Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.00733+01:00",
  "LogLevel": "Information",
  "Category": "comparison",
  "Message": "Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95",
  "Method": "GET",
  "Status": 200,
  "Elapsed": 1.234,
  "ReqId": "550e8400-e29b-41d4-a716-446655440000",
  "Amount": 49.95
}
```

### log4net

```csharp
log4net.InfoFormat("Request handled {0} {1} {2} {3} {4}", Method, Status, Elapsed, ReqId, Amount);
```

```
2026-03-22 21:43:52.008 INFO  Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95
```

```json
{
  "ts": "2026-03-22 20:43:52,008",
  "level": "INFO",
  "msg": "Request handled GET 200 1.234 550e8400-e29b-41d4-a716-446655440000 49.95"
}
```

### ZeroLog

```csharp
zerolog.Info()
    .Append("Request handled")
    .AppendKeyValue("Method", Method)
    .AppendKeyValue("Status", Status)
    .AppendKeyValue("Elapsed", Elapsed)
    .AppendKeyValue("RequestId", ReqId)
    .AppendKeyValue("Amount", Amount)
    .Log();
```

```
2026-03-22 20:43:52.008 INFO  Request handled ~~ { "Method": "GET", "Status": 200, "Elapsed": 1.234, "RequestId": "550e8400-e29b-41d4-a716-446655440000", "Amount": 49.95 }
```

---

## With Context

Message inside a logging scope that adds two context fields, plus one call-site field.

### Clip

```csharp
using (clip.AddContext(new { RequestId = "abc-123", UserId = 42 }))
    clip.Info("Processing", new { Step = "auth" });

using (clipZero.AddContext(
    new Field("RequestId", "abc-123"),
    new Field("UserId", 42)))
    clipZero.Info("Processing", new Field("Step", "auth"));
```

```
2026-03-22 20:43:52.010 INFO Processing                                RequestId=abc-123 Step=auth UserId=42
```

```json
{
  "ts": "2026-03-22T20:43:52.010Z",
  "level": "info",
  "msg": "Processing",
  "fields": {
    "RequestId": "abc-123",
    "UserId": 42,
    "Step": "auth"
  }
}
```

### Serilog

```csharp
using (LogContext.PushProperty("RequestId", "abc-123"))
using (LogContext.PushProperty("UserId", 42))
    serilog.Information("Processing {Step}", "auth");
```

```
2026-03-22 21:43:52.010 INFO Processing auth {"UserId":42,"RequestId":"abc-123"}
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.0109970+01:00",
  "Level": "Information",
  "MessageTemplate": "Processing {Step}",
  "Properties": {
    "Step": "auth",
    "UserId": 42,
    "RequestId": "abc-123"
  }
}
```

### NLog

```csharp
using (ScopeContext.PushProperty("RequestId", "abc-123"))
using (ScopeContext.PushProperty("UserId", 42))
    nlog.Info("Processing {Step}", "auth");
```

```
2026-03-22 21:43:52.011 INFO Processing auth Step=auth RequestId=abc-123 UserId=42
```

```json
{
  "ts": "2026-03-22 21:43:52.0122",
  "level": "Info",
  "msg": "Processing auth",
  "RequestId": "abc-123",
  "UserId": 42,
  "Step": "auth"
}
```

### MEL

```csharp
using (mel.BeginScope(new Dictionary<string, object?>
    { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
    mel.LogInformation("Processing {Step}", "auth");
```

```
2026-03-22 21:43:52.012 info: Demo[0]
      => System.Collections.Generic.Dictionary`2[System.String,System.Object]
      Processing auth
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.013Z",
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "Demo",
  "Message": "Processing auth",
  "State": {
    "Step": "auth",
    "{OriginalFormat}": "Processing {Step}"
  },
  "Scopes": [
    {
      "Message": "System.Collections.Generic.Dictionary`2[System.String,System.Object]",
      "RequestId": "abc-123",
      "UserId": 42
    }
  ]
}
```

### ZLogger

```csharp
using (zlogger.BeginScope(new Dictionary<string, object?>
    { ["RequestId"] = "abc-123", ["UserId"] = 42 }))
    zlogger.ZLogInformation($"Processing {Step}");
```

```
2026-03-22 21:43:52.013 INF Processing auth
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.014015+01:00",
  "LogLevel": "Information",
  "Category": "comparison",
  "Message": "Processing auth",
  "RequestId": "abc-123",
  "UserId": 42,
  "Step": "auth"
}
```

---

## With Exception

Message with an attached exception including a full stack trace.

### Clip

```csharp
clip.Error("Connection failed", exception,
    new { Host = "db.local", Port = 5432 });

clipZero.Error("Connection failed", exception,
    new Field("Host", "db.local"),
    new Field("Port", 5432));
```

```
2026-03-22 20:43:52.027 ERRO Connection failed                         Host=db.local Port=5432
  System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "ts": "2026-03-22T20:43:52.030Z",
  "level": "error",
  "msg": "Connection failed",
  "fields": {
    "Host": "db.local",
    "Port": 5432
  },
  "error": {
    "type": "System.InvalidOperationException",
    "msg": "simulated database error",
    "stack": "   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36"
  }
}
```

### Serilog

```csharp
serilog.Error(exception, "Connection failed {Host} {Port}", "db.local", 5432);
```

```
2026-03-22 21:43:52.030 EROR Connection failed db.local 5432 {}
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.0308420+01:00",
  "Level": "Error",
  "MessageTemplate": "Connection failed {Host} {Port}",
  "Exception": "System.InvalidOperationException: simulated database error\n   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36",
  "Properties": {
    "Host": "db.local",
    "Port": 5432
  }
}
```

### NLog

```csharp
nlog.Error(exception, "Connection failed {Host} {Port}", "db.local", 5432);
```

```
2026-03-22 21:43:52.030 ERRO Connection failed db.local 5432 Host=db.local Port=5432
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "ts": "2026-03-22 21:43:52.0309",
  "level": "Error",
  "msg": "Connection failed db.local 5432",
  "exception": "System.InvalidOperationException: simulated database error\n   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36",
  "Host": "db.local",
  "Port": 5432
}
```

### MEL

```csharp
mel.LogError(exception, "Connection failed {Host} {Port}", "db.local", 5432);
```

```
2026-03-22 21:43:52.031 fail: Demo[0]
      Connection failed db.local 5432
      System.InvalidOperationException: simulated database error
         at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.032Z",
  "EventId": 0,
  "LogLevel": "Error",
  "Category": "Demo",
  "Message": "Connection failed db.local 5432",
  "Exception": "System.InvalidOperationException: simulated database error\n   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36",
  "State": {
    "Host": "db.local",
    "Port": 5432,
    "{OriginalFormat}": "Connection failed {Host} {Port}"
  },
  "Scopes": []
}
```

### ZLogger

```csharp
zlogger.ZLogError(exception, $"Connection failed {Host} {Port}");
```

```
2026-03-22 21:43:52.032 ERR Connection failed db.local 5432
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "Timestamp": "2026-03-22T21:43:52.032711+01:00",
  "LogLevel": "Error",
  "Category": "comparison",
  "Exception": {
    "Name": "System.InvalidOperationException",
    "Message": "simulated database error",
    "StackTrace": "   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36",
    "InnerException": null
  },
  "Message": "Connection failed db.local 5432",
  "Host": "db.local",
  "Port": 5432
}
```

### log4net

```csharp
log4net.Error("Connection failed", exception);
```

```
2026-03-22 21:43:52.033 ERROR Connection failed
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

```json
{
  "ts": "2026-03-22 20:43:52,033",
  "level": "ERROR",
  "msg": "Connection failed"
}
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

### ZeroLog

```csharp
zerolog.Error()
    .Append("Connection failed")
    .AppendKeyValue("Host", "db.local")
    .AppendKeyValue("Port", 5432)
    .WithException(exception)
    .Log();
```

```
2026-03-22 20:43:52.033 ERROR Connection failed ~~ { "Host": "db.local", "Port": 5432 }
System.InvalidOperationException: simulated database error
   at Program.<Main>$(String[] args) in /Users/gregory/Code/personal/clip/Clip.ComparisonDemo/Program.cs:line 36
```

