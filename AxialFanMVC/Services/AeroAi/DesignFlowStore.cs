using System.Collections.Concurrent;
using System.Reflection;
using AxialFanMVC.Database;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services.AeroAi
{
    public class OptimizationDraft
    {
        public int SourceResultId { get; set; }
        public int ProjectId { get; set; }
        public DesignInput Input { get; set; } = null!;
        public DesignResult Result { get; set; } = null!;
        public int ProposedResultId { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public class LoadedDesign
    {
        public DesignResult Result { get; set; } = null!;
        public bool FromSessionMemory { get; set; }
    }

    // Step 1 of the AeroAi optimize flow: holds loaded results and the
    // pending optimized draft in per-user session memory, and falls back
    // to EF Core when a result is not in memory yet.
    public static class DesignFlowStore
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

        private sealed class CachedResult
        {
            public DesignResult Result { get; set; } = null!;
            public DateTime ExpiresUtc { get; set; }
        }

        private sealed class UserState
        {
            public readonly object Gate = new();
            public readonly Dictionary<int, CachedResult> Loaded = new();
            public OptimizationDraft? Draft;
            public int? LastResultId;
        }

        private static readonly ConcurrentDictionary<int, UserState> States = new();

        private static UserState StateFor(int userId) => States.GetOrAdd(userId, _ => new UserState());

        public static async Task<int?> ResolveResultIdAsync(AxialFanDbContext db, int userId, int? requestedId)
        {
            if (requestedId.HasValue) return requestedId.Value;

            var state = StateFor(userId);
            lock (state.Gate)
            {
                if (state.LastResultId.HasValue) return state.LastResultId.Value;
            }

            var latest = await db.design_results
                .AsNoTracking()
                .Where(r => r.DesignInput.Project.UserId == userId)
                .OrderByDescending(r => r.CalculatedAt)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();

            return latest;
        }

        public static async Task<LoadedDesign?> GetLoadedResultAsync(AxialFanDbContext db, int userId, int resultId)
        {
            var state = StateFor(userId);

            lock (state.Gate)
            {
                if (state.Loaded.TryGetValue(resultId, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
                {
                    state.LastResultId = resultId;
                    return new LoadedDesign { Result = cached.Result, FromSessionMemory = true };
                }
            }

            var result = await db.design_results
                .AsNoTracking()
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.Project)
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.BladeProfile)
                .FirstOrDefaultAsync(r => r.Id == resultId && r.DesignInput.Project.UserId == userId);

            if (result == null) return null;

            lock (state.Gate)
            {
                state.Loaded[resultId] = new CachedResult { Result = result, ExpiresUtc = DateTime.UtcNow.Add(Ttl) };
                state.LastResultId = resultId;
            }

            return new LoadedDesign { Result = result, FromSessionMemory = false };
        }

        public static void Invalidate(int userId, int resultId)
        {
            var state = StateFor(userId);
            lock (state.Gate) { state.Loaded.Remove(resultId); }
        }

        public static void SetLastResult(int userId, int resultId)
        {
            var state = StateFor(userId);
            lock (state.Gate) { state.LastResultId = resultId; }
        }

        public static void PutDraft(int userId, OptimizationDraft draft)
        {
            var state = StateFor(userId);
            lock (state.Gate) { state.Draft = draft; }
        }

        public static bool HasDraft(int userId)
        {
            var state = StateFor(userId);
            lock (state.Gate)
            {
                if (state.Draft == null) return false;
                if (state.Draft.CreatedUtc.Add(Ttl) < DateTime.UtcNow) { state.Draft = null; return false; }
                return true;
            }
        }

        public static OptimizationDraft? TakeDraft(int userId)
        {
            var state = StateFor(userId);
            lock (state.Gate)
            {
                var draft = state.Draft;
                state.Draft = null;
                if (draft != null && draft.CreatedUtc.Add(Ttl) < DateTime.UtcNow) return null;
                return draft;
            }
        }

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ScalarProps = new();

        // Copies every scalar (value-type / string) property; navigation
        // properties are intentionally left null so the copy is never
        // tied to an EF change tracker.
        public static T CloneScalars<T>(T source) where T : class, new()
        {
            var props = ScalarProps.GetOrAdd(typeof(T), t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
                .ToArray());

            var copy = new T();
            foreach (var p in props)
                p.SetValue(copy, p.GetValue(source));
            return copy;
        }
    }
}