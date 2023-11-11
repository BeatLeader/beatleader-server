using System.Linq.Expressions;
using BeatLeader_Server.Enums;

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
    }
}
