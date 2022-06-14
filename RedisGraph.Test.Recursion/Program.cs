using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RedisGraph.Test.Recursion;

record Node
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
}

internal class Program
{
    private const string ConnectionString = "redis:6379";
    private const string Graph = "test-graph";

    private static NRedisGraph.RedisGraph context;
    private static ConnectionMultiplexer multiplexer;

    private static async Task CleanCacheAsync()
    {
        await context.QueryAsync(Graph, "MATCH (n) DETACH DELETE n");
    }

    private static Task<int> CountGraphFromRootAsync()
    {
        return context.QueryAsync(Graph, "MATCH (:Group { Id: 1 })<-[:PARENT*0..]-(group:Group) RETURN group")
            .ContinueWith(task => task.Result.Count);
    }

    private static async Task Main(string[] args)
    {
        ConfigurationOptions options = new()
        {
            AbortOnConnectFail = false,
            ConnectRetry = 10,
            AllowAdmin = true
        };

        options.EndPoints.Add(ConnectionString);

        multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        context = new NRedisGraph.RedisGraph(multiplexer.GetDatabase());

        // Clean
        await CleanCacheAsync();

        // Seed
        int added = await SeedCacheAsync();

        // Read node count
        int count = await CountGraphFromRootAsync();

        Console.WriteLine($"Read {count} expected {added}\n");

        //Console.WriteLine("Press any key to restart Redis");
        //Console.ReadKey();

        // Restart redis and persist
        multiplexer.GetServer(multiplexer.GetEndPoints().First()).Shutdown(ShutdownMode.Always);

        Console.WriteLine("Waiting 30 seconds\n");

        // Wait for restart
        await Task.Delay(TimeSpan.FromSeconds(30));

        // Ensure reconnect
        multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        context = new NRedisGraph.RedisGraph(multiplexer.GetDatabase());

        // Read from cache
        count = await CountGraphFromRootAsync();

        Console.WriteLine($"Read {count} expected {added}");
    }

    private static async Task<int> SeedCacheAsync()
    {
        List<Node> nodes = new()
        {
            new()
            {
                Id = 1,
                ParentId = null
            }
        };

        void AddChildren(Node parent, int childrenCount, int maxDepth, int currentDepth = 0)
        {
            for (int i = 0; i < childrenCount; i++)
            {
                var node = new Node()
                {
                    Id = nodes.Count,
                    ParentId = parent.Id
                };

                nodes.Add(node);

                if (currentDepth < maxDepth)
                {
                    AddChildren(node, childrenCount, maxDepth, currentDepth + 1);
                }
            }
        }

        AddChildren(nodes.First(), 3, 5);

        await Parallel.ForEachAsync(nodes, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (node, ct) =>
        {
            // Create node
            await context.QueryAsync(Graph, "MERGE (:Group { Id: $Id })", new Dictionary<string, object>() { { "Id", node.Id } });

            if (node.ParentId.HasValue)
            {
                Dictionary<string, object> values = new()
                {
                    { "Id", node.Id },
                    { "ParentId", node.ParentId.Value }
                };

                // Ensure parent exists
                await context.QueryAsync(Graph, "MERGE (:Group { Id: $Id })", new Dictionary<string, object>() { { "Id", node.ParentId.Value } });

                // Create relation and delete other existing relations
                await context.QueryAsync(Graph, "MATCH (group:Group { Id: $Id }), (parent:Group { Id: $ParentId }) MERGE (group)-[r:PARENT]->(parent) WITH group, r MATCH (group)-[rel:PARENT]->(:Group) WHERE rel <> r DELETE rel", values);
            }
        });

        return nodes.Count;
    }
}