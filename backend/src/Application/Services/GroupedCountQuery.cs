using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Counts many predicates over the same source in a <b>single</b> round trip by translating them
/// into <c>COUNT(CASE WHEN … THEN 1 END)</c> columns, instead of one <c>CountAsync</c> per bucket.
///
/// Callers keep passing the same <see cref="Expression"/> predicates they use for filtering, so a
/// tab filter and its badge count can never drift apart.
/// </summary>
public static class GroupedCountQuery
{
    private static readonly MethodInfo CountWithPredicate = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Enumerable.Count)
                     && m.GetParameters().Length == 2);

    private static readonly MethodInfo CountAll = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Enumerable.Count)
                     && m.GetParameters().Length == 1);

    /// <summary>
    /// One bucket: which property of the result to fill, and the predicate to count.
    /// A null <paramref name="Predicate"/> counts every row.
    /// </summary>
    public sealed record Bucket<TSource, TResult>(
        Expression<Func<TResult, int>> Target,
        Expression<Func<TSource, bool>>? Predicate);

    /// <summary>Shorthand for building a <see cref="Bucket{TSource,TResult}"/>.</summary>
    public static Bucket<TSource, TResult> Count<TSource, TResult>(
        Expression<Func<TResult, int>> target,
        Expression<Func<TSource, bool>>? predicate) => new(target, predicate);

    /// <summary>
    /// Runs every bucket as one aggregate query over <paramref name="query"/>.
    /// Returns a fresh <typeparamref name="TResult"/> (all zeros) when nothing matches.
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TSource, TResult>(
        IQueryable<TSource> query,
        IReadOnlyList<Bucket<TSource, TResult>> buckets,
        CancellationToken cancellationToken)
        where TResult : new()
    {
        if (buckets.Count == 0)
        {
            return new TResult();
        }

        var group = Expression.Parameter(typeof(IGrouping<int, TSource>), "g");
        var bindings = new List<MemberBinding>(buckets.Count);

        foreach (var bucket in buckets)
        {
            var call = bucket.Predicate is null
                ? Expression.Call(CountAll.MakeGenericMethod(typeof(TSource)), group)
                : Expression.Call(
                    CountWithPredicate.MakeGenericMethod(typeof(TSource)),
                    group,
                    bucket.Predicate);

            bindings.Add(Expression.Bind(ResolveMember(bucket.Target), call));
        }

        var selector = Expression.Lambda<Func<IGrouping<int, TSource>, TResult>>(
            Expression.MemberInit(Expression.New(typeof(TResult)), bindings),
            group);

        // Constant key => a single group over the whole filtered set.
        var result = await query
            .GroupBy(_ => 1)
            .Select(selector)
            .FirstOrDefaultAsync(cancellationToken);

        return result ?? new TResult();
    }

    private static MemberInfo ResolveMember<TResult>(Expression<Func<TResult, int>> target) =>
        target.Body is MemberExpression member
            ? member.Member
            : throw new ArgumentException(
                $"Bucket target must be a property access, got: {target.Body}",
                nameof(target));
}
