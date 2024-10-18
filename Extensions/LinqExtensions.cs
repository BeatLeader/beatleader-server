using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using BeatLeader_Server.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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
        [NotParameterized] [CallerLineNumber] int lineNumber = 0)
        {
            return source.Where(_ => $"{filePath}:{lineNumber}" == $"{filePath}:{lineNumber}");
        }
    }
}
