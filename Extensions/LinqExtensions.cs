using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Z.BulkOperations;

namespace BeatLeader_Server.Extensions
{
    public static class LinqExtensions
    {

        public static IOrderedQueryable<TSource> Order<TSource, TKey>(this IQueryable<TSource> source, Order by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == Enums.Order.Desc)
            {
                return source.OrderByDescending(keySelector);
            } else
            {
                return source.OrderBy(keySelector);
            }
        }
        public static IOrderedQueryable<TSource> ThenOrder<TSource, TKey>(this IOrderedQueryable<TSource> source, Order by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == Enums.Order.Desc)
            {
                return source.ThenByDescending(keySelector);
            }
            else
            {
                return source.ThenBy(keySelector);
            }
        }

        public static IQueryable<T> TagWithCaller<T>(
        this IQueryable<T> source,
        [NotParameterized] [CallerFilePath] string? filePath = null,
        [NotParameterized] [CallerLineNumber] int lineNumber = 0) where T : TrackedEntity
        {
            //return source;
            var currentLine = $"{filePath.Split("\\").Last()}:{lineNumber}";

            var toString = 1.GetType().GetMethod("ToString", new System.Type[] { });

            var entity = Expression.Parameter(typeof(T), "s");
            var exp = Expression.NotEqual(Expression.Call(Expression.Property(entity, "Id"), toString), Expression.Constant(currentLine));
            return source.Where((Expression<Func<T, bool>>)Expression.Lambda(exp, entity));
        }

        public static IQueryable<T> TagWithCallerS<T>(
        this IQueryable<T> source,
        [NotParameterized] [CallerFilePath] string? filePath = null,
        [NotParameterized] [CallerLineNumber] int lineNumber = 0) where T : StringTrackedEntity
        {
            //return source;
            var currentLine = $"{filePath.Split("\\").Last()}:{lineNumber}";

            var entity = Expression.Parameter(typeof(T), "s");
            var exp = Expression.NotEqual(Expression.Property(entity, "Id"), Expression.Constant(currentLine));
            return source.Where((Expression<Func<T, bool>>)Expression.Lambda(exp, entity));
        }

        public static async Task SafeBulkUpdateAsync<T>(this DbContext @this, IEnumerable<T> entities, Action<BulkOperation<T>> options, int attempt = 1) where T : class {
            try { 
                await @this.BulkUpdateAsync(entities, options);
            } catch (Exception e) {
                if (attempt < 5) {
                    Console.WriteLine($"EXCEPTION SafeBulkUpdateAsync Retry {attempt}");
                    await @this.SafeBulkUpdateAsync(entities, options, attempt + 1);
                } else {
                    Console.WriteLine($"EXCEPTION SafeBulkUpdateAsync {e}");
                }
            }
        }
    }
}
