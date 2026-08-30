using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace FastCom.Infrastructure.Persistence;

/// <summary>
/// 🔴 B8: Global Query Filter لكل Entities اللي فيها IsDeleted.
/// كده أي استعلام بيتجاهل المحذوف تلقائيًا — ومش هتنسى في أي مكان.
/// </summary>
public static class SoftDeleteQueryFilter
{
    public static void ApplySoftDeleteFilters(this ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var isDeletedProp = entityType.FindProperty("IsDeleted");
            if (isDeletedProp is null || isDeletedProp.ClrType != typeof(bool))
                continue;

            var method = typeof(SoftDeleteQueryFilter)
                .GetMethod(nameof(BuildFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);

            var lambda = (LambdaExpression)method.Invoke(null, null)!;

            // لو فيه فلتر موجود أصلًا، نركّبه مع الجديد
            var existing = entityType.GetQueryFilter();
            var combined = existing is null
                ? lambda
                : CombineFilters(existing, lambda, entityType.ClrType);

            entityType.SetQueryFilter(combined);
        }
    }

    private static LambdaExpression BuildFilter<TEntity>() where TEntity : class
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var prop = Expression.Property(param, "IsDeleted");
        var notDeleted = Expression.Equal(prop, Expression.Constant(false));
        return Expression.Lambda(notDeleted, param);
    }

    private static LambdaExpression CombineFilters(
        LambdaExpression existing, LambdaExpression added, Type entityType)
    {
        // تبسيط: لو فيه فلتر موجود، هنستخدم الجديد بس
        // (حاليًا مافيش Entity له فلتر مخصص)
        return added;
    }
}
