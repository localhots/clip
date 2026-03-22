using Clip;

var useJson = args.Length > 0 && args[0].Equals("json", StringComparison.OrdinalIgnoreCase);

var logger = Logger.Create(c =>
{
    c.MinimumLevel(LogLevel.Trace);
    if (useJson)
        c.WriteTo.Json(Console.OpenStandardError());
    else
        c.WriteTo.Console();
});


logger.Trace("Loading configuration", new { Path = "/etc/myapp/config.yaml" });
logger.Debug("Configuration loaded",
    new { Env = "production", Region = "us-east-1", Workers = 8, DebugMode = false });
logger.Info("Connected to database", new { Host = "db-primary.internal", Port = 5432, Pool = 20 });
logger.Info("Server listening", new { Bind = "0.0.0.0", Port = 8080 });


using (Logger.AddContext(new { RequestId = "req-a1b2c3", Method = "GET", Path = "/api/users" }))
{
    logger.Info("Request received");
    logger.Debug("Authentication verified", new { UserId = 1042, Role = "admin" });
    logger.Info("Response sent", new { Status = 200, Bytes = 4820, DurationMs = 12.4 });
}


using (Logger.AddContext(new { RequestId = "req-d4e5f6", Method = "POST", Path = "/api/orders" }))
{
    logger.Info("Request received");
    logger.Debug("Payload validated",
        new Field("ContentType", "application/json"), new Field("Bytes", 384));
    logger.Info("Order created",
        new Field("OrderId", 90471L), new Field("Total", 129.99), new Field("Items", 3));
    logger.Info("Response sent",
        new Field("Status", 201), new Field("DurationMs", 47.2));
}


logger.Warning("Memory pressure detected",
    new { UsedMb = 3891, LimitMb = 4096, Utilization = 0.95 });
logger.Warning("Connection pool near capacity",
    new { Active = 18, Max = 20, WaitingRequests = 4 });


using (Logger.AddContext(new { RequestId = "req-g7h8i9", Method = "GET", Path = "/api/orders/90471" }))
{
    logger.Info("Request received");

    try
    {
        throw new TimeoutException("Query exceeded 30s timeout (query: SELECT * FROM orders WHERE id = 90471)");
    }
    catch (Exception ex)
    {
        logger.Error("Database query failed", ex, new { Query = "GetOrderById", TimeoutSec = 30 });
    }

    logger.Error("Request failed", new { Status = 503, Reason = "upstream_timeout" });
}


logger.Fatal("Out of memory — shutting down", new { HeapBytes = 4_294_967_296L, Pid = 48201 });
