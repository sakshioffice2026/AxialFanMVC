using AxialFanMVC.Database;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AxialFanMVC.Services
{
    public static class ValidationFlagsBackfill
    {
        public static async Task RunAsync(AxialFanDbContext db)
        {
            var curves = await db.performance_curves
                .Where(c => c.ValidationFlagsJson != null && c.ValidationFlagsJson != "")
                .ToListAsync();

            int updated = 0;
            foreach (var curve in curves)
            {
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(curve.ValidationFlagsJson!);
                }
                catch (JsonException)
                {
                    // Not valid JSON — leave it alone rather than risk corrupting it.
                    continue;
                }

                var recased = ToCamelCaseKeys(node);
                var normalized = recased?.ToJsonString();

                if (normalized != null && normalized != curve.ValidationFlagsJson)
                {
                    curve.ValidationFlagsJson = normalized;
                    updated++;
                }
            }

            if (updated > 0)
                await db.SaveChangesAsync();

            Console.WriteLine($"Backfilled {updated} of {curves.Count} PerformanceCurve rows to camelCase flags.");
        }

        // Recursively lowercases the first letter of every object key, leaving
        // values untouched. Works generically on whatever shape the flags
        // JSON actually has, so it can't silently drop fields the way binding
        // to a specific C# class could.
        private static JsonNode? ToCamelCaseKeys(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var newObj = new JsonObject();
                    foreach (var kvp in obj)
                    {
                        var newKey = ToCamelCase(kvp.Key);
                        newObj[newKey] = ToCamelCaseKeys(kvp.Value?.DeepClone());
                    }
                    return newObj;

                case JsonArray arr:
                    var newArr = new JsonArray();
                    foreach (var item in arr)
                    {
                        newArr.Add(ToCamelCaseKeys(item?.DeepClone()));
                    }
                    return newArr;

                default:
                    return node?.DeepClone();
            }
        }

        private static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}